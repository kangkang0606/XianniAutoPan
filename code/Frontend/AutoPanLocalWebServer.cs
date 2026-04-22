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
        private readonly ConcurrentDictionary<string, WsSession> _oneBotSessions = new ConcurrentDictionary<string, WsSession>();
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
        public void SendReply(FrontendInboundMessage sourceMessage, AutoPanCommandResult result)
        {
            if (sourceMessage == null || result == null)
            {
                return;
            }

            if (sourceMessage.SourceType == AutoPanInputSourceType.QqGroup)
            {
                if (!AutoPanQqBridgeService.TryBuildReplyPlan(sourceMessage, result, out string replySessionId, out string groupId, out List<string> chunks))
                {
                    return;
                }

                if (!_oneBotSessions.TryGetValue(replySessionId, out WsSession qqSession) || !qqSession.Alive)
                {
                    AutoPanLogService.Error($"QQ 回包失败：未找到可用 OneBot 会话 {replySessionId}。");
                    return;
                }

                _ = Task.Run(async () =>
                {
                    foreach (string chunk in chunks)
                    {
                        await SendOneBotGroupMessageAsync(replySessionId, qqSession, groupId, chunk).ConfigureAwait(false);
                        await Task.Delay(400).ConfigureAwait(false);
                    }
                });
                return;
            }

            if (string.IsNullOrWhiteSpace(sourceMessage.SessionId))
            {
                return;
            }

            if (!_sessions.TryGetValue(sourceMessage.SessionId, out WsSession session) || !session.Alive)
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
            _ = SendWsTextAsync(sourceMessage.SessionId, session, payload);
        }

        /// <summary>
        /// 按最近 QQ 会话向指定玩家发送系统通知。
        /// </summary>
        public void SendQqNotice(AutoPanSessionInfo sessionInfo, string userId, string text)
        {
            if (sessionInfo == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string groupId = AutoPanQqBridgeService.NormalizeQqDigits(sessionInfo.ContextId);
            if (string.IsNullOrWhiteSpace(groupId) || !AutoPanConfigHooks.IsQqGroupAllowed(groupId))
            {
                return;
            }

            FrontendInboundMessage sourceMessage = new FrontendInboundMessage
            {
                SessionId = sessionInfo.SessionId,
                UserId = userId,
                PlayerName = sessionInfo.PlayerName,
                SourceType = AutoPanInputSourceType.QqGroup,
                ReplyTargetId = groupId,
                ContextId = groupId,
                BotSelfId = sessionInfo.BotSelfId
            };
            SendReply(sourceMessage, new AutoPanCommandResult
            {
                Success = true,
                Text = text,
                UserId = userId
            });
        }

        /// <summary>
        /// 向 QQ 群直接发送原始 OneBot 消息，允许包含 CQ 码图片。
        /// </summary>
        public bool SendQqGroupRawMessage(FrontendInboundMessage sourceMessage, string text)
        {
            if (sourceMessage == null || sourceMessage.SourceType != AutoPanInputSourceType.QqGroup || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string groupId = AutoPanQqBridgeService.NormalizeQqDigits(sourceMessage.ReplyTargetId);
            if (string.IsNullOrWhiteSpace(groupId))
            {
                groupId = AutoPanQqBridgeService.NormalizeQqDigits(sourceMessage.ContextId);
            }

            if (string.IsNullOrWhiteSpace(groupId))
            {
                AutoPanLogService.Error("QQ 原始消息发送失败：缺少群号。");
                return false;
            }

            if (!AutoPanConfigHooks.IsQqGroupAllowed(groupId))
            {
                return false;
            }

            if (!AutoPanQqBridgeService.TryResolveReplySessionId(sourceMessage.SessionId, sourceMessage.BotSelfId, out string replySessionId))
            {
                AutoPanLogService.Error($"QQ 原始消息发送失败：未找到可用 OneBot 会话，group={groupId}。");
                return false;
            }

            if (!_oneBotSessions.TryGetValue(replySessionId, out WsSession qqSession) || !qqSession.Alive)
            {
                AutoPanLogService.Error($"QQ 原始消息发送失败：会话 {replySessionId} 不可用。");
                return false;
            }

            _ = Task.Run(() => SendOneBotGroupMessageAsync(replySessionId, qqSession, groupId, text));
            return true;
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

                if (LooksLikeWebSocketRequest(headerResult.Headers) && IsSameWebSocketPath(path, "/ws"))
                {
                    // WebSocket 连接期间不关闭 TcpClient，由 HandleFrontendWebSocketAsync 管理
                    await HandleFrontendWebSocketAsync(client, stream, headerResult.Headers, remoteEndPoint, token).ConfigureAwait(false);
                    return;
                }

                if (LooksLikeWebSocketRequest(headerResult.Headers) && IsSameWebSocketPath(path, AutoPanConfigHooks.QqOneBotWsPath))
                {
                    string queryToken = ExtractQueryParam(rawPath, "access_token");
                    if (string.IsNullOrWhiteSpace(queryToken))
                    {
                        queryToken = ExtractQueryParam(rawPath, "token");
                    }

                    if (!string.IsNullOrWhiteSpace(queryToken) && !headerResult.Headers.ContainsKey("Authorization"))
                    {
                        headerResult.Headers["Authorization"] = "Bearer " + queryToken.Trim();
                    }

                    // OneBot 反向 WebSocket 连接期间不关闭 TcpClient，由 HandleOneBotWebSocketAsync 管理
                    await HandleOneBotWebSocketAsync(client, stream, headerResult.Headers, remoteEndPoint, token).ConfigureAwait(false);
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

            if (path.StartsWith("/api/qq-config/set", StringComparison.OrdinalIgnoreCase))
            {
                string key = ExtractQueryParam(rawPath, "key");
                string value = ExtractQueryParam(rawPath, "value");
                bool success = AutoPanConfigHooks.TrySetQqSetting(key, value, out string message);
                await ServeJsonAsync(stream, new
                {
                    ok = success,
                    text = message,
                    qqBridge = AutoPanQqBridgeService.BuildDashboardSnapshot()
                }, token).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/policy/set", StringComparison.OrdinalIgnoreCase))
            {
                string key = ExtractQueryParam(rawPath, "key");
                string value = ExtractQueryParam(rawPath, "value");
                bool success = AutoPanConfigHooks.TrySetPolicy(global::XianniAutoPan.XianniAutoPanMain.Instance.GetConfig(), key, value, out string message);
                await ServeJsonAsync(stream, new
                {
                    ok = success,
                    text = message,
                    policy = AutoPanConfigHooks.BuildPolicySnapshot()
                }, token).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/policy-random/set", StringComparison.OrdinalIgnoreCase))
            {
                string key = ExtractQueryParam(rawPath, "key");
                string enabled = ExtractQueryParam(rawPath, "enabled");
                string minValue = ExtractQueryParam(rawPath, "min");
                string maxValue = ExtractQueryParam(rawPath, "max");
                bool success = AutoPanConfigHooks.TrySetPolicyRandom(key, enabled, minValue, maxValue, out string message);
                await ServeJsonAsync(stream, new
                {
                    ok = success,
                    text = message,
                    policy = AutoPanConfigHooks.BuildPolicySnapshot()
                }, token).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/speed-schedule/set", StringComparison.OrdinalIgnoreCase))
            {
                string value = ExtractQueryParam(rawPath, "value");
                bool success = AutoPanConfigHooks.TrySetWorldSpeedSchedule(value, out string message);
                await ServeJsonAsync(stream, new
                {
                    ok = success,
                    text = message,
                    speedSchedule = AutoPanWorldSpeedService.BuildScheduleSnapshot()
                }, token).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/speed-schedule-enabled/set", StringComparison.OrdinalIgnoreCase))
            {
                string value = ExtractQueryParam(rawPath, "value");
                bool success = AutoPanConfigHooks.TrySetWorldSpeedScheduleEnabled(value, out string message);
                await ServeJsonAsync(stream, new
                {
                    ok = success,
                    text = message,
                    speedSchedule = AutoPanWorldSpeedService.BuildScheduleSnapshot()
                }, token).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/rank-config/set", StringComparison.OrdinalIgnoreCase))
            {
                string enabled = ExtractQueryParam(rawPath, "enabled");
                string ranks = ExtractQueryParam(rawPath, "ranks");
                bool success = AutoPanRankService.TrySetConfig(enabled, ranks, out string message);
                await ServeJsonAsync(stream, new
                {
                    ok = success,
                    text = message,
                    rankConfig = AutoPanRankService.BuildSnapshot()
                }, token).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/score/set", StringComparison.OrdinalIgnoreCase))
            {
                string userId = ExtractQueryParam(rawPath, "userId");
                string playerName = ExtractQueryParam(rawPath, "playerName");
                string winsText = ExtractQueryParam(rawPath, "wins");
                string message = string.Empty;
                bool success = int.TryParse(winsText, out int wins) && AutoPanScoreService.TrySetScore(userId, playerName, wins, out message);
                if (!success && string.IsNullOrWhiteSpace(message))
                {
                    message = "保存积分失败：积分必须是整数。";
                }

                await ServeJsonAsync(stream, new
                {
                    ok = success,
                    text = message,
                    scoreboard = AutoPanScoreService.BuildDashboardRecords()
                }, token).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/score/delete", StringComparison.OrdinalIgnoreCase))
            {
                string userId = ExtractQueryParam(rawPath, "userId");
                bool success = AutoPanScoreService.TryDeleteScore(userId, out string message);
                await ServeJsonAsync(stream, new
                {
                    ok = success,
                    text = message,
                    scoreboard = AutoPanScoreService.BuildDashboardRecords()
                }, token).ConfigureAwait(false);
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

        private async Task HandleFrontendWebSocketAsync(TcpClient client, NetworkStream stream, Dictionary<string, string> headers, string remoteEndPoint, CancellationToken token)
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
                            RemoteEndPoint = remoteEndPoint,
                            SourceType = AutoPanInputSourceType.FrontendWeb
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

        private async Task HandleOneBotWebSocketAsync(TcpClient client, NetworkStream stream, Dictionary<string, string> headers, string remoteEndPoint, CancellationToken token)
        {
            headers.TryGetValue("sec-websocket-key", out string wsKey);
            if (string.IsNullOrWhiteSpace(wsKey))
            {
                await WriteHttpResponseAsync(stream, 400, "text/plain", Encoding.UTF8.GetBytes("Missing Sec-WebSocket-Key"), token).ConfigureAwait(false);
                return;
            }

            if (!AutoPanQqBridgeService.TryValidateHandshake(headers, out string role, out string selfId, out string error))
            {
                await WriteHttpResponseAsync(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(error), token).ConfigureAwait(false);
                return;
            }

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
            _oneBotSessions[sessionId] = session;
            AutoPanQqBridgeService.RegisterSession(sessionId, role, selfId, remoteEndPoint);

            try
            {
                while (!token.IsCancellationRequested && session.Alive)
                {
                    WsFrame frame = await ReadWsFrameAsync(stream, token).ConfigureAwait(false);
                    if (frame == null || frame.Opcode == 8)
                    {
                        break;
                    }

                    if (frame.Opcode == 9)
                    {
                        await WriteWsFrameAsync(session, 10, frame.Payload, token).ConfigureAwait(false);
                        continue;
                    }

                    if (frame.Opcode != 1)
                    {
                        continue;
                    }

                    string payload = Encoding.UTF8.GetString(frame.Payload);
                    if (AutoPanQqBridgeService.TryConvertIncomingPayload(sessionId, payload, remoteEndPoint, out FrontendInboundMessage inbound))
                    {
                        _pendingMessages.Enqueue(inbound);
                    }
                }
            }
            finally
            {
                session.Alive = false;
                _oneBotSessions.TryRemove(sessionId, out _);
                AutoPanQqBridgeService.UnregisterSession(sessionId);
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

        private async Task SendOneBotGroupMessageAsync(string sessionId, WsSession session, string groupId, string text)
        {
            if (!long.TryParse(groupId, out long parsedGroupId))
            {
                AutoPanLogService.Error($"QQ 回包失败：非法群号 {groupId}。");
                return;
            }

            string payload = JsonConvert.SerializeObject(new
            {
                action = "send_group_msg",
                @params = new
                {
                    group_id = parsedGroupId,
                    message = text,
                    auto_escape = false
                },
                echo = $"autopan-{DateTime.UtcNow.Ticks}"
            }, CamelCaseJsonSettings);
            await SendWsTextAsync(sessionId, session, payload).ConfigureAwait(false);
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
        private static bool LooksLikeWebSocketRequest(Dictionary<string, string> headers)
        {
            headers.TryGetValue("Connection", out string connection);
            headers.TryGetValue("Upgrade", out string upgrade);
            headers.TryGetValue("Sec-WebSocket-Key", out string secWebSocketKey);
            if (!string.IsNullOrWhiteSpace(secWebSocketKey))
            {
                return true;
            }

            return connection != null
                && connection.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0
                && string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断两个 WebSocket 路径是否一致，忽略末尾斜杠差异。
        /// </summary>
        private static bool IsSameWebSocketPath(string actualPath, string configuredPath)
        {
            string left = NormalizeWebSocketPath(actualPath);
            string right = NormalizeWebSocketPath(configuredPath);
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 规范化 WebSocket 路径。
        /// </summary>
        private static string NormalizeWebSocketPath(string path)
        {
            string normalized = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "/";
            }

            if (!normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = "/" + normalized;
            }

            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
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
