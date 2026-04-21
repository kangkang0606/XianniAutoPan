using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XianniAutoPan.Model;
using xn.api;
using xn.tournament;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 管理结盟与约斗的短时请求与应答。
    /// </summary>
    internal static class AutoPanRequestService
    {
        private sealed class PendingRequest
        {
            /// <summary>
            /// 请求唯一标识。
            /// </summary>
            public string Id;

            /// <summary>
            /// 请求类型。
            /// </summary>
            public AutoPanPendingRequestType Type;

            /// <summary>
            /// 发起方国家 ID。
            /// </summary>
            public long SourceKingdomId;

            /// <summary>
            /// 接收方国家 ID。
            /// </summary>
            public long TargetKingdomId;

            /// <summary>
            /// 发起时预扣的金币。
            /// </summary>
            public int ReservedCost;

            /// <summary>
            /// 到期时间。
            /// </summary>
            public float ExpiresAt;

            /// <summary>
            /// 约斗赌注金额。
            /// </summary>
            public int BetAmount;
        }

        private static readonly List<PendingRequest> PendingRequests = new List<PendingRequest>();

        /// <summary>
        /// 每帧清理过期请求。
        /// </summary>
        public static void Update()
        {
            if (PendingRequests.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            for (int index = PendingRequests.Count - 1; index >= 0; index--)
            {
                PendingRequest request = PendingRequests[index];
                if (request == null)
                {
                    PendingRequests.RemoveAt(index);
                    continue;
                }

                Kingdom source = World.world?.kingdoms?.get(request.SourceKingdomId);
                Kingdom target = World.world?.kingdoms?.get(request.TargetKingdomId);
                if (source == null || !source.isAlive() || target == null || !target.isAlive() || now < request.ExpiresAt)
                {
                    continue;
                }

                RefundRequest(source, request);
                AutoPanLogService.Info($"{GetTypeText(request.Type)} 请求已超时：{AutoPanKingdomService.FormatKingdomLabel(source)} -> {AutoPanKingdomService.FormatKingdomLabel(target)}");
                string timeoutText = $"{AutoPanKingdomService.FormatKingdomLabel(source)} 发给 {AutoPanKingdomService.FormatKingdomLabel(target)} 的{GetTypeText(request.Type)}请求已超时";
                XianniAutoPanApi.Broadcast(timeoutText);
                AutoPanNotificationService.BroadcastToKnownGroups(timeoutText, GetOwnerUserIds(source, target));
                PendingRequests.RemoveAt(index);
            }
        }

        /// <summary>
        /// 清理所有待处理请求并退回预扣金币。
        /// </summary>
        public static void ClearAll()
        {
            foreach (PendingRequest request in PendingRequests.ToList())
            {
                Kingdom source = World.world?.kingdoms?.get(request.SourceKingdomId);
                if (source != null && source.isAlive())
                {
                    RefundRequest(source, request);
                }
            }

            PendingRequests.Clear();
        }

        /// <summary>
        /// 获取某个国家收到的待处理请求。
        /// </summary>
        public static List<AutoPanPendingRequestSnapshot> BuildPendingSnapshots(Kingdom targetKingdom)
        {
            List<AutoPanPendingRequestSnapshot> result = new List<AutoPanPendingRequestSnapshot>();
            if (targetKingdom == null || !targetKingdom.isAlive())
            {
                return result;
            }

            float now = Time.unscaledTime;
            foreach (PendingRequest request in PendingRequests)
            {
                if (request == null || request.TargetKingdomId != targetKingdom.getID())
                {
                    continue;
                }

                Kingdom source = World.world?.kingdoms?.get(request.SourceKingdomId);
                if (source == null || !source.isAlive())
                {
                    continue;
                }

                result.Add(new AutoPanPendingRequestSnapshot
                {
                    RequestType = GetTypeText(request.Type),
                    SourceKingdomLabel = AutoPanKingdomService.FormatKingdomLabel(source),
                    TargetKingdomLabel = AutoPanKingdomService.FormatKingdomLabel(targetKingdom),
                    SecondsRemaining = Mathf.Max(0, Mathf.CeilToInt(request.ExpiresAt - now)),
                    DetailsText = request.Type == AutoPanPendingRequestType.Duel && request.BetAmount > 0 ? $"赌注 {request.BetAmount} 金币" : string.Empty
                });
            }

            return result.OrderBy(item => item.RequestType).ThenBy(item => item.SourceKingdomLabel).ToList();
        }

        /// <summary>
        /// 创建结盟请求。
        /// </summary>
        public static bool TryCreateAllianceRequest(Kingdom source, Kingdom target, out string message)
        {
            message = string.Empty;
            if (source == null || target == null || source == target)
            {
                message = "结盟目标无效。";
                return false;
            }

            if (Alliance.isSame(source.getAlliance(), target.getAlliance()))
            {
                message = $"你与 {AutoPanKingdomService.FormatKingdomLabel(target)} 已在同一联盟中。";
                return false;
            }

            if (FindDuplicate(source.getID(), target.getID(), AutoPanPendingRequestType.Alliance) != null)
            {
                message = $"你已向 {AutoPanKingdomService.FormatKingdomLabel(target)} 发出结盟请求，请等待对方回应。";
                return false;
            }

            int cost = AutoPanConfigHooks.AllianceRequestCost;
            if (!AutoPanKingdomService.TrySpendTreasury(source, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            PendingRequest request = CreateRequest(source, target, AutoPanPendingRequestType.Alliance, cost);
            PendingRequests.Add(request);
            AutoPanKingdomSpeechService.ShowSpeech(target, "外交请求", $"{AutoPanKingdomService.FormatKingdomLabel(source)} 请求结盟", isCommand: true);
            message = $"{AutoPanKingdomService.FormatKingdomLabel(source)} 已向 {AutoPanKingdomService.FormatKingdomLabel(target)} 发出结盟请求，对方需在 {AutoPanConfigHooks.RequestTimeoutSeconds} 秒内发送"同意结盟"或"拒绝结盟"。";
            if (TryAutoRespondByAi(request, source, target, out string aiResponseText))
            {
                message += "\n" + aiResponseText;
            }
            return true;
        }

        /// <summary>
        /// 创建战力前五随机约斗请求。
        /// </summary>
        public static bool TryCreateDuelRequest(Kingdom source, Kingdom target, int betAmount, out string message)
        {
            message = string.Empty;
            if (source == null || target == null || source == target)
            {
                message = "约斗目标无效。";
                return false;
            }

            if (AutoPanDuelService.IsRunning)
            {
                message = "当前已有一场约斗在进行中，请稍后再试。";
                return false;
            }

            if (TournamentManager.IsRunning)
            {
                message = "仙逆比武大会正在进行中，不能发起约斗。";
                return false;
            }

            if (FindDuplicate(source.getID(), target.getID(), AutoPanPendingRequestType.Duel) != null)
            {
                message = $"你已向 {AutoPanKingdomService.FormatKingdomLabel(target)} 发出约斗请求，请等待对方回应。";
                return false;
            }

            // 未指定赌注时默认为发起费的两倍，强制要求赌注参与
            int resolvedBet = betAmount > 0 ? betAmount : AutoPanConfigHooks.DuelRequestCost * 2;
            int cost = AutoPanConfigHooks.DuelRequestCost;
            if (!AutoPanKingdomService.TrySpendTreasury(source, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            PendingRequest request = CreateRequest(source, target, AutoPanPendingRequestType.Duel, cost);
            request.BetAmount = resolvedBet;
            PendingRequests.Add(request);
            string duelTitle = $"发起约斗，赌注 {request.BetAmount} 金币";
            AutoPanKingdomSpeechService.ShowSpeech(target, "比武邀请", $"{AutoPanKingdomService.FormatKingdomLabel(source)} {duelTitle}", isCommand: true);
            string defaultBetNote = betAmount <= 0 ? $"（未指定赌注，已按默认赌注 {resolvedBet} 金币计算）" : string.Empty;
            message = $"{AutoPanKingdomService.FormatKingdomLabel(source)} 已向 {AutoPanKingdomService.FormatKingdomLabel(target)} 发出约斗请求，赌注 {request.BetAmount} 金币{defaultBetNote}，开战后双方各从国家战力前 5 随机出战，对方需在 {AutoPanConfigHooks.RequestTimeoutSeconds} 秒内回复「同意约斗」或「拒绝约斗」。";
            if (TryAutoRespondByAi(request, source, target, out string aiResponseText))
            {
                message += "\n" + aiResponseText;
            }
            return true;
        }

        /// <summary>
        /// 回应结盟请求。
        /// </summary>
        public static bool TryRespondAllianceRequest(Kingdom target, string rawSourceKingdomName, bool accept, out string message)
        {
            return TryRespondRequest(target, rawSourceKingdomName, AutoPanPendingRequestType.Alliance, accept, out message);
        }

        /// <summary>
        /// 回应约斗请求。
        /// </summary>
        public static bool TryRespondDuelRequest(Kingdom target, string rawSourceKingdomName, bool accept, out string message)
        {
            return TryRespondRequest(target, rawSourceKingdomName, AutoPanPendingRequestType.Duel, accept, out message);
        }

        private static bool TryRespondRequest(Kingdom target, string rawSourceKingdomName, AutoPanPendingRequestType type, bool accept, out string message)
        {
            message = string.Empty;
            if (target == null || !target.isAlive())
            {
                message = "当前国家状态无效。";
                return false;
            }

            PendingRequest request = ResolveIncomingRequest(target, rawSourceKingdomName, type, out string resolveError);
            if (request == null)
            {
                message = resolveError;
                return false;
            }

            Kingdom source = World.world?.kingdoms?.get(request.SourceKingdomId);
            if (source == null || !source.isAlive())
            {
                PendingRequests.Remove(request);
                message = "请求发起国已失效，该请求已自动清除。";
                return false;
            }

            if (type == AutoPanPendingRequestType.Duel && accept && TournamentManager.IsRunning)
            {
                message = "仙逆比武大会正在进行中，不能同意约斗。";
                return false;
            }

            PendingRequests.Remove(request);
            if (!accept)
            {
                RefundRequest(source, request);
                message = $"{AutoPanKingdomService.FormatKingdomLabel(target)} 已拒绝来自 {AutoPanKingdomService.FormatKingdomLabel(source)} 的{GetTypeText(type)}请求。";
                XianniAutoPanApi.Broadcast(message);
                return true;
            }

            if (type == AutoPanPendingRequestType.Alliance)
            {
                if (!AutoPanKingdomService.TryCreateOrMergeAlliance(source, target))
                {
                    RefundRequest(source, request);
                    message = $"与 {AutoPanKingdomService.FormatKingdomLabel(source)} 结盟失败，已退回请求花费。";
                    return false;
                }

                message = $"{AutoPanKingdomService.FormatKingdomLabel(target)} 已同意与 {AutoPanKingdomService.FormatKingdomLabel(source)} 结盟。";
                XianniAutoPanApi.Broadcast($"{AutoPanKingdomService.FormatKingdomLabel(source)} 与 {AutoPanKingdomService.FormatKingdomLabel(target)} 正式结盟");
                return true;
            }

            if (!AutoPanDuelService.TryStartStrongestDuel(source, target, request.BetAmount, out string duelMessage))
            {
                RefundRequest(source, request);
                message = duelMessage;
                return false;
            }

            message = duelMessage;
            return true;
        }

        private static PendingRequest ResolveIncomingRequest(Kingdom target, string rawSourceKingdomName, AutoPanPendingRequestType type, out string error)
        {
            error = string.Empty;
            List<PendingRequest> candidates = PendingRequests
                .Where(item => item != null && item.TargetKingdomId == target.getID() && item.Type == type)
                .ToList();
            if (candidates.Count == 0)
            {
                error = $"当前没有待处理的{GetTypeText(type)}请求。";
                return null;
            }

            if (string.IsNullOrWhiteSpace(rawSourceKingdomName))
            {
                if (candidates.Count == 1)
                {
                    return candidates[0];
                }

                error = $"你收到多个{GetTypeText(type)}请求，请改用"同意{GetTypeText(type)} 国家名 [kingdomId]"或"拒绝{GetTypeText(type)} 国家名 [kingdomId]"。";
                return null;
            }

            if (!AutoPanKingdomService.TryResolveKingdom(rawSourceKingdomName, out Kingdom source, out error))
            {
                return null;
            }

            PendingRequest matched = candidates.FirstOrDefault(item => item.SourceKingdomId == source.getID());
            if (matched != null)
            {
                return matched;
            }

            error = $"当前没有来自 {AutoPanKingdomService.FormatKingdomLabel(source)} 的{GetTypeText(type)}请求。";
            return null;
        }

        private static bool TryAutoRespondByAi(PendingRequest request, Kingdom source, Kingdom target, out string message)
        {
            message = string.Empty;
            if (!AutoPanConfigHooks.EnableLlmAi || request == null || source == null || target == null || AutoPanStateRepository.IsPlayerOwnedKingdom(target))
            {
                return false;
            }

            bool accept = ShouldAiAcceptRequest(request, source, target);
            if (!TryRespondRequest(target, AutoPanKingdomService.FormatKingdomLabel(source), request.Type, accept, out string responseText))
            {
                message = $"Ai:{target.name}回应失败";
                return true;
            }

            string actionText = accept ? "同意" : "拒绝";
            message = $"Ai:{target.name}已{actionText}{GetTypeText(request.Type)}";
            return true;
        }

        private static bool ShouldAiAcceptRequest(PendingRequest request, Kingdom source, Kingdom target)
        {
            if (request.Type == AutoPanPendingRequestType.Alliance)
            {
                return target.getWars().Count() <= 2 && !target.isEnemy(source);
            }

            return !AutoPanDuelService.IsRunning && !TournamentManager.IsRunning;
        }

        private static PendingRequest FindDuplicate(long sourceKingdomId, long targetKingdomId, AutoPanPendingRequestType type)
        {
            return PendingRequests.FirstOrDefault(item =>
                item != null
                && item.SourceKingdomId == sourceKingdomId
                && item.TargetKingdomId == targetKingdomId
                && item.Type == type);
        }

        private static PendingRequest CreateRequest(Kingdom source, Kingdom target, AutoPanPendingRequestType type, int reservedCost)
        {
            return new PendingRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = type,
                SourceKingdomId = source.getID(),
                TargetKingdomId = target.getID(),
                ReservedCost = reservedCost,
                ExpiresAt = Time.unscaledTime + AutoPanConfigHooks.RequestTimeoutSeconds
            };
        }

        private static void RefundRequest(Kingdom source, PendingRequest request)
        {
            if (source == null || request == null || request.ReservedCost <= 0)
            {
                return;
            }

            AutoPanKingdomService.AddTreasury(source, request.ReservedCost);
        }

        private static string GetTypeText(AutoPanPendingRequestType type)
        {
            return type == AutoPanPendingRequestType.Alliance ? "结盟" : "约斗";
        }

        private static List<string> GetOwnerUserIds(params Kingdom[] kingdoms)
        {
            return (kingdoms ?? new Kingdom[0])
                .Where(item => item != null)
                .SelectMany(item => AutoPanStateRepository.GetBindingsByKingdomId(item.getID()))
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.UserId))
                .Select(item => item.UserId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }
}
