using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XianniAutoPan.Commands;
using XianniAutoPan.Model;
using XianniAutoPan.Services;

namespace XianniAutoPan.AI
{
    /// <summary>
    /// 自动盘 AI 调度器。
    /// </summary>
    internal static class AutoPanAiService
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<long> RunningKingdoms = new HashSet<long>();
        private static readonly Dictionary<long, int> FailureCounts = new Dictionary<long, int>();
        private static readonly ConcurrentQueue<AutoPanAiDecisionResult> CompletedResults = new ConcurrentQueue<AutoPanAiDecisionResult>();
        private static readonly HttpClient Client = new HttpClient();
        private static int _scheduleOffset;

        /// <summary>
        /// 在年度变更时为 AI 国家安排决策。
        /// </summary>
        public static void ScheduleForYear(int year)
        {
            if (!AutoPanConfigHooks.EnableLlmAi || year < AutoPanConfigHooks.AiDecisionStartYear || World.world?.kingdoms == null)
            {
                return;
            }

            int intervalYears = GetDecisionIntervalYears();
            if ((year - AutoPanConfigHooks.AiDecisionStartYear) % intervalYears != 0)
            {
                return;
            }

            List<Kingdom> candidates = World.world.kingdoms
                .Where(item => item != null && item.isAlive() && item.isCiv() && !AutoPanStateRepository.IsPlayerOwnedKingdom(item))
                .OrderBy(item => item.getID())
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            AutoPanKingdomService.RefreshRuntimeStatsCacheForCurrentYear(includeSnapshots: true);
            int scheduledCount = 0;
            int maxScheduleCount = GetMaxKingdomsPerSchedule();
            int startIndex = Math.Abs(_scheduleOffset) % candidates.Count;
            for (int offset = 0; offset < candidates.Count && scheduledCount < maxScheduleCount; offset++)
            {
                Kingdom kingdom = candidates[(startIndex + offset) % candidates.Count];
                lock (Sync)
                {
                    if (RunningKingdoms.Contains(kingdom.getID()))
                    {
                        continue;
                    }
                    RunningKingdoms.Add(kingdom.getID());
                }

                AutoPanAiRequestContext context = AutoPanKingdomService.BuildAiContext(kingdom);
                Task.Run(() => ThinkAsync(context));
                scheduledCount++;
            }

            _scheduleOffset = (startIndex + Math.Max(1, scheduledCount)) % candidates.Count;
        }

