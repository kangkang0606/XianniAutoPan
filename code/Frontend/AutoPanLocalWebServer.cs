using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using XianniAutoPan.Model;
using XianniAutoPan.Services;

namespace XianniAutoPan.Frontend
{
    /// <summary>
    /// 自动盘本地网页与 WebSocket 服务（基于 TcpListener，兼容 Unity/Mono 运行时）。
    /// IPAddress.Any 自动监听所有网卡，hostname/IP/localhost 均可访问，无需管理员权限。
    /// </summary>
    internal sealed class AutoPanLocalWebServer
    {
        /// <summary>
        /// WebSocket 会话，封装网络流与写锁。
        /// </summary>
        private sealed class WsSession
        {
            /// <summary>
            /// 底层 TCP 网络流。
            /// </summary>
            public NetworkStream Stream;

            /// <summary>
            /// 写操作信号量，防止并发写入导致帧损坏。
            /// </summary>
            public SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);

            /// <summary>
            /// 会话是否仍处于活跃状态。
            /// </summary>
            public volatile bool Alive = true;
        }

        private sealed class FrontendSocketRequest
        {
            /// <summary>
            /// 玩家唯一标识。
            /// </summary>
            public string userId { get; set; }

            /// <summary>
            /// 玩家显示名。
            /// </summary>
            public string playerName { get; set; }

            /// <summary>
            /// 待发送指令文本。
            /// </summary>
            public string text { get; set; }
        }

        /// <summary>
        /// WebSocket 帧。
        /// </summary>
        private sealed class WsFrame
        {
            /// <summary>
            /// 操作码：1=文本，8=关闭，9=Ping，10=Pong。
            /// </summary>
            public int Opcode;

            /// <summary>
            /// 已解码载荷。
            /// </summary>
            public byte[] Payload;
        }

        /// <summary>
        /// HTTP 请求头解析结果。
        /// </summary>
        private sealed class HttpHeaderResult
        {
            /// <summary>
            /// 请求行，例如 "GET / HTTP/1.1"。
            /// </summary>
            public string RequestLine;

            /// <summary>
            /// 请求头字典（键不区分大小写）。
            /// </summary>
            public Dictionary<string, string> Headers;
        }

        private const string WsAcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int MaxHeaderBytes = 8192;
        private static readonly JsonSerializerSettings CamelCaseJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        /// <summary>
        /// 全局单例。
        /// </summary>
        public static AutoPanLocalWebServer Instance { get; } = new AutoPanLocalWebServer();

        private readonly object _sync = new object();
        private readonly ConcurrentQueue<FrontendInboundMessage> _pendingMessages = new ConcurrentQueue<FrontendInboundMessage>();
        private readonly ConcurrentDictionary<string, WsSession> _sessions = new ConcurrentDictionary<string, WsSession>();
        private TcpListener _tcpListener;
        private CancellationTokenSource _cts;
        private string _frontendFolder;
        private string _commandBookPath;
        private int _lastPort = -1;
        private string _lastBindHost = string.Empty;
        private List<string> _listenAddresses = new List<string>();

        /// <summary>
        /// 当前服务是否处于运行状态。
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 初始化路径信息。
        /// </summary>
        public void Initialize(string modFolder)
        {
            _frontendFolder = Path.Combine(modFolder, "frontend");
            _commandBookPath = Path.Combine(modFolder, "指令书.txt");
        }

