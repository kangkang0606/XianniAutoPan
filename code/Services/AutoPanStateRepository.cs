using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using XianniAutoPan.Model;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 自动盘世界状态与玩家绑定仓库。
    /// </summary>
    internal static class AutoPanStateRepository
    {
        private static readonly object Sync = new object();
        private static AutoPanWorldState _state = new AutoPanWorldState();

        /// <summary>
        /// 从当前世界载入自动盘状态。
        /// </summary>
        public static void LoadFromWorld()
        {
            lock (Sync)
            {
                _state = new AutoPanWorldState();
                if (World.world == null || World.world.map_stats == null)
                {
                    return;
                }

                EnsureCustomDataReady();
                World.world.map_stats.custom_data.get(AutoPanConstants.WorldStateKey, out string json, null);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                try
                {
                    AutoPanWorldState loaded = JsonConvert.DeserializeObject<AutoPanWorldState>(json);
                    if (loaded != null)
                    {
                        _state = loaded;
                        if (_state.Bindings == null)
                        {
                            _state.Bindings = new Dictionary<string, AutoPanBindingRecord>();
                        }
                        if (_state.RecentSessions == null)
                        {
                            _state.RecentSessions = new List<AutoPanSessionInfo>();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AutoPanLogService.Error($"解析世界状态失败：{ex.Message}");
                    _state = new AutoPanWorldState();
                }
            }
        }

        /// <summary>
        /// 将当前状态写回世界存档。
        /// </summary>
        public static void SaveToWorld()
        {
            lock (Sync)
            {
                if (World.world == null || World.world.map_stats == null)
                {
                    return;
                }

                EnsureCustomDataReady();
                World.world.map_stats.custom_data.set(AutoPanConstants.WorldStateKey, JsonConvert.SerializeObject(_state));
            }
        }

        /// <summary>
        /// 判断当前世界是否已经完成自动盘初始化。
        /// </summary>
        public static bool IsWorldInitialized()
        {
            lock (Sync)
            {
                return _state.WorldInitialized;
            }
        }

        /// <summary>
        /// 将当前世界标记为已完成自动盘初始化。
        /// </summary>
        public static void MarkWorldInitialized()
        {
            lock (Sync)
            {
                if (_state.WorldInitialized)
                {
                    return;
                }

                _state.WorldInitialized = true;
            }

            SaveToWorld();
        }

        /// <summary>
        /// 获取指定用户的绑定快照。
        /// </summary>
        public static AutoPanBindingRecord GetBindingSnapshot(string userId)
        {
            lock (Sync)
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return null;
                }

                return _state.Bindings.TryGetValue(userId, out AutoPanBindingRecord binding) ? CloneBinding(binding) : null;
            }
        }

        /// <summary>
        /// 获取指定用户的活跃绑定。
        /// </summary>
        public static bool TryGetLiveBinding(string userId, out AutoPanBindingRecord binding, out Kingdom kingdom)
        {
            binding = null;
            kingdom = null;
            AutoPanBindingRecord snapshot = GetBindingSnapshot(userId);
            if (snapshot == null)
            {
                return false;
            }

            Kingdom current = World.world?.kingdoms?.get(snapshot.KingdomId);
            if (current == null || !current.isAlive())
            {
                return false;
            }

            binding = snapshot;
            kingdom = current;
            return true;
        }

        /// <summary>
        /// 确保指定用户的绑定仍然有效。
        /// </summary>
        public static void EnsureBindingValidForUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            AutoPanBindingRecord binding = GetBindingSnapshot(userId);
            if (binding == null)
            {
                return;
            }

            if (!TryGetLiveBinding(userId, out _, out _))
            {
                AutoPanNotificationService.NotifyKingdomDestroyed(binding, null);
                ClearBinding(userId, saveImmediately: true);
            }
        }

        /// <summary>
        /// 全量清理已失效绑定。
        /// </summary>
        public static void CleanupDeadBindings()
        {
            List<AutoPanBindingRecord> deadBindings = new List<AutoPanBindingRecord>();
            lock (Sync)
            {
                List<string> toRemove = new List<string>();
                foreach (KeyValuePair<string, AutoPanBindingRecord> pair in _state.Bindings)
                {
                    Kingdom kingdom = World.world?.kingdoms?.get(pair.Value.KingdomId);
                    if (kingdom == null || !kingdom.isAlive())
                    {
                        toRemove.Add(pair.Key);
                        deadBindings.Add(CloneBinding(pair.Value));
                    }
                }

                foreach (string userId in toRemove)
                {
                    _state.Bindings.Remove(userId);
                }
            }

            SaveToWorld();
            foreach (AutoPanBindingRecord binding in deadBindings)
            {
                AutoPanNotificationService.NotifyKingdomDestroyed(binding, null);
            }
        }

        /// <summary>
        /// 绑定玩家与国家。
        /// </summary>
        public static void BindPlayerToKingdom(string userId, string playerName, string raceId, Kingdom kingdom)
        {
            if (string.IsNullOrWhiteSpace(userId) || kingdom == null)
            {
                return;
            }

            AutoPanBindingRecord record = new AutoPanBindingRecord
            {
                UserId = userId,
                PlayerName = playerName,
                KingdomId = kingdom.getID(),
                KingdomName = kingdom.name,
                RaceId = raceId
            };

            lock (Sync)
            {
                _state.Bindings[userId] = record;
            }

            kingdom.data.set(AutoPanConstants.KeyOwnerUserId, userId);
            kingdom.data.set(AutoPanConstants.KeyOwnerName, playerName);
            SaveToWorld();
        }

        /// <summary>
        /// 按用户清理绑定。
        /// </summary>
        public static void ClearBinding(string userId, bool saveImmediately)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            lock (Sync)
            {
                _state.Bindings.Remove(userId);
            }

            if (saveImmediately)
            {
                SaveToWorld();
            }
        }

        /// <summary>
        /// 按国家清理绑定。
        /// </summary>
        public static void ClearBindingByKingdomId(long kingdomId)
        {
            lock (Sync)
            {
                List<string> toRemove = new List<string>();
                foreach (KeyValuePair<string, AutoPanBindingRecord> pair in _state.Bindings)
                {
                    if (pair.Value.KingdomId == kingdomId)
                    {
                        toRemove.Add(pair.Key);
                    }
                }

                foreach (string userId in toRemove)
                {
                    _state.Bindings.Remove(userId);
                }
            }

            SaveToWorld();
        }

        /// <summary>
        /// 记录前端会话最近信息。
        /// </summary>
        public static void RecordSession(string sessionId, string userId, string playerName, string remoteEndPoint, AutoPanInputSourceType sourceType = AutoPanInputSourceType.FrontendWeb, string contextId = null, string botSelfId = null)
        {
            lock (Sync)
            {
                _state.RecentSessions.RemoveAll(item => item.SessionId == sessionId);
                _state.RecentSessions.Add(new AutoPanSessionInfo
                {
                    SessionId = sessionId,
                    UserId = userId,
                    PlayerName = playerName,
                    RemoteEndPoint = remoteEndPoint,
                    LastSeenUtc = DateTime.UtcNow.ToString("o"),
                    SourceType = sourceType,
                    ContextId = contextId,
                    BotSelfId = botSelfId
                });
                if (_state.RecentSessions.Count > AutoPanConstants.SessionCapacity)
                {
                    _state.RecentSessions.RemoveRange(0, _state.RecentSessions.Count - AutoPanConstants.SessionCapacity);
                }
            }

            SaveToWorld();
        }

        /// <summary>
        /// 刷新玩家当前显示名到绑定记录，不改已创建国家名称。
        /// </summary>
        public static void RefreshPlayerProfile(string userId, string playerName)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(playerName))
            {
                return;
            }

            bool changed = false;
            lock (Sync)
            {
                if (_state.Bindings.TryGetValue(userId, out AutoPanBindingRecord binding) && binding != null && !string.Equals(binding.PlayerName, playerName, StringComparison.Ordinal))
                {
                    binding.PlayerName = playerName;
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            Kingdom kingdom = World.world?.kingdoms?.get(GetBindingSnapshot(userId)?.KingdomId ?? 0);
            if (kingdom != null && kingdom.isAlive())
            {
                kingdom.data.set(AutoPanConstants.KeyOwnerName, playerName);
            }

            SaveToWorld();
        }

        /// <summary>
        /// 获取绑定到指定国家的所有玩家快照。
        /// </summary>
        public static List<AutoPanBindingRecord> GetBindingsByKingdomId(long kingdomId)
        {
            lock (Sync)
            {
                return _state.Bindings.Values
                    .Where(item => item != null && item.KingdomId == kingdomId)
                    .Select(CloneBinding)
                    .Where(item => item != null)
                    .ToList();
            }
        }

        /// <summary>
        /// 获取指定用户最近一次 QQ 群会话路由。
        /// </summary>
        public static bool TryGetLatestQqSessionForUser(string userId, out AutoPanSessionInfo session)
        {
            session = null;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            lock (Sync)
            {
                session = _state.RecentSessions
                    .Where(item => item != null
                        && item.SourceType == AutoPanInputSourceType.QqGroup
                        && string.Equals(item.UserId, userId, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(item.ContextId))
                    .OrderByDescending(item => item.LastSeenUtc, StringComparer.Ordinal)
                    .Select(CloneSession)
                    .FirstOrDefault();
            }

            return session != null;
        }

        /// <summary>
        /// 同步更新绑定记录中的国家名称。
        /// </summary>
        public static void UpdateBoundKingdomName(long kingdomId, string kingdomName)
        {
            if (kingdomId <= 0 || string.IsNullOrWhiteSpace(kingdomName))
            {
                return;
            }

            bool changed = false;
            lock (Sync)
            {
                foreach (AutoPanBindingRecord binding in _state.Bindings.Values)
                {
                    if (binding == null || binding.KingdomId != kingdomId)
                    {
                        continue;
                    }

                    binding.KingdomName = kingdomName;
                    changed = true;
                }
            }

            if (changed)
            {
                SaveToWorld();
            }
        }

        /// <summary>
        /// 生成新的消息流水号。
        /// </summary>
        public static long NextMessageSequence()
        {
            lock (Sync)
            {
                _state.MessageSequence++;
                SaveToWorld();
                return _state.MessageSequence;
            }
        }

        /// <summary>
        /// 判断国家是否由玩家绑定。
        /// </summary>
        public static bool IsPlayerOwnedKingdom(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return false;
            }

            kingdom.data.get(AutoPanConstants.KeyOwnerUserId, out string ownerUserId, null);
            return !string.IsNullOrWhiteSpace(ownerUserId);
        }

        /// <summary>
        /// 构建前端仪表盘快照。
        /// </summary>
        public static AutoPanDashboardSnapshot CreateDashboardSnapshot(string userId, IReadOnlyList<string> addresses, bool serverRunning, string commandBookText)
        {
            AutoPanDashboardSnapshot snapshot = new AutoPanDashboardSnapshot
            {
                ServerRunning = serverRunning,
                AiEnabled = AutoPanConfigHooks.EnableLlmAi,
                AiDecisionIntensity = AutoPanConfigHooks.AiDecisionIntensity,
                AiQqChatEnabled = AutoPanConfigHooks.AiQqChatEnabled > 0,
                CommandBookText = commandBookText,
                RecentLogs = AutoPanLogService.GetRecentEntries(),
                Kingdoms = AutoPanKingdomService.BuildDashboardKingdoms(),
                Scoreboard = AutoPanScoreService.BuildDashboardRecords(),
                Policy = AutoPanConfigHooks.BuildPolicySnapshot(),
                QqBridge = AutoPanQqBridgeService.BuildDashboardSnapshot(),
                SpeedSchedule = AutoPanWorldSpeedService.BuildScheduleSnapshot()
            };

            if (addresses != null)
            {
                snapshot.ListenAddresses.AddRange(addresses);
            }

            lock (Sync)
            {
                snapshot.Binding = GetBindingSnapshot(userId);
                snapshot.RecentSessions = new List<AutoPanSessionInfo>(_state.RecentSessions);
            }

            if (snapshot.Binding != null && World.world?.kingdoms != null)
            {
                Kingdom boundKingdom = World.world.kingdoms.get(snapshot.Binding.KingdomId);
                snapshot.PendingRequests = AutoPanRequestService.BuildPendingSnapshots(boundKingdom);
            }

            return snapshot;
        }

        private static void EnsureCustomDataReady()
        {
            if (World.world.map_stats.custom_data == null)
            {
                World.world.map_stats.custom_data = new SaveCustomData();
            }
        }

        private static AutoPanBindingRecord CloneBinding(AutoPanBindingRecord binding)
        {
            if (binding == null)
            {
                return null;
            }

            return new AutoPanBindingRecord
            {
                UserId = binding.UserId,
                PlayerName = binding.PlayerName,
                KingdomId = binding.KingdomId,
                KingdomName = binding.KingdomName,
                RaceId = binding.RaceId
            };
        }

        private static AutoPanSessionInfo CloneSession(AutoPanSessionInfo session)
        {
            if (session == null)
            {
                return null;
            }

            return new AutoPanSessionInfo
            {
                SessionId = session.SessionId,
                UserId = session.UserId,
                PlayerName = session.PlayerName,
                RemoteEndPoint = session.RemoteEndPoint,
                LastSeenUtc = session.LastSeenUtc,
                SourceType = session.SourceType,
                ContextId = session.ContextId,
                BotSelfId = session.BotSelfId
            };
        }
    }
}