        /// <summary>
        /// 将后台完成的 AI 结果应用到当前世界。
        /// </summary>
        public static void FlushCompletedResults()
        {
            while (CompletedResults.TryDequeue(out AutoPanAiDecisionResult result))
            {
                Kingdom kingdom = World.world?.kingdoms?.get(result.KingdomId);
                if (kingdom == null || !kingdom.isAlive() || AutoPanStateRepository.IsPlayerOwnedKingdom(kingdom))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(result.Error) && result.Commands.Count == 0)
                {
                    AutoPanLogService.Error($"AI 国家 {kingdom.name} 决策失败：{result.Error}");
                    continue;
                }

                int currentYear = Date.getCurrentYear();
                if (result.DecisionYear > 0 && (currentYear < result.DecisionYear || currentYear - result.DecisionYear > GetMaxDecisionLagYears()))
                {
                    AutoPanLogService.Info($"AI 国家 {kingdom.name} 丢弃过期决策：决策年份 {result.DecisionYear}，当前年份 {currentYear}。");
                    continue;
                }

                if (result.UsedFallback)
                {
                    AutoPanLogService.Info($"AI 国家 {kingdom.name} 使用规则兜底：{string.Join(" | ", result.Commands)}");
                }
                else if (!string.IsNullOrWhiteSpace(result.AnalysisText))
                {
                    AutoPanLogService.Info($"AI 国家 {kingdom.name} 国情分析：{result.AnalysisText}");
                }

                List<string> executionTexts = new List<string>();
                foreach (string command in result.Commands.Take(GetMaxActionsPerDecision()))
                {
                    if (AutoPanCommandExecutor.TryExecuteAiCommand(kingdom, command, out string executeText))
                    {
                        AutoPanLogService.Info($"AI 国家 {kingdom.name} 执行：{command} -> {executeText}");
                        executionTexts.Add(command + " 成功");
                    }
                    else
                    {
                        AutoPanLogService.Error($"AI 国家 {kingdom.name} 执行失败：{command} -> {executeText}");
                    }
                }

                NotifyAiDecisionToQq(kingdom, result, executionTexts);
            }
        }

        /// <summary>
        /// 为 QQ 普通聊天构建一条基于国情的 AI 回复。
        /// </summary>
        public static bool TryBuildPlayerChatReply(Kingdom kingdom, string playerName, string playerText, out string reply)
        {
            reply = string.Empty;
            if (!AutoPanConfigHooks.EnableLlmAi || AutoPanConfigHooks.AiQqChatEnabled <= 0 || kingdom == null || !kingdom.isAlive() || string.IsNullOrWhiteSpace(playerText))
            {
                return false;
            }

            AutoPanAiRequestContext context = AutoPanKingdomService.BuildAiContext(kingdom, forceRefresh: true);
            List<string> suggestions = BuildPlayerChatSuggestions(context);
            string suggestionText = suggestions.Count == 0 ? "先观察战况攒金币" : suggestions[0];
            reply = "Ai:" + TrimForNotice($"{context.KingdomName}国库{context.Treasury}，{suggestionText}", 50);
            return true;
        }

        private static async Task ThinkAsync(AutoPanAiRequestContext context)
        {
            try
            {
                AutoPanAiDecisionResult decision = await RequestDecisionFromLlm(context);
                lock (Sync)
                {
                    FailureCounts[context.KingdomId] = 0;
                }
                decision.KingdomId = context.KingdomId;
                CompletedResults.Enqueue(decision);
            }
            catch (Exception ex)
            {
                int failureCount;
                lock (Sync)
                {
                    FailureCounts.TryGetValue(context.KingdomId, out failureCount);
                    failureCount++;
                    FailureCounts[context.KingdomId] = failureCount;
                }

                if (failureCount >= 3)
                {
                CompletedResults.Enqueue(new AutoPanAiDecisionResult
                {
                    KingdomId = context.KingdomId,
                    DecisionYear = context.CurrentYear,
                    Commands = BuildFallbackCommands(context),
                    UsedFallback = true,
                        AnalysisText = "LLM 连续失败，改用规则兜底。",
                        ChatText = "本轮 AI 接口异常，已按国库与成长优先级执行兜底运营。",
                        Error = ex.Message
                    });
                }
                else
                {
                    CompletedResults.Enqueue(new AutoPanAiDecisionResult
                    {
                        KingdomId = context.KingdomId,
                        DecisionYear = context.CurrentYear,
                        Error = ex.Message
                    });
                }
            }
            finally
            {
                lock (Sync)
                {
                    RunningKingdoms.Remove(context.KingdomId);
                }
            }
        }

        private static async Task<AutoPanAiDecisionResult> RequestDecisionFromLlm(AutoPanAiRequestContext context)
        {
            if (string.IsNullOrWhiteSpace(AutoPanConfigHooks.LlmApiUrl))
            {
                throw new InvalidOperationException("未配置 autopan_llm_api_url。");
            }
            if (string.IsNullOrWhiteSpace(AutoPanConfigHooks.LlmModel))
            {
                throw new InvalidOperationException("未配置 autopan_llm_model。");
            }

            JObject payload = new JObject
            {
                ["model"] = AutoPanConfigHooks.LlmModel,
                ["temperature"] = 0.2,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = BuildSystemPrompt()
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = JsonConvert.SerializeObject(context, Formatting.Indented)
                    }
                }
            };

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, NormalizeEndpoint(AutoPanConfigHooks.LlmApiUrl));
            request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(AutoPanConfigHooks.LlmApiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + AutoPanConfigHooks.LlmApiKey);
            }

            using HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false);
            string jsonText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"LLM 接口返回 {(int)response.StatusCode}：{jsonText}");
            }

            JObject root = JObject.Parse(jsonText);
            string content = root["choices"]?[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("LLM 没有返回 message.content。");
            }

            string normalized = StripMarkdownFence(content.Trim());
            JObject commandRoot = JObject.Parse(normalized);
            JArray actions = commandRoot["actions"] as JArray;
            if (actions == null)
            {
                throw new InvalidOperationException("LLM 返回 JSON 缺少 actions 数组。");
            }

            List<string> commands = actions.Values<string>()
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct()
                .Take(GetMaxActionsPerDecision())
                .ToList();

            return new AutoPanAiDecisionResult
            {
                KingdomId = context.KingdomId,
                DecisionYear = context.CurrentYear,
                Commands = commands,
                AnalysisText = TrimForNotice(commandRoot["analysis"]?.ToString(), 220),
                ChatText = TrimForNotice(commandRoot["chat"]?.ToString(), 180)
            };
        }

        private static List<string> BuildFallbackCommands(AutoPanAiRequestContext context)
        {
            List<string> commands = new List<string>();
            int maxActions = GetMaxActionsPerDecision();
            int remainingTreasury = context.Treasury;
            if (context.EnemyKingdomNames.Count > 0 && remainingTreasury >= AutoPanConfigHooks.MobilizeCost)
            {
                commands.Add("动员");
                remainingTreasury -= AutoPanConfigHooks.MobilizeCost;
            }

            int upgradeCost = AutoPanConfigHooks.NationUpgradeCostPerLevel * Math.Max(1, context.NationLevel);
            if (commands.Count < maxActions && remainingTreasury >= upgradeCost)
            {
                commands.Add("升级国运");
                remainingTreasury -= upgradeCost;
            }

            int xiuzhenguoUpgradeCost = AutoPanConfigHooks.XiuzhenguoUpgradeCostPerLevel * Math.Max(1, context.XiuzhenguoLevel + 1);
            if (commands.Count < maxActions && context.XiuzhenguoLevel < AutoPanKingdomService.MaxXiuzhenguoLevel && remainingTreasury >= xiuzhenguoUpgradeCost)
            {
                commands.Add("升级修真国");
                remainingTreasury -= xiuzhenguoUpgradeCost;
            }

            if (commands.Count < maxActions && context.GatherSpiritRemainYears <= 0 && remainingTreasury >= AutoPanConfigHooks.GatherSpiritCost)
            {
                commands.Add("国策 聚灵");
                remainingTreasury -= AutoPanConfigHooks.GatherSpiritCost;
            }

            remainingTreasury = AddActorFallbackCommand(commands, maxActions, context.CultivatorChoices, AutoPanConfigHooks.CultivatorRetreatCost, remainingTreasury, "修士", "闭关");
            remainingTreasury = AddActorFallbackCommand(commands, maxActions, context.AncientChoices, AutoPanConfigHooks.AncientTrainCost, remainingTreasury, "古神", "炼体");
            AddActorFallbackCommand(commands, maxActions, context.BeastChoices, AutoPanConfigHooks.BeastTrainCost, remainingTreasury, "妖兽", "养成");
            return commands;
        }

        private static int AddActorFallbackCommand(List<string> commands, int maxActions, List<string> choices, int cost, int treasury, string actorType, string actionText)
        {
            if (commands.Count >= maxActions || choices == null || choices.Count == 0 || treasury < cost)
            {
                return treasury;
            }

            string actorId = ExtractActorId(choices[0]);
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                commands.Add($"{actorType} {actorId} {actionText}");
                return treasury - cost;
            }
            return treasury;
        }

        private static List<string> BuildPlayerChatSuggestions(AutoPanAiRequestContext context)
        {
            List<string> suggestions = new List<string>();
            int upgradeCost = AutoPanConfigHooks.NationUpgradeCostPerLevel * Math.Max(1, context.NationLevel);
            if (context.Treasury >= upgradeCost)
            {
                suggestions.Add($"可执行“升级国运”，预计消耗 {upgradeCost} 金币");
            }

            int xiuzhenguoCost = AutoPanConfigHooks.XiuzhenguoUpgradeCostPerLevel * Math.Max(1, context.XiuzhenguoLevel + 1);
            if (suggestions.Count < 3 && context.XiuzhenguoLevel < AutoPanKingdomService.MaxXiuzhenguoLevel && context.Treasury >= xiuzhenguoCost)
            {
                suggestions.Add($"可执行“升级修真国”，预计消耗 {xiuzhenguoCost} 金币");
            }

            if (suggestions.Count < 3 && context.GatherSpiritRemainYears <= 0 && context.Treasury >= AutoPanConfigHooks.GatherSpiritCost)
            {
                suggestions.Add($"可执行“国策 聚灵”，消耗 {AutoPanConfigHooks.GatherSpiritCost} 金币");
            }

            if (suggestions.Count < 3 && context.EnemyKingdomNames.Count > 0 && context.Treasury >= AutoPanConfigHooks.MobilizeCost)
            {
                suggestions.Add($"战争中可执行“动员”，消耗 {AutoPanConfigHooks.MobilizeCost} 金币");
            }

            if (suggestions.Count < 3 && context.EnemyKingdomNames.Count > 0 && context.Treasury >= AutoPanConfigHooks.SeekPeaceCost * 2)
            {
                suggestions.Add($"若想停战，可对 {context.EnemyKingdomNames[0]} 求和，需准备 {AutoPanConfigHooks.SeekPeaceCost * 2} 金币");
            }

            if (suggestions.Count < 3 && context.CanDeclareWar && context.CandidateKingdomNames.Count > 0 && context.Treasury >= AutoPanConfigHooks.DeclareWarCost)
            {
                suggestions.Add($"可宣战目标示例：{context.CandidateKingdomNames[0]}，宣战消耗 {AutoPanConfigHooks.DeclareWarCost} 金币");
            }

            if (suggestions.Count == 0 && !context.CanDeclareWar)
            {
                suggestions.Add($"第 {context.DeclareWarStartYear} 年前不能宣战，建议先攒国库和培养强者");
            }
            return suggestions;
        }

        private static string BuildSystemPrompt()
        {
            int maxActions = GetMaxActionsPerDecision();
            int nationUpCost = AutoPanConfigHooks.NationUpgradeCostPerLevel;
            int xiuzhenUpCost = AutoPanConfigHooks.XiuzhenguoUpgradeCostPerLevel;
            int policyCost = AutoPanConfigHooks.OccupationPolicyChangeCost;
            int gatherCost = AutoPanConfigHooks.GatherSpiritCost;
            int militiaCost = AutoPanConfigHooks.NationalMilitiaCost;
            int mobilizeCost = AutoPanConfigHooks.MobilizeCost;
            int warCost = AutoPanConfigHooks.DeclareWarCost;
            int peaceCost = AutoPanConfigHooks.SeekPeaceCost;
            int allianceCost = AutoPanConfigHooks.AllianceRequestCost;
            int leaveAllianceCost = AutoPanConfigHooks.LeaveAllianceCost;
            int duelCost = AutoPanConfigHooks.DuelRequestCost;
            int popCost = AutoPanConfigHooks.AddPopulationCostPerUnit;
            int sabotageMin = AutoPanConfigHooks.AuraSabotageMinCost;
            int assassinBase = AutoPanConfigHooks.AssassinateBaseCost;
            int curseBase = AutoPanConfigHooks.CurseBaseCost;
            int cursePer = AutoPanConfigHooks.CurseCostPerTarget;
            int heavenPunish = AutoPanConfigHooks.HeavenPunishCost;
            int heavenBless = AutoPanConfigHooks.HeavenBlessCost;
            int disturbCost = AutoPanConfigHooks.DisturbKingdomCost;
            int disturbRate = AutoPanConfigHooks.DisturbSuccessRate;
            int lowerCost = AutoPanConfigHooks.LowerNationCostPerLevel;
            int retreatCost = AutoPanConfigHooks.CultivatorRetreatCost;
            int ancientCost = AutoPanConfigHooks.AncientTrainCost;
            int beastCost = AutoPanConfigHooks.BeastTrainCost;

            return $"你是 WorldBox 国家自动盘 AI，负责为一个国家做战略决策。分析 user JSON 后选择 0~{maxActions} 个动作。" +
                   "【决策优先级从高到低】1.战争中军队未推进时先动员 2.升级国运/修真国（国力根基）3.宣战/求和/结盟/约斗（外交互动）4.聚灵/全民皆兵/增加人数（内政发展）5.天运/削灵/斩首/诅咒/扰动/降低国运（对敌干扰）6.修士闭关/升境、古神炼体/升星、妖兽养成/升阶（个体培养，国库充裕时才考虑）。" +
                   "你应该积极与其他国家互动（结盟、约斗、宣战、天运等），不要只做内政。AllKingdoms 中 IsPlayerOwned=true 的是玩家国家，也可以互动。" +
                   "约束：国库不足不选；目标不存在不选；同盟不宣战；CanDeclareWar=false 不宣战；宣战目标必须来自 CandidateKingdomNames；求和目标必须来自 EnemyKingdomNames。" +
                   "动作格式：升级国运、升级修真国、政策 开放占领、政策 坚守城池、国策 聚灵、全民皆兵、动员、宣战 国家名 [kingdomId]、求和 国家名 [kingdomId]、结盟 国家名 [kingdomId]、退盟、约斗 国家名 [kingdomId]、增加人数 数字(1~10)、血脉创立、国家祝福 5、国家祝福 全员、削灵 国家名 [kingdomId] 数量、斩首 国家名 [kingdomId]、诅咒 国家名 [kingdomId] 人数、天运惩罚 国家名 [kingdomId]、天运赐福 国家名 [kingdomId]、扰动国家 国家名 [kingdomId]、降低国运 国家名 [kingdomId] 级数、修士 单位id 闭关、修士 单位id 升境、古神 单位id 炼体、古神 单位id 升星、妖兽 单位id 养成、妖兽 单位id 升阶。" +
                   $"成本（金币）：升级国运={nationUpCost}×当前等级；升级修真国={xiuzhenUpCost}×下一级；政策变更={policyCost}；聚灵={gatherCost}；全民皆兵={militiaCost}；动员={mobilizeCost}；宣战={warCost}；求和={peaceCost}×2；结盟={allianceCost}；退盟={leaveAllianceCost}；约斗={duelCost}；增加人数={popCost}×人数；削灵≥{sabotageMin}；斩首={assassinBase}+层级加价；诅咒={curseBase}+{cursePer}×人数；天运惩罚={heavenPunish}；天运赐福={heavenBless}；扰动={disturbCost}(成功率{disturbRate}%)；降低国运={lowerCost}×级数；修士闭关={retreatCost}；古神炼体={ancientCost}；妖兽养成={beastCost}；血脉创立和国家祝福按目标计价。" +
                   "返回严格 JSON：{\"analysis\":\"一句话国情分析\",\"chat\":\"一句话聊天，可挑衅可友好，不要编造\",\"actions\":[\"动作1\"]}。没有合适动作时 actions 返回空数组。";
        }

        private static int GetDecisionIntervalYears()
        {
            return Math.Max(1, AutoPanConfigHooks.AiDecisionIntervalYears);
        }

        private static int GetMaxActionsPerDecision()
        {
            return Math.Max(1, Math.Min(5, AutoPanConfigHooks.AiDecisionIntensity));
        }

        private static int GetMaxKingdomsPerSchedule()
        {
            return Math.Max(1, Math.Min(10, AutoPanConfigHooks.AiDecisionIntensity * 2));
        }

        private static int GetMaxDecisionLagYears()
        {
            return Math.Max(2, GetDecisionIntervalYears() * 2);
        }

        private static void NotifyAiDecisionToQq(Kingdom kingdom, AutoPanAiDecisionResult result, List<string> executionTexts)
        {
            if (!AutoPanConfigHooks.EnableLlmAi || AutoPanConfigHooks.AiQqChatEnabled <= 0 || kingdom == null)
            {
                return;
            }

            bool hasExecution = executionTexts != null && executionTexts.Count > 0;
            if (!hasExecution)
            {
                return;
            }

            string commandList = string.Join("、", executionTexts);
            string chat = !string.IsNullOrWhiteSpace(result.ChatText) ? $"「{result.ChatText.Trim()}」" : string.Empty;
            string notice = $"{kingdom.name} 决策{executionTexts.Count}项：{commandList}{chat}";
            AutoPanNotificationService.BroadcastToKnownGroups("Ai:" + TrimForNotice(notice, 200));
        }

        private static string ExtractActorId(string choiceText)
        {
            if (string.IsNullOrWhiteSpace(choiceText))
            {
                return string.Empty;
            }

            const string marker = "[id=";
            int start = choiceText.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            start += marker.Length;
            int end = choiceText.IndexOf(']', start);
            return end > start ? choiceText.Substring(start, end - start).Trim() : string.Empty;
        }

        private static string TrimForNotice(string text, int maxLength)
        {
            string normalized = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }
            return normalized.Substring(0, Math.Max(0, maxLength - 1)) + "…";
        }

        private static string NormalizeEndpoint(string rawUrl)
        {
            string url = rawUrl.Trim().TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
            if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return url + "/chat/completions";
            }
            return url + "/v1/chat/completions";
        }

        private static string StripMarkdownFence(string content)
        {
            if (!content.StartsWith("```", StringComparison.Ordinal))
            {
                return content;
            }

            int firstNewLine = content.IndexOf('\n');
            if (firstNewLine < 0)
            {
                return content.Trim('`');
            }

            string body = content.Substring(firstNewLine + 1);
            int closing = body.LastIndexOf("```", StringComparison.Ordinal);
            if (closing >= 0)
            {
                body = body.Substring(0, closing);
            }
            return body.Trim();
        }
    }
}
