using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using XianniAutoPan.Model;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 维护 QQ 群 OneBot 接入状态、事件适配与自动回包格式。
    /// </summary>
    internal static class AutoPanQqBridgeService
    {
        private sealed class SessionMeta
        {
            /// <summary>
            /// 会话 ID。
            /// </summary>
            public string SessionId { get; set; }

            /// <summary>
            /// OneBot 客户端角色。
            /// </summary>
            public string Role { get; set; }

            /// <summary>
            /// 机器人自身 QQ。
            /// </summary>
            public string SelfId { get; set; }

            /// <summary>
            /// 远端地址。
            /// </summary>
            public string RemoteEndPoint { get; set; }

            /// <summary>
            /// 最近活跃时间。
            /// </summary>
            public string LastSeenUtc { get; set; }
        }

        private const int RecentGroupCapacity = 20;
        private const int RecentMessageCapacity = 30;
        private const int MaxReplyChunkLength = 420;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, SessionMeta> Sessions = new Dictionary<string, SessionMeta>();
        private static readonly List<string> RecentGroups = new List<string>();
        private static readonly List<string> RecentMessages = new List<string>();

        /// <summary>
        /// 规范化并验证 OneBot 反向 WebSocket 握手头。
        /// </summary>
        public static bool TryValidateHandshake(Dictionary<string, string> headers, out string role, out string selfId, out string error)
        {
            role = string.Empty;
            selfId = string.Empty;
            error = string.Empty;
            if (!AutoPanConfigHooks.QqAdapterEnabled)
            {
                error = "QQ 群接入当前未启用。";
                return false;
            }

            headers?.TryGetValue("X-Client-Role", out role);
            role = NormalizeRole(role);
            if (string.IsNullOrWhiteSpace(role))
            {
                role = "Universal";
            }

            if (!CanReceiveEvents(role) && !CanSendActions(role))
            {
                error = $"不支持的 OneBot 角色：{role}";
                return false;
            }

            headers?.TryGetValue("X-Self-ID", out selfId);
            selfId = NormalizeDigits(selfId);
            if (!string.IsNullOrWhiteSpace(AutoPanConfigHooks.QqBotSelfId) && !string.Equals(selfId, AutoPanConfigHooks.QqBotSelfId, StringComparison.Ordinal))
            {
                error = $"机器人 QQ 不匹配：当前 {selfId}，期望 {AutoPanConfigHooks.QqBotSelfId}";
                return false;
            }

            string authHeader = string.Empty;
            headers?.TryGetValue("Authorization", out authHeader);
            if (!IsAuthorizationValid(authHeader, AutoPanConfigHooks.QqOneBotAccessToken))
            {
                error = "OneBot 访问令牌校验失败。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 记录 OneBot 会话已连接。
        /// </summary>
        public static void RegisterSession(string sessionId, string role, string selfId, string remoteEndPoint)
        {
            lock (Sync)
            {
                Sessions[sessionId] = new SessionMeta
                {
                    SessionId = sessionId,
                    Role = NormalizeRole(role),
                    SelfId = NormalizeDigits(selfId),
                    RemoteEndPoint = remoteEndPoint,
                    LastSeenUtc = DateTime.UtcNow.ToString("o")
                };
            }

            AutoPanLogService.Info($"OneBot 会话已连接：role={role}，self={selfId}，remote={remoteEndPoint}");
        }

        /// <summary>
        /// 移除 OneBot 会话。
        /// </summary>
        public static void UnregisterSession(string sessionId)
        {
            SessionMeta removed = null;
            lock (Sync)
            {
                if (Sessions.TryGetValue(sessionId, out removed))
                {
                    Sessions.Remove(sessionId);
                }
            }

            if (removed != null)
            {
                AutoPanLogService.Info($"OneBot 会话已断开：role={removed.Role}，self={removed.SelfId}，remote={removed.RemoteEndPoint}");
            }
        }

        /// <summary>
        /// 解析 OneBot 文本帧；若成功适配为自动盘消息则返回 true。
        /// </summary>
        public static bool TryConvertIncomingPayload(string sessionId, string payload, string remoteEndPoint, out FrontendInboundMessage message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(payload);
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"OneBot 消息 JSON 解析失败：{ex.Message}");
                return false;
            }

            if (!string.Equals(root["post_type"]?.ToString(), "message", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(root["message_type"]?.ToString(), "group", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string selfId = NormalizeDigits(root["self_id"]?.ToString());
            string userId = NormalizeDigits(root["user_id"]?.ToString());
            string groupId = NormalizeDigits(root["group_id"]?.ToString());
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }

            if (string.Equals(selfId, userId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!AutoPanConfigHooks.IsQqGroupAllowed(groupId))
            {
                return false;
            }

            string rawMessage = root["raw_message"]?.ToString() ?? string.Empty;
            string text = NormalizeIncomingText(rawMessage, selfId);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string nickname = (root["sender"]?["nickname"]?.ToString() ?? string.Empty).Trim();
            string playerName = string.IsNullOrWhiteSpace(nickname) ? userId : nickname;

            TouchSession(sessionId);
            RecordIncoming(groupId, $"{nickname}({userId})：{Truncate(text, 80)}");
            message = new FrontendInboundMessage
            {
                SessionId = sessionId,
                UserId = userId,
                PlayerName = playerName,
                Text = text,
                RemoteEndPoint = remoteEndPoint,
                SourceType = AutoPanInputSourceType.QqGroup,
                ReplyTargetId = groupId,
                ContextId = groupId,
                BotSelfId = selfId
            };
            return true;
        }

        /// <summary>
        /// 解析出 QQ 群回包所需的目标会话与文本分片。
        /// </summary>
        public static bool TryBuildReplyPlan(FrontendInboundMessage sourceMessage, AutoPanCommandResult result, out string replySessionId, out string groupId, out List<string> messageChunks)
        {
            replySessionId = string.Empty;
            groupId = string.Empty;
            messageChunks = new List<string>();
            if (sourceMessage == null || sourceMessage.SourceType != AutoPanInputSourceType.QqGroup || result == null || result.SuppressQqReply)
            {
                return false;
            }

            groupId = NormalizeDigits(sourceMessage.ReplyTargetId);
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(result.Text))
            {
                return false;
            }

            replySessionId = ResolveReplySessionId(sourceMessage.SessionId, sourceMessage.BotSelfId);
            if (string.IsNullOrWhiteSpace(replySessionId))
            {
                AutoPanLogService.Error($"未找到可用的 OneBot 回包会话：group={groupId}，user={sourceMessage.UserId}");
                return false;
            }

            messageChunks = SplitMessage(result.Text, MaxReplyChunkLength);
            return messageChunks.Count > 0;
        }

        /// <summary>
        /// 构建前端展示用 QQ 接入状态快照。
        /// </summary>
        public static AutoPanQqDashboardSnapshot BuildDashboardSnapshot()
        {
            AutoPanQqDashboardSnapshot snapshot = AutoPanConfigHooks.BuildQqDashboardSnapshot();
            lock (Sync)
            {
                snapshot.ConnectedBots = Sessions.Values
                    .Where(item => !string.IsNullOrWhiteSpace(item.SelfId))
                    .OrderBy(item => item.SelfId, StringComparer.Ordinal)
                    .Select(item => $"{item.SelfId} ({item.Role})")
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                snapshot.RecentGroups = new List<string>(RecentGroups);
                snapshot.RecentMessages = new List<string>(RecentMessages);
                snapshot.Connected = Sessions.Values.Any(item => CanReceiveEvents(item.Role) || CanSendActions(item.Role));
                if (string.IsNullOrWhiteSpace(snapshot.BotSelfId))
                {
                    snapshot.BotSelfId = Sessions.Values.Select(item => item.SelfId).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
                }
            }

            return snapshot;
        }

        private static void RecordIncoming(string groupId, string summary)
        {
            lock (Sync)
            {
                if (!string.IsNullOrWhiteSpace(groupId))
                {
                    RecentGroups.RemoveAll(item => item == groupId);
                    RecentGroups.Add(groupId);
                    if (RecentGroups.Count > RecentGroupCapacity)
                    {
                        RecentGroups.RemoveRange(0, RecentGroups.Count - RecentGroupCapacity);
                    }
                }

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    RecentMessages.Add($"[{DateTime.Now:HH:mm:ss}] 群{groupId} {summary}");
                    if (RecentMessages.Count > RecentMessageCapacity)
                    {
                        RecentMessages.RemoveRange(0, RecentMessages.Count - RecentMessageCapacity);
                    }
                }
            }
        }

        private static void TouchSession(string sessionId)
        {
            lock (Sync)
            {
                if (Sessions.TryGetValue(sessionId, out SessionMeta meta) && meta != null)
                {
                    meta.LastSeenUtc = DateTime.UtcNow.ToString("o");
                }
            }
        }

        private static string ResolveReplySessionId(string preferredSessionId, string selfId)
        {
            lock (Sync)
            {
                if (Sessions.TryGetValue(preferredSessionId, out SessionMeta preferred) && preferred != null && CanSendActions(preferred.Role))
                {
                    return preferred.SessionId;
                }

                SessionMeta matched = Sessions.Values
                    .Where(item => CanSendActions(item.Role))
                    .Where(item => string.IsNullOrWhiteSpace(selfId) || string.Equals(item.SelfId, NormalizeDigits(selfId), StringComparison.Ordinal))
                    .OrderByDescending(item => item.LastSeenUtc, StringComparer.Ordinal)
                    .FirstOrDefault();
                return matched?.SessionId ?? string.Empty;
            }
        }

        private static string BuildReplyText(string userId, string text)
        {
            string body = (text ?? string.Empty).Trim();
            if (!AutoPanConfigHooks.QqReplyAtSender || string.IsNullOrWhiteSpace(userId))
            {
                return body;
            }

            return $"[CQ:at,qq={NormalizeDigits(userId)}] {body}";
        }

        private static List<string> SplitMessage(string text, int maxLength)
        {
            List<string> result = new List<string>();
            string remaining = (text ?? string.Empty).Trim();
            while (!string.IsNullOrWhiteSpace(remaining))
            {
                if (remaining.Length <= maxLength)
                {
                    result.Add(remaining);
                    break;
                }

                int splitIndex = remaining.LastIndexOf('\n', maxLength);
                if (splitIndex <= 0)
                {
                    splitIndex = maxLength;
                }

                result.Add(remaining.Substring(0, splitIndex).Trim());
                remaining = remaining.Substring(splitIndex).Trim();
            }

            return result.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        }

        private static bool IsAuthorizationValid(string authHeader, string expectedToken)
        {
            if (string.IsNullOrWhiteSpace(expectedToken))
            {
                return true;
            }

            string normalized = (authHeader ?? string.Empty).Trim();
            if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Bearer ".Length).Trim();
            }
            else if (normalized.StartsWith("Token ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Token ".Length).Trim();
            }

            return string.Equals(normalized, expectedToken, StringComparison.Ordinal);
        }

        private static string NormalizeRole(string role)
        {
            string normalized = (role ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return normalized switch
            {
                "event" or "Event" => "Event",
                "api" or "API" => "API",
                _ => "Universal"
            };
        }

        private static bool CanReceiveEvents(string role)
        {
            string normalized = NormalizeRole(role);
            return string.Equals(normalized, "Event", StringComparison.Ordinal) || string.Equals(normalized, "Universal", StringComparison.Ordinal);
        }

        private static bool CanSendActions(string role)
        {
            string normalized = NormalizeRole(role);
            return string.Equals(normalized, "API", StringComparison.Ordinal) || string.Equals(normalized, "Universal", StringComparison.Ordinal);
        }

        private static string NormalizeIncomingText(string rawMessage, string selfId)
        {
            string text = (rawMessage ?? string.Empty)
                .Replace("\u2005", " ")
                .Replace("\u00A0", " ")
                .Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(selfId))
            {
                string atCode = $"[CQ:at,qq={selfId}]";
                text = RemoveCaseInsensitive(text, atCode);
            }

            while (text.StartsWith("[CQ:reply,", StringComparison.OrdinalIgnoreCase))
            {
                int end = text.IndexOf(']');
                if (end < 0)
                {
                    break;
                }

                text = text.Substring(end + 1).TrimStart();
            }

            return text.Trim();
        }

        private static string NormalizeDigits(string value)
        {
            return new string((value ?? string.Empty).Trim().Where(char.IsDigit).ToArray());
        }

        private static string RemoveCaseInsensitive(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            {
                return source ?? string.Empty;
            }

            int index = source.IndexOf(target, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                source = source.Remove(index, target.Length);
                index = source.IndexOf(target, StringComparison.OrdinalIgnoreCase);
            }

            return source;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, maxLength) + "...";
        }
    }
}
