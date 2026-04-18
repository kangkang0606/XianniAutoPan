using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using XianniAutoPan.Frontend;
using XianniAutoPan.Model;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 维护 QQ 群系统通知路由，并向玩家最近活跃群发送自动盘提醒。
    /// </summary>
    internal static class AutoPanNotificationService
    {
        private sealed class PersistedRouteFile
        {
            /// <summary>
            /// 玩家到 QQ 群路由的持久化列表。
            /// </summary>
            public List<AutoPanQqRouteRecord> Routes { get; set; } = new List<AutoPanQqRouteRecord>();
        }

        private sealed class AutoPanQqRouteRecord
        {
            /// <summary>
            /// 玩家唯一标识，QQ 场景下固定为 QQ 号。
            /// </summary>
            public string UserId { get; set; }

            /// <summary>
            /// 玩家显示名。
            /// </summary>
            public string PlayerName { get; set; }

            /// <summary>
            /// OneBot WebSocket 会话 ID。
            /// </summary>
            public string SessionId { get; set; }

            /// <summary>
            /// QQ 群号。
            /// </summary>
            public string GroupId { get; set; }

            /// <summary>
            /// 机器人自身 QQ。
            /// </summary>
            public string BotSelfId { get; set; }

            /// <summary>
            /// 最近活跃时间，UTC ISO 字符串。
            /// </summary>
            public string LastSeenUtc { get; set; }
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, AutoPanQqRouteRecord> RoutesByUser = new Dictionary<string, AutoPanQqRouteRecord>(StringComparer.Ordinal);
        private static string _routePath = string.Empty;

        /// <summary>
        /// 初始化 QQ 路由文件路径并载入已记录路由。
        /// </summary>
        public static void Initialize(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder))
            {
                return;
            }

            _routePath = Path.Combine(modFolder, "autopan_qq_routes.json");
            Load();
        }

        /// <summary>
        /// 记录 QQ 群消息的最新回包路由，供灭国、约斗与结盘通知使用。
        /// </summary>
        public static void RecordRoute(FrontendInboundMessage message, string playerName)
        {
            if (message == null || message.SourceType != AutoPanInputSourceType.QqGroup)
            {
                return;
            }

            string userId = NormalizeDigits(message.UserId);
            string groupId = NormalizeDigits(message.ContextId);
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            lock (Sync)
            {
                RoutesByUser[userId] = new AutoPanQqRouteRecord
                {
                    UserId = userId,
                    PlayerName = string.IsNullOrWhiteSpace(playerName) ? userId : playerName.Trim(),
                    SessionId = message.SessionId,
                    GroupId = groupId,
                    BotSelfId = NormalizeDigits(message.BotSelfId),
                    LastSeenUtc = DateTime.UtcNow.ToString("o")
                };
            }

            Save();
        }

        /// <summary>
        /// 通知指定玩家最近活跃的 QQ 群，并自动 @ 该玩家。
        /// </summary>
        public static void NotifyUser(string userId, string text)
        {
            string normalizedUserId = NormalizeDigits(userId);
            if (string.IsNullOrWhiteSpace(normalizedUserId) || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (TryGetRouteSession(normalizedUserId, out AutoPanSessionInfo session))
            {
                AutoPanLocalWebServer.Instance.SendQqNotice(session, normalizedUserId, text);
                return;
            }

            if (AutoPanStateRepository.TryGetLatestQqSessionForUser(normalizedUserId, out session))
            {
                AutoPanLocalWebServer.Instance.SendQqNotice(session, normalizedUserId, text);
            }
        }

        /// <summary>
        /// 向绑定该国家的全部玩家发送 QQ 通知。
        /// </summary>
        public static void NotifyKingdomOwners(Kingdom kingdom, string text)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            foreach (AutoPanBindingRecord binding in AutoPanStateRepository.GetBindingsByKingdomId(kingdom.getID()))
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.UserId))
                {
                    continue;
                }

                NotifyUser(binding.UserId, text);
            }
        }

        /// <summary>
        /// 通知玩家其绑定国家已灭亡，并提示重新加入。
        /// </summary>
        public static void NotifyKingdomDestroyed(AutoPanBindingRecord binding, Kingdom kingdom)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.UserId))
            {
                return;
            }

            string kingdomText = kingdom != null
                ? AutoPanKingdomService.FormatKingdomLabel(kingdom)
                : $"{binding.KingdomName} [{binding.KingdomId}]";
            string playerText = $"{(string.IsNullOrWhiteSpace(binding.PlayerName) ? "未知玩家" : binding.PlayerName)}({binding.UserId})";
            NotifyUser(binding.UserId, $"{playerText} 绑定的国家 {kingdomText} 已灭亡，绑定已自动解除。现在可以重新发送“加入人类/加入兽人/加入精灵/加入矮人”。");
        }

        /// <summary>
        /// 向所有已知最近活跃 QQ 群广播系统通知，可附带一组需要 @ 的玩家。
        /// </summary>
        public static void BroadcastToKnownGroups(string text, IEnumerable<string> atUserIds = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            List<AutoPanQqRouteRecord> routes;
            lock (Sync)
            {
                routes = RoutesByUser.Values
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.GroupId))
                    .GroupBy(item => item.GroupId + "|" + item.BotSelfId, StringComparer.Ordinal)
                    .Select(group => group.OrderByDescending(item => item.LastSeenUtc, StringComparer.Ordinal).First())
                    .ToList();
            }

            string fullText = BuildAtPrefix(atUserIds) + text.Trim();
            foreach (AutoPanQqRouteRecord route in routes)
            {
                AutoPanLocalWebServer.Instance.SendQqNotice(ToSessionInfo(route), string.Empty, fullText);
            }
        }

        private static bool TryGetRouteSession(string userId, out AutoPanSessionInfo session)
        {
            session = null;
            lock (Sync)
            {
                if (!RoutesByUser.TryGetValue(userId, out AutoPanQqRouteRecord route) || route == null)
                {
                    return false;
                }

                session = ToSessionInfo(route);
                return true;
            }
        }

        private static AutoPanSessionInfo ToSessionInfo(AutoPanQqRouteRecord route)
        {
            return new AutoPanSessionInfo
            {
                SessionId = route.SessionId,
                UserId = route.UserId,
                PlayerName = route.PlayerName,
                LastSeenUtc = route.LastSeenUtc,
                SourceType = AutoPanInputSourceType.QqGroup,
                ContextId = route.GroupId,
                BotSelfId = route.BotSelfId
            };
        }

        private static string BuildAtPrefix(IEnumerable<string> atUserIds)
        {
            if (!AutoPanConfigHooks.QqReplyAtSender || atUserIds == null)
            {
                return string.Empty;
            }

            string prefix = string.Join(" ", atUserIds
                .Select(NormalizeDigits)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .Select(item => $"[CQ:at,qq={item}]")
                .ToArray());
            return string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix + " ";
        }

        private static void Load()
        {
            lock (Sync)
            {
                RoutesByUser.Clear();
                if (string.IsNullOrWhiteSpace(_routePath) || !File.Exists(_routePath))
                {
                    return;
                }

                try
                {
                    PersistedRouteFile file = JsonConvert.DeserializeObject<PersistedRouteFile>(File.ReadAllText(_routePath));
                    foreach (AutoPanQqRouteRecord route in file?.Routes ?? new List<AutoPanQqRouteRecord>())
                    {
                        string userId = NormalizeDigits(route?.UserId);
                        string groupId = NormalizeDigits(route?.GroupId);
                        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(groupId))
                        {
                            continue;
                        }

                        route.UserId = userId;
                        route.GroupId = groupId;
                        route.BotSelfId = NormalizeDigits(route.BotSelfId);
                        RoutesByUser[userId] = route;
                    }
                }
                catch (Exception ex)
                {
                    AutoPanLogService.Error($"读取 QQ 通知路由失败：{ex.Message}");
                }
            }
        }

        private static void Save()
        {
            if (string.IsNullOrWhiteSpace(_routePath))
            {
                return;
            }

            try
            {
                string folder = Path.GetDirectoryName(_routePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                PersistedRouteFile file;
                lock (Sync)
                {
                    file = new PersistedRouteFile
                    {
                        Routes = RoutesByUser.Values.OrderBy(item => item.UserId, StringComparer.Ordinal).ToList()
                    };
                }

                File.WriteAllText(_routePath, JsonConvert.SerializeObject(file, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"保存 QQ 通知路由失败：{ex.Message}");
            }
        }

        private static string NormalizeDigits(string value)
        {
            return new string((value ?? string.Empty).Trim().Where(char.IsDigit).ToArray());
        }
    }
}