        /// <summary>
        /// 根据当前配置启动或重启本地服务。
        /// </summary>
        public void UpdateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_frontendFolder))
            {
                return;
            }

            if (IsRunning && _lastPort == AutoPanConfigHooks.HttpPort && string.Equals(_lastBindHost, AutoPanConfigHooks.BindHost, StringComparison.Ordinal))
            {
                return;
            }

            Restart();
        }

        /// <summary>
        /// 关闭服务。
        /// </summary>
        public void Stop()
        {
            lock (_sync)
            {
                IsRunning = false;
                _lastPort = -1;
                _lastBindHost = string.Empty;
                _listenAddresses = new List<string>();
                try
                {
                    _cts?.Cancel();
                    _tcpListener?.Stop();
                }
                catch
                {
                }
                finally
                {
                    _tcpListener = null;
                    _cts = null;
                }
            }
        }

        /// <summary>
        /// 获取当前待处理前端消息。
        /// </summary>
        public bool TryDequeueMessage(out FrontendInboundMessage message)
        {
            return _pendingMessages.TryDequeue(out message);
        }

        /// <summary>
        /// 获取当前监听地址快照。
        /// </summary>
        public IReadOnlyList<string> GetListenAddresses()
        {
            lock (_sync)
            {
                return new List<string>(_listenAddresses);
            }
        }

        /// <summary>
        /// 向指定会话发送回包。
        /// </summary>
        public void SendReply(string sessionId, AutoPanCommandResult result)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (!_sessions.TryGetValue(sessionId, out WsSession session) || !session.Alive)
            {
                return;
            }

            AutoPanBindingRecord binding = AutoPanStateRepository.GetBindingSnapshot(result.UserId);
            string payload = JsonConvert.SerializeObject(new
            {
                type = "reply",
                ok = result.Success,
                text = result.Text,
                sequence = result.Sequence,
                binding,
                aiEnabled = AutoPanConfigHooks.EnableLlmAi
            }, CamelCaseJsonSettings);
            _ = SendWsTextAsync(sessionId, session, payload);
        }

        // ── 生命周期 ──

        private void Restart()
        {
            Stop();

            int port = AutoPanConfigHooks.HttpPort;
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Any, port);
                listener.Start();

                List<string> displayAddresses = CollectDisplayAddresses(port);
                lock (_sync)
                {
                    _tcpListener = listener;
                    _cts = new CancellationTokenSource();
                    _listenAddresses = displayAddresses;
                    _lastPort = port;
                    _lastBindHost = AutoPanConfigHooks.BindHost;
                    IsRunning = true;
                }

                AutoPanLogService.Info($"网页前端已启动：{string.Join("，", displayAddresses)}");
                _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"启动网页前端失败：{ex.Message}");
                Stop();
            }
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                client.NoDelay = true;
                string remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                NetworkStream stream = client.GetStream();

                HttpHeaderResult headerResult = await ReadHttpHeadersAsync(stream, token).ConfigureAwait(false);
                if (headerResult == null)
                {
                    return;
                }

                string[] parts = headerResult.RequestLine.Split(' ');
                if (parts.Length < 2)
                {
                    return;
                }

                string rawPath = parts[1];
                string path = Uri.UnescapeDataString(rawPath.Split('?')[0]);

                if (IsWebSocketUpgrade(headerResult.Headers) && string.Equals(path, "/ws", StringComparison.OrdinalIgnoreCase))
                {
                    // WebSocket 连接期间不关闭 TcpClient，由 HandleWebSocketAsync 管理
                    await HandleWebSocketAsync(client, stream, headerResult.Headers, remoteEndPoint, token).ConfigureAwait(false);
                    return;
                }

                await HandleHttpAsync(stream, path, rawPath, token).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }
        }

        // ── HTTP 处理 ──

        private async Task HandleHttpAsync(NetworkStream stream, string path, string rawPath, CancellationToken token)
        {
            if (string.Equals(path, "/", StringComparison.OrdinalIgnoreCase))
            {
                await ServeFileAsync(stream, Path.Combine(_frontendFolder, "index.html"), "text/html; charset=utf-8", token).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/app.js", StringComparison.OrdinalIgnoreCase))
            {
                await ServeFileAsync(stream, Path.Combine(_frontendFolder, "app.js"), "application/javascript; charset=utf-8", token).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/style.css", StringComparison.OrdinalIgnoreCase))
            {
                await ServeFileAsync(stream, Path.Combine(_frontendFolder, "style.css"), "text/css; charset=utf-8", token).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/指令书.txt", StringComparison.OrdinalIgnoreCase))
            {
                await ServeFileAsync(stream, _commandBookPath, "text/plain; charset=utf-8", token).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/dashboard", StringComparison.OrdinalIgnoreCase))
            {
                string userId = ExtractQueryParam(rawPath, "userId");
                AutoPanDashboardSnapshot snapshot = AutoPanStateRepository.CreateDashboardSnapshot(userId, GetListenAddresses(), IsRunning, GetCommandBookText());
                await ServeJsonAsync(stream, snapshot, token).ConfigureAwait(false);
                return;
            }

            await WriteHttpResponseAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"), token).ConfigureAwait(false);
        }

        private async Task ServeFileAsync(NetworkStream stream, string filePath, string contentType, CancellationToken token)
        {
            if (!File.Exists(filePath))
            {
                await WriteHttpResponseAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"), token).ConfigureAwait(false);
                return;
            }

            byte[] body = File.ReadAllBytes(filePath);
            await WriteHttpResponseAsync(stream, 200, contentType, body, token).ConfigureAwait(false);
        }

        private async Task ServeJsonAsync(NetworkStream stream, object payload, CancellationToken token)
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload, CamelCaseJsonSettings));
            await WriteHttpResponseAsync(stream, 200, "application/json; charset=utf-8", body, token).ConfigureAwait(false);
        }

        private static async Task WriteHttpResponseAsync(NetworkStream stream, int statusCode, string contentType, byte[] body, CancellationToken token)
        {
            string statusText = statusCode == 200 ? "OK" : statusCode == 404 ? "Not Found" : "Error";
            string header = $"HTTP/1.1 {statusCode} {statusText}\r\n"
                + $"Content-Type: {contentType}\r\n"
                + $"Content-Length: {body.Length}\r\n"
                + "Connection: close\r\n"
                + "\r\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        // ── WebSocket 处理 ──

        private async Task HandleWebSocketAsync(TcpClient client, NetworkStream stream, Dictionary<string, string> headers, string remoteEndPoint, CancellationToken token)
        {
            headers.TryGetValue("sec-websocket-key", out string wsKey);
            if (string.IsNullOrWhiteSpace(wsKey))
            {
                await WriteHttpResponseAsync(stream, 400, "text/plain", Encoding.UTF8.GetBytes("Missing Sec-WebSocket-Key"), token).ConfigureAwait(false);
                return;
            }

            // 完成 WebSocket 握手
            string acceptKey;
            using (SHA1 sha1 = SHA1.Create())
            {
                acceptKey = Convert.ToBase64String(sha1.ComputeHash(Encoding.UTF8.GetBytes(wsKey.Trim() + WsAcceptGuid)));
            }

            string upgradeResponse = "HTTP/1.1 101 Switching Protocols\r\n"
                + "Upgrade: websocket\r\n"
                + "Connection: Upgrade\r\n"
                + $"Sec-WebSocket-Accept: {acceptKey}\r\n"
                + "\r\n";
            byte[] upgradeBytes = Encoding.UTF8.GetBytes(upgradeResponse);
            await stream.WriteAsync(upgradeBytes, 0, upgradeBytes.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);

            string sessionId = Guid.NewGuid().ToString("N");
            WsSession session = new WsSession { Stream = stream };
            _sessions[sessionId] = session;

            await SendWsTextAsync(sessionId, session, JsonConvert.SerializeObject(new { type = "hello", sessionId }, CamelCaseJsonSettings));

            try
            {
                while (!token.IsCancellationRequested && session.Alive)
                {
                    WsFrame frame = await ReadWsFrameAsync(stream, token).ConfigureAwait(false);
                    if (frame == null || frame.Opcode == 8)
                    {
                        break;
                    }

                    // Ping → Pong
                    if (frame.Opcode == 9)
                    {
                        await WriteWsFrameAsync(session, 10, frame.Payload, token).ConfigureAwait(false);
                        continue;
                    }

                    // 文本帧
                    if (frame.Opcode == 1)
                    {
                        string payload = Encoding.UTF8.GetString(frame.Payload);
                        FrontendSocketRequest request = null;
                        try
                        {
                            request = JsonConvert.DeserializeObject<FrontendSocketRequest>(payload);
                        }
                        catch (Exception ex)
                        {
                            await SendWsTextAsync(sessionId, session, JsonConvert.SerializeObject(new
                            {
                                type = "reply",
                                ok = false,
                                text = "消息 JSON 解析失败：" + ex.Message
                            }, CamelCaseJsonSettings));
                            continue;
                        }

                        if (request == null || string.IsNullOrWhiteSpace(request.text))
                        {
                            continue;
                        }

                        _pendingMessages.Enqueue(new FrontendInboundMessage
                        {
                            SessionId = sessionId,
                            UserId = request.userId,
                            PlayerName = request.playerName,
                            Text = request.text,
                            RemoteEndPoint = remoteEndPoint
                        });
                    }
                }
            }
            finally
            {
                session.Alive = false;
                _sessions.TryRemove(sessionId, out _);
                try
                {
                    await WriteWsFrameAsync(session, 8, new byte[0], CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    client.Close();
                }
                catch
                {
                }
            }
        }

        // ── WebSocket 帧编解码 ──

        /// <summary>
        /// 从流中读取一个完整的 WebSocket 帧。
        /// </summary>
        private static async Task<WsFrame> ReadWsFrameAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] head = new byte[2];
            if (!await ReadExactAsync(stream, head, 2, token))
            {
                return null;
            }

            int opcode = head[0] & 0x0F;
            bool masked = (head[1] & 0x80) != 0;
            long payloadLen = head[1] & 0x7F;

            if (payloadLen == 126)
            {
                byte[] ext = new byte[2];
                if (!await ReadExactAsync(stream, ext, 2, token))
                {
                    return null;
                }

                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLen == 127)
            {
                byte[] ext = new byte[8];
                if (!await ReadExactAsync(stream, ext, 8, token))
                {
                    return null;
                }

                payloadLen = 0;
                for (int i = 0; i < 8; i++)
                {
                    payloadLen = (payloadLen << 8) | ext[i];
                }
            }

            byte[] maskKey = null;
            if (masked)
            {
                maskKey = new byte[4];
                if (!await ReadExactAsync(stream, maskKey, 4, token))
                {
                    return null;
                }
            }

            byte[] payload = new byte[payloadLen];
            if (payloadLen > 0 && !await ReadExactAsync(stream, payload, (int)payloadLen, token))
            {
                return null;
            }

            if (masked && maskKey != null)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] ^= maskKey[i % 4];
                }
            }

            return new WsFrame { Opcode = opcode, Payload = payload };
        }

        /// <summary>
        /// 向 WebSocket 会话写入一帧（含写锁保护）。
        /// </summary>
        private static async Task WriteWsFrameAsync(WsSession session, int opcode, byte[] payload, CancellationToken token)
        {
            int len = payload?.Length ?? 0;
            byte[] frame;
            using (MemoryStream ms = new MemoryStream())
            {
                ms.WriteByte((byte)(0x80 | (opcode & 0x0F)));

                if (len < 126)
                {
                    ms.WriteByte((byte)len);
                }
                else if (len <= 65535)
                {
                    ms.WriteByte(126);
                    ms.WriteByte((byte)((len >> 8) & 0xFF));
                    ms.WriteByte((byte)(len & 0xFF));
                }
                else
                {
                    ms.WriteByte(127);
                    for (int i = 7; i >= 0; i--)
                    {
                        ms.WriteByte((byte)((len >> (i * 8)) & 0xFF));
                    }
                }

                if (len > 0)
                {
                    ms.Write(payload, 0, len);
                }

                frame = ms.ToArray();
            }

            await session.WriteLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await session.Stream.WriteAsync(frame, 0, frame.Length, token).ConfigureAwait(false);
                await session.Stream.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                session.WriteLock.Release();
            }
        }

        /// <summary>
        /// 向 WebSocket 会话发送文本消息。
        /// </summary>
        private async Task SendWsTextAsync(string sessionId, WsSession session, string text)
        {
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(text);
                await WriteWsFrameAsync(session, 1, payload, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                session.Alive = false;
                _sessions.TryRemove(sessionId, out _);
            }
        }

        // ── HTTP 请求解析 ──

        /// <summary>
        /// 从 TCP 流中读取 HTTP 请求行与头部。
        /// </summary>
        private static async Task<HttpHeaderResult> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken token)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            byte[] buffer = new byte[MaxHeaderBytes];
            int total = 0;
            int headerEnd = -1;

            while (total < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer, total, buffer.Length - total, token).ConfigureAwait(false);
                if (n == 0)
                {
                    return null;
                }

                total += n;

                // 搜索 \r\n\r\n 边界
                for (int i = Math.Max(0, total - n - 3); i <= total - 4; i++)
                {
                    if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                    {
                        headerEnd = i;
                        break;
                    }
                }

                if (headerEnd >= 0)
                {
                    break;
                }
            }

            if (headerEnd < 0)
            {
                return null;
            }

            string headerSection = Encoding.UTF8.GetString(buffer, 0, headerEnd);
            string[] lines = headerSection.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                return null;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                int colonIdx = lines[i].IndexOf(':');
                if (colonIdx > 0)
                {
                    string key = lines[i].Substring(0, colonIdx).Trim();
                    string value = lines[i].Substring(colonIdx + 1).Trim();
                    headers[key] = value;
                }
            }

            return new HttpHeaderResult { RequestLine = lines[0], Headers = headers };
        }

        // ── 工具方法 ──

        /// <summary>
        /// 从流中精确读取指定字节数。
        /// </summary>
        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer, read, count - read, token).ConfigureAwait(false);
                if (n == 0)
                {
                    return false;
                }

                read += n;
            }

            return true;
        }

        /// <summary>
        /// 检测 HTTP 请求是否为 WebSocket 升级。
        /// </summary>
        private static bool IsWebSocketUpgrade(Dictionary<string, string> headers)
        {
            headers.TryGetValue("Connection", out string connection);
            headers.TryGetValue("Upgrade", out string upgrade);
            return connection != null
                && connection.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0
                && string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从 URL 路径中提取查询参数。
        /// </summary>
        private static string ExtractQueryParam(string rawPath, string paramName)
        {
            int qIdx = rawPath.IndexOf('?');
            if (qIdx < 0)
            {
                return string.Empty;
            }

            string query = rawPath.Substring(qIdx + 1);
            string prefix = paramName + "=";
            foreach (string pair in query.Split('&'))
            {
                if (pair.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair.Substring(prefix.Length));
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 收集当前机器所有可用访问地址（仅用于日志/前端展示）。
        /// </summary>
        private static List<string> CollectDisplayAddresses(int port)
        {
            HashSet<string> addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"http://localhost:{port}"
            };

            try
            {
                string hostname = Dns.GetHostName();
                if (!string.IsNullOrWhiteSpace(hostname))
                {
                    addresses.Add($"http://{hostname}:{port}");
                }

                foreach (IPAddress ip in Dns.GetHostEntry(hostname).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        addresses.Add($"http://{ip}:{port}");
                    }
                }
            }
            catch
            {
            }

            return addresses.ToList();
        }

        private string GetCommandBookText()
        {
            if (!File.Exists(_commandBookPath))
            {
                return "指令书.txt 缺失。";
            }

            return File.ReadAllText(_commandBookPath, Encoding.UTF8);
        }
    }
}
