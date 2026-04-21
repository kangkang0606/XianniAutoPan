using System;
using System.Collections.Generic;
using System.Linq;
using XianniAutoPan.Model;
using xn.api;
using xn.tournament;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 管理自动盘指令开启的仙逆比武大会与结束后的国家国库奖励。
    /// </summary>
    internal static class AutoPanTournamentService
    {
        private static bool _pendingReward;
        private static int _historyCountAtStart;
        private static int _startYear;
        private static string _openerUserId = string.Empty;
        private static string _openerName = string.Empty;

        /// <summary>
        /// 尝试由玩家付费开启仙逆比武大会。
        /// </summary>
        public static bool TryStart(Kingdom opener, string openerUserId, string openerName, out string message)
        {
            message = string.Empty;
            if (opener == null || !opener.isAlive() || !opener.isCiv())
            {
                message = "当前国家状态无效，无法开启比武大会。";
                return false;
            }

            if (TournamentManager.IsRunning)
            {
                message = "仙逆比武大会正在进行中，不能重复开启。";
                return false;
            }

            if (AutoPanDuelService.IsRunning)
            {
                message = "当前已有约斗正在进行中，不能开启比武大会。";
                return false;
            }

            int cost = AutoPanConfigHooks.TournamentOpenCost;
            if (!AutoPanKingdomService.TrySpendTreasury(opener, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            int historyCount = TournamentHistoryStorage.GetCount();
            if (!TournamentManager.StartTournament())
            {
                AutoPanKingdomService.AddTreasury(opener, cost);
                message = $"仙逆比武大会开启失败，已退回 {cost} 金币。";
                return false;
            }

            _pendingReward = true;
            _historyCountAtStart = historyCount;
            _startYear = Date.getCurrentYear();
            _openerUserId = openerUserId ?? string.Empty;
            _openerName = string.IsNullOrWhiteSpace(openerName) ? "未知玩家" : openerName.Trim();

            string openerLabel = AutoPanKingdomService.FormatKingdomLabel(opener);
            message = $"{openerLabel} 已消耗 {cost} 金币开启仙逆比武大会。比赛结束后，自动盘会按第 1~3 名当前所属国家发放国库奖励。";
            XianniAutoPanApi.Broadcast($"{openerLabel} 开启了仙逆比武大会");
            AutoPanLogService.Info($"{_openerName} 开启比武大会：{openerLabel} / cost={cost} / historyCount={historyCount}");
            return true;
        }

        /// <summary>
        /// 每帧轮询仙逆历史记录，处理自动盘开启的比武大会奖励。
        /// </summary>
        public static void Update()
        {
            if (!_pendingReward || TournamentManager.IsRunning)
            {
                return;
            }

            List<TournamentHistoryData> histories = TournamentHistoryStorage.GetAllHistories();
            if (histories.Count <= _historyCountAtStart)
            {
                return;
            }

            TournamentHistoryData history = histories
                .Skip(_historyCountAtStart)
                .OrderBy(item => item?.Edition ?? int.MaxValue)
                .FirstOrDefault(item => item != null);
            if (history == null)
            {
                return;
            }

            _pendingReward = false;
            RewardHistory(history);
        }

        /// <summary>
        /// 清理等待奖励状态，通常用于世界重载或模组卸载。
        /// </summary>
        public static void Clear()
        {
            _pendingReward = false;
            _historyCountAtStart = 0;
            _startYear = 0;
            _openerUserId = string.Empty;
            _openerName = string.Empty;
        }

        private static void RewardHistory(TournamentHistoryData history)
        {
            AutoPanConfigHooks.RollRandomPolicyValuesForOperation();
            List<string> lines = new List<string>
            {
                $"第 {history.Edition} 届比武大会已结束，自动盘国库奖励如下："
            };
            List<string> atUserIds = new List<string>();

            lines.Add(BuildRewardLine(1, history.ChampionId, history.ChampionInfo, history.ChampionName, AutoPanConfigHooks.TournamentFirstReward, atUserIds));
            lines.Add(BuildRewardLine(2, history.RunnerUpId, history.RunnerUpInfo, history.RunnerUpName, AutoPanConfigHooks.TournamentSecondReward, atUserIds));
            lines.Add(BuildRewardLine(3, history.ThirdPlaceId, history.ThirdPlaceInfo, history.ThirdPlaceName, AutoPanConfigHooks.TournamentThirdReward, atUserIds));

            if (!string.IsNullOrWhiteSpace(_openerName))
            {
                lines.Add($"开启者：{_openerName}，开启年份：第 {_startYear} 年。");
            }

            string text = string.Join("\n", lines);
            XianniAutoPanApi.Broadcast(text);
            AutoPanNotificationService.BroadcastToKnownGroups(text, atUserIds);

            AutoPanLogService.Info(text.Replace("\n", " "));
            Clear();
        }

        private static string BuildRewardLine(int place, string actorIdText, ParticipantDisplayInfo info, string legacyName, int reward, List<string> atUserIds)
        {
            string actorName = BuildParticipantName(info, legacyName);
            if (!long.TryParse(actorIdText, out long actorId))
            {
                return $"{place}. {actorName}：单位记录无效，无法发放 {reward} 金币。";
            }

            Actor actor = World.world?.units?.get(actorId);
            Kingdom kingdom = actor?.kingdom;
            if (actor == null || !actor.isAlive() || kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
            {
                return $"{place}. {actorName}：单位或所属国家已失效，无法发放 {reward} 金币。";
            }

            int treasury = AutoPanKingdomService.AddTreasury(kingdom, Math.Max(0, reward));
            string kingdomLabel = AutoPanKingdomService.FormatKingdomLabel(kingdom);
            string line = $"{place}. {actorName}，所属国家 {kingdomLabel}，奖励 {Math.Max(0, reward)} 金币，当前国库 {treasury}。";
            foreach (AutoPanBindingRecord binding in AutoPanStateRepository.GetBindingsByKingdomId(kingdom.getID()))
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.UserId) || atUserIds.Contains(binding.UserId))
                {
                    continue;
                }

                atUserIds.Add(binding.UserId);
            }

            AutoPanNotificationService.NotifyKingdomOwners(kingdom, line);
            return line;
        }

        private static string BuildParticipantName(ParticipantDisplayInfo info, string legacyName)
        {
            string name = info?.GetFullDisplayName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return string.IsNullOrWhiteSpace(legacyName) ? "未知单位" : legacyName.Trim();
        }
    }
}
