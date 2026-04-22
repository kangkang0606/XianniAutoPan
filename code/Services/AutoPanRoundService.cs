using System;
using System.Collections.Generic;
using System.Linq;
using XianniAutoPan.Model;
using xn.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 管理自动盘结盘、积分结算与新局生成。
    /// </summary>
    internal static class AutoPanRoundService
    {
        private const string AiScorePlayerName = "AI";

        private sealed class RoundCandidate
        {
            /// <summary>
            /// 积分归属用户 ID。
            /// </summary>
            public string UserId;

            /// <summary>
            /// 积分归属显示名。
            /// </summary>
            public string PlayerName;

            /// <summary>
            /// 是否为无绑定 AI 国家。
            /// </summary>
            public bool IsAi;

            /// <summary>
            /// 国家 ID。
            /// </summary>
            public long KingdomId;

            /// <summary>
            /// 国家标签。
            /// </summary>
            public string KingdomLabel;

            /// <summary>
            /// 国家领土格数。
            /// </summary>
            public int TerritoryCount;

            /// <summary>
            /// 国家战力榜前 3 综合战力。
            /// </summary>
            public long TopPowerScore;

            /// <summary>
            /// 国家等级。
            /// </summary>
            public int NationLevel;

            /// <summary>
            /// 人口数量。
            /// </summary>
            public int Population;

            /// <summary>
            /// 军队数量。
            /// </summary>
            public int ArmyCount;

            /// <summary>
            /// 结盘综合评分。
            /// </summary>
            public double Score;
        }

        private const string GaiaLawId = "world_law_gaias_covenant";
        private const string HundredPopulationLawId = "world_law_civ_limit_population_100";
        private const string EvolutionEventsLawId = "world_law_evolution_events";
        private const string DiplomacyLawId = "world_law_diplomacy";
        private const string DiplomacyGroupId = "diplomacy";
        private static bool _isEndingRound;
        private static bool _isGeneratingNewRound;
        private static bool _pendingNewRoundNotice;
        private static bool _diplomacyAutoOpenedForWorld;
        private static int _lastAutoRoundYear = -1;

        /// <summary>
        /// 初始化结盘服务运行状态。
        /// </summary>
        public static void Initialize()
        {
            _isEndingRound = false;
            _isGeneratingNewRound = false;
            _pendingNewRoundNotice = false;
            _diplomacyAutoOpenedForWorld = false;
            _lastAutoRoundYear = -1;
        }

        /// <summary>
        /// 在年度结算后检查是否达到自动结盘年份。
        /// </summary>
        public static bool CheckAutoEndRound(int currentYear)
        {
            EnsureDiplomacyLawsForYear(currentYear);
            if (_isEndingRound || _isGeneratingNewRound || currentYear < AutoPanConfigHooks.RoundEndYear || currentYear == _lastAutoRoundYear)
            {
                return false;
            }

            _lastAutoRoundYear = currentYear;
            EndRound($"达到第 {currentYear} 年自动结盘", "系统");
            return true;
        }

        /// <summary>
        /// 结算当前局并启动新一局。
        /// </summary>
        /// <summary>
        /// 管理员结盘，所有国家不计入积分。
        /// </summary>
        public static string EndRoundNoScore(string reason, string operatorName)
        {
            if (_isEndingRound || _isGeneratingNewRound)
            {
                return "当前正在结盘或生成新一局，请稍后再试。";
            }

            _isEndingRound = true;
            try
            {
                string resultText = BuildRoundResult(reason, operatorName, skipScore: true);
                XianniAutoPanApi.Broadcast(resultText);
                AutoPanNotificationService.BroadcastToKnownGroups(resultText, ExtractWinnerUserIds());
                AutoPanLogService.Info(resultText.Replace("\n", " "));
                bool started = StartNewRound();
                return resultText + (started ? "\n新一局正在生成。" : "\n新一局生成未启动：当前世界仍在加载或 MapBox 未就绪。");
            }
            finally
            {
                _isEndingRound = false;
            }
        }

        public static string EndRound(string reason, string operatorName)
        {
            if (_isEndingRound || _isGeneratingNewRound)
            {
                return "当前正在结盘或生成新一局，请稍后再试。";
            }

            _isEndingRound = true;
            try
            {
                string resultText = BuildRoundResult(reason, operatorName, skipScore: false);
                XianniAutoPanApi.Broadcast(resultText);
                AutoPanNotificationService.BroadcastToKnownGroups(resultText, ExtractWinnerUserIds());
                AutoPanLogService.Info(resultText.Replace("\n", " "));
                bool started = StartNewRound();
                return resultText + (started ? "\n新一局正在生成。" : "\n新一局生成未启动：当前世界仍在加载或 MapBox 未就绪。");
            }
            finally
            {
                _isEndingRound = false;
            }
        }

        /// <summary>
        /// 世界加载完成后应用新局法则，并按需播报新局开启。
        /// </summary>
        public static void OnWorldLoaded()
        {
            ApplyRoundWorldRules();
            EnsureDiplomacyLawsForYear(Date.getCurrentYear());
            _isGeneratingNewRound = false;
            if (!_pendingNewRoundNotice)
            {
                return;
            }

            _pendingNewRoundNotice = false;
            AutoPanConfigHooks.RollRandomPolicyValuesForOperation();
            SpawnAiKingdomsIfConfigured();
            const string text = "新一局游戏已经开启。";
            XianniAutoPanApi.Broadcast(text);
            AutoPanNotificationService.BroadcastToKnownGroups(text);
        }

        /// <summary>
        /// 根据配置在新盘开启时自动生成 AI 国家。
        /// </summary>
        private static void SpawnAiKingdomsIfConfigured()
        {
            int count = AutoPanConfigHooks.AiAutoJoinCount;
            if (count <= 0)
            {
                return;
            }

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (AutoPanKingdomService.TrySpawnUnboundKingdom("随机", out string message))
                {
                    spawned++;
                    AutoPanLogService.Info($"新盘自动生成 AI 国家 ({i + 1}/{count})：{message}");
                }
                else
                {
                    AutoPanLogService.Error($"新盘自动生成 AI 国家失败 ({i + 1}/{count})：{message}");
                }
            }

            if (spawned > 0)
            {
                string notice = $"新盘已自动生成 {spawned} 个 AI 国家。";
                XianniAutoPanApi.Broadcast(notice);
                AutoPanNotificationService.BroadcastToKnownGroups(notice);
            }
        }

        private static string BuildRoundResult(string reason, string operatorName, bool skipScore = false)
        {
            List<RoundCandidate> candidates = BuildRankedCandidates();
            if (candidates.Count == 0)
            {
                return $"本局结盘：{reason}。没有可结算国家，未增加积分。";
            }

            RoundCandidate winner = candidates.First();
            List<RoundCandidate> topThree = candidates.Take(3).ToList();
            List<string> awardLines = new List<string>();
            for (int index = 0; index < topThree.Count; index++)
            {
                RoundCandidate candidate = topThree[index];
                int points = GetPlacePoints(index);
                string rankStats = $"领土 {candidate.TerritoryCount}，战力榜前三 {candidate.TopPowerScore}，人口 {candidate.Population}，军队 {candidate.ArmyCount}，综合分 {candidate.Score:0.###}";
                if (skipScore)
                {
                    awardLines.Add($"{index + 1}. {BuildCandidateOwnerText(candidate)} 的 {candidate.KingdomLabel}：{rankStats}，本局不计积分。");
                    continue;
                }

                if (candidate.IsAi)
                {
                    awardLines.Add($"{index + 1}. {BuildCandidateOwnerText(candidate)} 的 {candidate.KingdomLabel}：{rankStats}，AI 不计入积分。");
                    continue;
                }

                if (points <= 0)
                {
                    awardLines.Add($"{index + 1}. {BuildCandidateOwnerText(candidate)} 的 {candidate.KingdomLabel}：{rankStats}，+0 分。");
                    continue;
                }

                int totalPoints = AutoPanScoreService.AddPoints(candidate.UserId, candidate.PlayerName, points);
                awardLines.Add($"{index + 1}. {BuildCandidateOwnerText(candidate)} 的 {candidate.KingdomLabel}：{rankStats}，+{points} 分，累计 {totalPoints} 分。");
            }

            List<string> lines = new List<string>
            {
                $"本局结盘：{reason}。",
                $"胜者：{BuildCandidateOwnerText(winner)} 的 {winner.KingdomLabel}，领土 {winner.TerritoryCount}，战力榜前三 {winner.TopPowerScore}，人口 {winner.Population}，军队 {winner.ArmyCount}，综合分 {winner.Score:0.###}。",
                winner.IsAi ? "本轮结果：AI 胜利。" : $"本轮结果：玩家 {winner.PlayerName} 胜利。",
                "裁定规则：国家领土 40%、战力榜前三 30%、人口 20%、军队数量 10% 综合评分。",
                "积分发放：",
                string.Join("\n", awardLines)
            };

            if (!string.IsNullOrWhiteSpace(operatorName))
            {
                lines.Add($"结盘操作：{operatorName}。");
            }

            return string.Join("\n", lines);
        }

        private static List<string> ExtractWinnerUserIds()
        {
            return BuildRankedCandidates()
                .Take(1)
                .Where(item => !item.IsAi)
                .Select(item => item.UserId)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        private static List<RoundCandidate> BuildRankedCandidates()
        {
            return BuildCandidates()
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.TerritoryCount)
                .ThenByDescending(item => item.TopPowerScore)
                .ThenByDescending(item => item.Population)
                .ThenByDescending(item => item.ArmyCount)
                .ThenBy(item => item.KingdomId)
                .ToList();
        }

        private static List<RoundCandidate> BuildCandidates()
        {
            List<RoundCandidate> result = new List<RoundCandidate>();
            if (World.world?.kingdoms == null)
            {
                return result;
            }

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
                {
                    continue;
                }

                kingdom.data.get(AutoPanConstants.KeyOwnerUserId, out string ownerUserId, string.Empty);
                kingdom.data.get(AutoPanConstants.KeyOwnerName, out string ownerName, ownerUserId);
                bool isAi = string.IsNullOrWhiteSpace(ownerUserId);
                int territory = kingdom.countZones();
                long topPowerScore = AutoPanKingdomService.SumTopPowerScore(kingdom, 3);
                int population = kingdom.getPopulationTotal();
                int armyCount = AutoPanKingdomService.CountArmyUnits(kingdom);
                result.Add(new RoundCandidate
                {
                    UserId = isAi ? string.Empty : ownerUserId,
                    PlayerName = isAi ? AiScorePlayerName : string.IsNullOrWhiteSpace(ownerName) ? ownerUserId : ownerName,
                    IsAi = isAi,
                    KingdomId = kingdom.getID(),
                    KingdomLabel = AutoPanKingdomService.FormatKingdomLabel(kingdom),
                    TerritoryCount = territory,
                    NationLevel = AutoPanKingdomService.GetLevel(kingdom),
                    TopPowerScore = topPowerScore,
                    Population = population,
                    ArmyCount = armyCount
                });
            }

            ApplyCandidateScores(result);
            return result;
        }

        private static void ApplyCandidateScores(List<RoundCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            int maxTerritory = Math.Max(1, candidates.Max(item => item.TerritoryCount));
            long maxPower = Math.Max(1L, candidates.Max(item => item.TopPowerScore));
            int maxPopulation = Math.Max(1, candidates.Max(item => item.Population));
            int maxArmy = Math.Max(1, candidates.Max(item => item.ArmyCount));
            foreach (RoundCandidate candidate in candidates)
            {
                candidate.Score =
                    BuildWeightedScore(candidate.TerritoryCount, maxTerritory, 40d) +
                    BuildWeightedScore(candidate.TopPowerScore, maxPower, 30d) +
                    BuildWeightedScore(candidate.Population, maxPopulation, 20d) +
                    BuildWeightedScore(candidate.ArmyCount, maxArmy, 10d);
            }
        }

        private static double BuildWeightedScore(double value, double maxValue, double weight)
        {
            return maxValue <= 0d ? 0d : value / maxValue * weight;
        }

        private static int GetPlacePoints(int zeroBasedIndex)
        {
            switch (zeroBasedIndex)
            {
                case 0:
                    return AutoPanConfigHooks.RoundFirstPlacePoints;
                case 1:
                    return AutoPanConfigHooks.RoundSecondPlacePoints;
                case 2:
                    return AutoPanConfigHooks.RoundThirdPlacePoints;
                default:
                    return 0;
            }
        }

        private static string BuildCandidateOwnerText(RoundCandidate candidate)
        {
            if (candidate == null)
            {
                return "未知";
            }

            return candidate.IsAi ? AiScorePlayerName : candidate.PlayerName;
        }

        private static bool StartNewRound()
        {
            if (Config.worldLoading || MapBox.instance == null)
            {
                AutoPanLogService.Error("结盘后生成新局失败：当前世界仍在加载或 MapBox 未就绪。");
                return false;
            }

            AutoPanRequestService.ClearAll();
            AutoPanDuelService.ClearAll();
            AutoPanTournamentService.Clear();
            AutoPanKingdomSpeechService.ClearAll();
            Config.customMapSize = "iceberg";
            Config.current_map_template = "box_world";
            AutoPanRoundUiService.RequestHideAfterNextWorldLoad();
            _isGeneratingNewRound = true;
            _pendingNewRoundNotice = true;
            MapBox.instance.generateNewMap();
            return true;
        }

        private static void ApplyRoundWorldRules()
        {
            if (World.world?.world_laws == null || AssetManager.world_laws_library == null)
            {
                return;
            }

            World.world.world_laws.init();
            foreach (WorldLawAsset asset in AssetManager.world_laws_library.list)
            {
                if (asset != null && string.Equals(asset.group_id, DiplomacyGroupId, StringComparison.Ordinal))
                {
                    SetWorldLaw(asset.id, enabled: false);
                }
            }

            SetWorldLaw(EvolutionEventsLawId, enabled: false);
            _diplomacyAutoOpenedForWorld = false;
            SetWorldLaw(GaiaLawId, enabled: true);
            SetWorldLaw(HundredPopulationLawId, enabled: true);
            World.world.era_manager?.togglePlay(false);
            AutoPanLogService.Info("新局世界法则已应用：盖亚契约与百人开启，外交组和演化事件关闭，纪元更迭暂停。");
        }

        private static void EnsureDiplomacyLawsForYear(int currentYear)
        {
            if (_diplomacyAutoOpenedForWorld || AutoPanConfigHooks.AutoOpenDiplomacyLaw <= 0 || currentYear < AutoPanConfigHooks.PlayerDecisionStartYear)
            {
                return;
            }

            if (World.world?.world_laws == null || AssetManager.world_laws_library == null)
            {
                return;
            }

            World.world.world_laws.init();
            // 只开启原版外交总开关，避免连带打开魔法仪式、叛乱和边境偷取等同组法则。
            SetWorldLaw(DiplomacyLawId, enabled: true);
            _diplomacyAutoOpenedForWorld = true;
            AutoPanLogService.Info($"第 {currentYear} 年达到可宣战年份，已按前端配置自动开启外交法则。");
        }

        private static void SetWorldLaw(string lawId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(lawId) || World.world?.world_laws?.dict == null || AssetManager.world_laws_library == null)
            {
                return;
            }

            WorldLawAsset asset = AssetManager.world_laws_library.get(lawId);
            if (asset == null)
            {
                return;
            }

            if (!World.world.world_laws.dict.TryGetValue(lawId, out PlayerOptionData option) || option == null)
            {
                option = World.world.world_laws.add(new PlayerOptionData(lawId)
                {
                    boolVal = asset.default_state,
                    on_switch = asset.on_state_change
                });
            }

            bool oldValue = option.boolVal;
            if (!asset.can_turn_off && !enabled)
            {
                enabled = true;
            }

            option.boolVal = enabled;
            if (enabled && !oldValue)
            {
                asset.on_state_enabled?.Invoke(option);
            }

            World.world.world_laws.updateCaches();
            option.on_switch?.Invoke(option);
        }
    }
}
