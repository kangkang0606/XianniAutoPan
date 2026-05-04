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
        private const int MinimumActivityYearsToKeep = 1000;
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
                        if (_state.RoundParticipants == null)
                        {
                            _state.RoundParticipants = new List<AutoPanRoundParticipantRecord>();
                        }
                        if (_state.PlayerActivities == null)
                        {
                            _state.PlayerActivities = new List<AutoPanPlayerActivityRecord>();
                        }
                        if (_state.TransferLedgers == null)
                        {
                            _state.TransferLedgers = new List<AutoPanTransferLedgerRecord>();
                        }
                        TrimVolatileStateLocked();
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
                TrimVolatileStateLocked();
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
            if (current == null || current.data == null || !current.isAlive())
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
                if (!AutoPanRoundService.IsRoundTransitioning)
                {
                    AutoPanNotificationService.NotifyKingdomDestroyed(binding, null);
                }
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
                    if (kingdom == null || kingdom.data == null || !kingdom.isAlive())
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

            if (AutoPanRoundService.IsRoundTransitioning)
            {
                return;
            }

            if (deadBindings.Count > 0)
            {
                SaveToWorld();
            }

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
                RecordRoundParticipantLocked(userId, playerName);
                RecordPlayerActivityLocked(userId, playerName, kingdom.getID(), Date.getCurrentYear(), treatAsBinding: true);
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

            if (saveImmediately && !AutoPanRoundService.IsRoundTransitioning)
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

            if (!AutoPanRoundService.IsRoundTransitioning)
            {
                SaveToWorld();
            }
        }

        /// <summary>
        /// 记录前端会话最近信息。
        /// </summary>
        public static void RecordSession(string sessionId, string userId, string playerName, string remoteEndPoint, AutoPanInputSourceType sourceType = AutoPanInputSourceType.FrontendWeb, string contextId = null, string botSelfId = null)
        {
            lock (Sync)
            {
                if (_state.RecentSessions == null)
                {
                    _state.RecentSessions = new List<AutoPanSessionInfo>();
                }

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

                AutoPanRoundParticipantRecord participant = _state.RoundParticipants.FirstOrDefault(item => item != null && string.Equals(item.UserId, userId, StringComparison.Ordinal));
                if (participant != null && !string.Equals(participant.PlayerName, playerName, StringComparison.Ordinal))
                {
                    participant.PlayerName = playerName;
                    changed = true;
                }

                AutoPanPlayerActivityRecord activity = _state.PlayerActivities.FirstOrDefault(item => item != null && string.Equals(item.UserId, userId, StringComparison.Ordinal));
                if (activity != null && !string.Equals(activity.PlayerName, playerName, StringComparison.Ordinal))
                {
                    activity.PlayerName = playerName;
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
        /// 获取本局所有参与玩家快照。
        /// </summary>
        public static List<AutoPanRoundParticipantRecord> GetRoundParticipantsSnapshot()
        {
            lock (Sync)
            {
                return _state.RoundParticipants
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.UserId))
                    .Select(CloneParticipant)
                    .ToList();
            }
        }

        /// <summary>
        /// 清理本局参与玩家记录，用于进入新局前重置参与积分依据。
        /// </summary>
        public static void ClearRoundParticipants()
        {
            lock (Sync)
            {
                _state.RoundParticipants.Clear();
            }

            SaveToWorld();
        }

        /// <summary>
        /// 清理本局活动与转账限制状态，用于进入新局前重置。
        /// </summary>
        public static void ClearRoundActivityAndTransferState()
        {
            lock (Sync)
            {
                (_state.RoundParticipants ?? (_state.RoundParticipants = new List<AutoPanRoundParticipantRecord>())).Clear();
                (_state.PlayerActivities ?? (_state.PlayerActivities = new List<AutoPanPlayerActivityRecord>())).Clear();
                (_state.TransferLedgers ?? (_state.TransferLedgers = new List<AutoPanTransferLedgerRecord>())).Clear();
            }

            SaveToWorld();
        }

        /// <summary>
        /// 记录玩家本年的一次自动盘活动，同年多次活动只保留一个年份。
        /// </summary>
        public static void RecordPlayerActivity(string userId, string playerName)
        {
            if (!TryGetLiveBinding(userId, out AutoPanBindingRecord binding, out Kingdom kingdom))
            {
                return;
            }

            bool changed;
            lock (Sync)
            {
                changed = RecordPlayerActivityLocked(userId, string.IsNullOrWhiteSpace(playerName) ? binding.PlayerName : playerName, kingdom.getID(), Date.getCurrentYear(), treatAsBinding: false);
            }

            if (changed)
            {
                SaveToWorld();
            }
        }

        /// <summary>
        /// 获取指定国家当前自然成长倍率百分比。
        /// </summary>
        public static int GetGrowthMultiplierPercent(Kingdom kingdom, string category)
        {
            if (kingdom == null || !kingdom.isAlive() || AutoPanConfigHooks.InactiveGrowthEnabled == 0)
            {
                return 100;
            }

            if (!IsCategoryAffected(category))
            {
                return 100;
            }

            kingdom.data.get(AutoPanConstants.KeyOwnerUserId, out string ownerUserId, string.Empty);
            if (string.IsNullOrWhiteSpace(ownerUserId))
            {
                return 100;
            }

            AutoPanPlayerActivityRecord activity;
            lock (Sync)
            {
                activity = CloneActivity(_state.PlayerActivities.FirstOrDefault(item => item != null && string.Equals(item.UserId, ownerUserId, StringComparison.Ordinal)));
            }

            if (activity == null)
            {
                return 100;
            }

            int currentYear = Date.getCurrentYear();
            int boundYear = activity.BoundYear <= 0 ? currentYear : activity.BoundYear;
            int protectionYears = Math.Max(0, AutoPanConfigHooks.ActivityProtectionYears);
            if (protectionYears > 0 && currentYear - boundYear < protectionYears)
            {
                return 100;
            }

            int penaltyLevel = CalculateInactivePenaltyLevel(activity, currentYear);
            if (penaltyLevel <= 0)
            {
                return 100;
            }

            int minPercent = Math.Max(0, Math.Min(100, AutoPanConfigHooks.InactiveGrowthMinPercent));
            int stepPercent = Math.Max(0, AutoPanConfigHooks.InactiveGrowthStepPercent);
            int percent = 100 - penaltyLevel * stepPercent;
            return Math.Max(minPercent, Math.Min(100, percent));
        }

        /// <summary>
        /// 构建所有国家回包里的挂机压制标注。
        /// </summary>
        public static string BuildInactiveGrowthTag(Kingdom kingdom)
        {
            int percent = new[] { "cultivator", "ancient", "beast" }
                .Select(category => GetGrowthMultiplierPercent(kingdom, category))
                .Min();
            return percent >= 100 ? string.Empty : $"（挂机压制 {percent}%）";
        }

        /// <summary>
        /// 记录国家转账流水。
        /// </summary>
        public static void RecordTransfer(long sourceKingdomId, long targetKingdomId, int amount, int receivedAmount, int taxAmount)
        {
            int year = Date.getCurrentYear();
            lock (Sync)
            {
                RemoveTransferLedgersOutsideYearLocked(year);
                _state.TransferLedgers.Add(new AutoPanTransferLedgerRecord
                {
                    Year = year,
                    SourceKingdomId = sourceKingdomId,
                    TargetKingdomId = targetKingdomId,
                    Amount = Math.Max(0, amount),
                    ReceivedAmount = Math.Max(0, receivedAmount),
                    TaxAmount = Math.Max(0, taxAmount)
                });
            }

            SaveToWorld();
        }

        /// <summary>
        /// 获取国家本年已转出的金币总额。
        /// </summary>
        public static int GetYearlyTransferOut(long kingdomId)
        {
            int year = Date.getCurrentYear();
            lock (Sync)
            {
                RemoveTransferLedgersOutsideYearLocked(year);
                return _state.TransferLedgers
                    .Where(item => item != null && item.Year == year && item.SourceKingdomId == kingdomId)
                    .Sum(item => Math.Max(0, item.Amount));
            }
        }

        /// <summary>
        /// 获取国家本年已接收的金币总额。
        /// </summary>
        public static int GetYearlyTransferIn(long kingdomId)
        {
            int year = Date.getCurrentYear();
            lock (Sync)
            {
                RemoveTransferLedgersOutsideYearLocked(year);
                return _state.TransferLedgers
                    .Where(item => item != null && item.Year == year && item.TargetKingdomId == kingdomId)
                    .Sum(item => Math.Max(0, item.ReceivedAmount));
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
                return _state.MessageSequence;
            }
        }

        /// <summary>
        /// 判断国家是否由玩家绑定。
        /// </summary>
        public static bool IsPlayerOwnedKingdom(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.data == null)
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
                RankConfig = AutoPanRankService.BuildSnapshot(),
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

        private static void TrimVolatileStateLocked()
        {
            EnsureStateCollectionsLocked();
            _state.RecentSessions.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.SessionId));
            if (_state.RecentSessions.Count > AutoPanConstants.SessionCapacity)
            {
                _state.RecentSessions = _state.RecentSessions
                    .OrderByDescending(item => item.LastSeenUtc, StringComparer.Ordinal)
                    .Take(AutoPanConstants.SessionCapacity)
                    .OrderBy(item => item.LastSeenUtc, StringComparer.Ordinal)
                    .ToList();
            }

            _state.RoundParticipants = _state.RoundParticipants
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.UserId))
                .GroupBy(item => item.UserId.Trim(), StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList();

            HashSet<string> boundUserIds = new HashSet<string>(_state.Bindings.Keys.Where(item => !string.IsNullOrWhiteSpace(item)), StringComparer.Ordinal);
            int currentYear = Math.Max(1, Date.getCurrentYear());
            int keepYears = Math.Max(
                MinimumActivityYearsToKeep,
                Math.Max(1, AutoPanConfigHooks.ActivityWindowYears)
                    + Math.Max(0, AutoPanConfigHooks.ActivityProtectionYears)
                    + Math.Max(0, AutoPanConfigHooks.ActivityIdleYears)
                    + Math.Max(1, AutoPanConfigHooks.ActivityCoverageYears)
                    + 30);
            int minActivityYear = currentYear - keepYears + 1;
            _state.PlayerActivities = _state.PlayerActivities
                .Where(item =>
                {
                    string userId = item?.UserId?.Trim();
                    return !string.IsNullOrWhiteSpace(userId) && boundUserIds.Contains(userId);
                })
                .GroupBy(item => item.UserId.Trim(), StringComparer.Ordinal)
                .Select(group => NormalizeActivityForSave(group.Last(), minActivityYear, currentYear))
                .Where(item => item != null)
                .ToList();

            RemoveTransferLedgersOutsideYearLocked(currentYear);
        }

        private static void EnsureStateCollectionsLocked()
        {
            if (_state.Bindings == null)
            {
                _state.Bindings = new Dictionary<string, AutoPanBindingRecord>();
            }
            if (_state.RecentSessions == null)
            {
                _state.RecentSessions = new List<AutoPanSessionInfo>();
            }
            if (_state.RoundParticipants == null)
            {
                _state.RoundParticipants = new List<AutoPanRoundParticipantRecord>();
            }
            if (_state.PlayerActivities == null)
            {
                _state.PlayerActivities = new List<AutoPanPlayerActivityRecord>();
            }
            if (_state.TransferLedgers == null)
            {
                _state.TransferLedgers = new List<AutoPanTransferLedgerRecord>();
            }
        }

        private static AutoPanPlayerActivityRecord NormalizeActivityForSave(AutoPanPlayerActivityRecord activity, int minActivityYear, int currentYear)
        {
            if (activity == null)
            {
                return null;
            }

            activity.UserId = activity.UserId?.Trim();
            activity.PlayerName = string.IsNullOrWhiteSpace(activity.PlayerName) ? activity.UserId : activity.PlayerName.Trim();
            activity.BoundYear = activity.BoundYear <= 0 ? currentYear : activity.BoundYear;
            activity.LastActivityYear = activity.LastActivityYear <= 0 ? activity.BoundYear : activity.LastActivityYear;
            activity.InactivePenaltyLevel = Math.Max(0, Math.Min(GetMaxInactivePenaltyLevel(), activity.InactivePenaltyLevel));
            activity.LastPenaltyYear = activity.LastPenaltyYear <= 0 ? activity.LastActivityYear : activity.LastPenaltyYear;
            activity.ActivityYears = (activity.ActivityYears ?? new List<int>())
                .Where(year => year >= minActivityYear && year <= currentYear)
                .Distinct()
                .OrderBy(year => year)
                .ToList();
            return activity;
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

        private static void RecordRoundParticipantLocked(string userId, string playerName)
        {
            string normalizedUserId = (userId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedUserId))
            {
                return;
            }

            AutoPanRoundParticipantRecord existing = _state.RoundParticipants.FirstOrDefault(item => item != null && string.Equals(item.UserId, normalizedUserId, StringComparison.Ordinal));
            if (existing == null)
            {
                _state.RoundParticipants.Add(new AutoPanRoundParticipantRecord
                {
                    UserId = normalizedUserId,
                    PlayerName = string.IsNullOrWhiteSpace(playerName) ? normalizedUserId : playerName.Trim()
                });
                return;
            }

            if (!string.IsNullOrWhiteSpace(playerName))
            {
                existing.PlayerName = playerName.Trim();
            }
        }

        private static bool RecordPlayerActivityLocked(string userId, string playerName, long kingdomId, int year, bool treatAsBinding)
        {
            string normalizedUserId = (userId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedUserId) || kingdomId <= 0 || year <= 0)
            {
                return false;
            }

            if (_state.PlayerActivities == null)
            {
                _state.PlayerActivities = new List<AutoPanPlayerActivityRecord>();
            }

            bool changed = false;
            string normalizedName = string.IsNullOrWhiteSpace(playerName) ? normalizedUserId : playerName.Trim();
            AutoPanPlayerActivityRecord existing = _state.PlayerActivities.FirstOrDefault(item => item != null && string.Equals(item.UserId, normalizedUserId, StringComparison.Ordinal));
            if (existing == null)
            {
                existing = new AutoPanPlayerActivityRecord
                {
                    UserId = normalizedUserId,
                    PlayerName = normalizedName,
                    KingdomId = kingdomId,
                    BoundYear = year,
                    LastActivityYear = year,
                    InactivePenaltyLevel = 0,
                    LastPenaltyYear = year,
                    ActivityYears = new List<int>()
                };
                _state.PlayerActivities.Add(existing);
                changed = true;
            }

            if (!string.Equals(existing.PlayerName, normalizedName, StringComparison.Ordinal))
            {
                existing.PlayerName = normalizedName;
                changed = true;
            }
            if (existing.KingdomId != kingdomId)
            {
                existing.KingdomId = kingdomId;
                changed = true;
            }
            if (treatAsBinding || existing.BoundYear <= 0)
            {
                if (existing.BoundYear != year)
                {
                    existing.BoundYear = year;
                    changed = true;
                }
            }
            if (existing.ActivityYears == null)
            {
                existing.ActivityYears = new List<int>();
                changed = true;
            }
            if (treatAsBinding)
            {
                if (existing.InactivePenaltyLevel != 0)
                {
                    existing.InactivePenaltyLevel = 0;
                    changed = true;
                }
                if (existing.LastPenaltyYear != year)
                {
                    existing.LastPenaltyYear = year;
                    changed = true;
                }
            }
            bool isNewActivityYear = !existing.ActivityYears.Contains(year);
            if (isNewActivityYear)
            {
                if (!treatAsBinding)
                {
                    int currentLevel = CalculateInactivePenaltyLevel(existing, year);
                    int recoveredLevel = Math.Max(0, currentLevel - 1);
                    if (existing.InactivePenaltyLevel != recoveredLevel)
                    {
                        existing.InactivePenaltyLevel = recoveredLevel;
                        changed = true;
                    }
                    if (existing.LastPenaltyYear != year)
                    {
                        existing.LastPenaltyYear = year;
                        changed = true;
                    }
                }

                existing.ActivityYears.Add(year);
                existing.ActivityYears.Sort();
                changed = true;
            }

            int nextLastActivityYear = Math.Max(existing.LastActivityYear, year);
            if (existing.LastActivityYear != nextLastActivityYear)
            {
                existing.LastActivityYear = nextLastActivityYear;
                changed = true;
            }

            return changed;
        }

        private static bool IsCategoryAffected(string category)
        {
            string normalized = (category ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "ancient")
            {
                return AutoPanConfigHooks.InactiveAffectsAncient != 0;
            }

            if (normalized == "beast")
            {
                return AutoPanConfigHooks.InactiveAffectsBeast != 0;
            }

            return AutoPanConfigHooks.InactiveAffectsCultivator != 0;
        }

        private static int CalculateInactivePenaltyLevel(AutoPanPlayerActivityRecord activity, int currentYear)
        {
            if (activity == null)
            {
                return 0;
            }

            int maxLevel = GetMaxInactivePenaltyLevel();
            if (maxLevel <= 0)
            {
                return 0;
            }

            int boundYear = activity.BoundYear <= 0 ? currentYear : activity.BoundYear;
            int protectionYears = Math.Max(0, AutoPanConfigHooks.ActivityProtectionYears);
            int protectionEndYear = boundYear + protectionYears;
            if (protectionYears > 0 && currentYear < protectionEndYear)
            {
                return 0;
            }

            int idleYears = Math.Max(0, AutoPanConfigHooks.ActivityIdleYears);
            int level = Math.Max(0, Math.Min(maxLevel, activity.InactivePenaltyLevel));
            if (idleYears <= 0)
            {
                return level;
            }

            int coverageYears = Math.Max(1, AutoPanConfigHooks.ActivityCoverageYears);
            int lastActivityYear = activity.LastActivityYear <= 0 ? boundYear : activity.LastActivityYear;
            int lastPenaltyYear = activity.LastPenaltyYear <= 0 ? lastActivityYear : activity.LastPenaltyYear;
            int idleReferenceYear = Math.Max(Math.Max(lastPenaltyYear, protectionEndYear), lastActivityYear + coverageYears - 1);
            int idleDuration = Math.Max(0, currentYear - idleReferenceYear);
            int extraLevels = idleDuration / idleYears;
            return Math.Max(0, Math.Min(maxLevel, level + extraLevels));
        }

        private static int GetMaxInactivePenaltyLevel()
        {
            int minPercent = Math.Max(0, Math.Min(100, AutoPanConfigHooks.InactiveGrowthMinPercent));
            int stepPercent = Math.Max(0, AutoPanConfigHooks.InactiveGrowthStepPercent);
            if (minPercent >= 100 || stepPercent <= 0)
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Ceiling((100 - minPercent) / (double)stepPercent));
        }

        private static void RemoveTransferLedgersOutsideYearLocked(int year)
        {
            if (_state.TransferLedgers == null)
            {
                _state.TransferLedgers = new List<AutoPanTransferLedgerRecord>();
                return;
            }

            _state.TransferLedgers.RemoveAll(item => item == null || item.Year != year);
        }

        private static AutoPanPlayerActivityRecord CloneActivity(AutoPanPlayerActivityRecord activity)
        {
            if (activity == null)
            {
                return null;
            }

            return new AutoPanPlayerActivityRecord
            {
                UserId = activity.UserId,
                PlayerName = activity.PlayerName,
                KingdomId = activity.KingdomId,
                BoundYear = activity.BoundYear,
                LastActivityYear = activity.LastActivityYear,
                InactivePenaltyLevel = activity.InactivePenaltyLevel,
                LastPenaltyYear = activity.LastPenaltyYear,
                ActivityYears = new List<int>(activity.ActivityYears ?? new List<int>())
            };
        }

        private static AutoPanRoundParticipantRecord CloneParticipant(AutoPanRoundParticipantRecord participant)
        {
            if (participant == null)
            {
                return null;
            }

            return new AutoPanRoundParticipantRecord
            {
                UserId = participant.UserId,
                PlayerName = participant.PlayerName
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
