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

        /// <summary>
        /// 在年度变更时为 AI 国家安排决策。
        /// </summary>
        public static void ScheduleForYear(int year)
        {
            if (!AutoPanConfigHooks.EnableLlmAi || year < AutoPanConfigHooks.AiDecisionStartYear || year % 2 != 0 || World.world?.kingdoms == null)
            {
                return;
            }

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv() || AutoPanStateRepository.IsPlayerOwnedKingdom(kingdom))
                {
                    continue;
                }

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
            }
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

                if (result.UsedFallback)
                {
                    AutoPanLogService.Info($"AI 国家 {kingdom.name} 使用规则兜底：{string.Join(" | ", result.Commands)}");
                }

                foreach (string command in result.Commands.Take(2))
                {
                    if (AutoPanCommandExecutor.TryExecuteAiCommand(kingdom, command, out string executeText))
                    {
                        AutoPanLogService.Info($"AI 国家 {kingdom.name} 执行：{command} -> {executeText}");
                    }
                    else
                    {
                        AutoPanLogService.Error($"AI 国家 {kingdom.name} 执行失败：{command} -> {executeText}");
                    }
                }
            }
        }

        private static async Task ThinkAsync(AutoPanAiRequestContext context)
        {
            try
            {
                List<string> commands = await RequestCommandsFromLlm(context);
                lock (Sync)
                {
                    FailureCounts[context.KingdomId] = 0;
                }
                CompletedResults.Enqueue(new AutoPanAiDecisionResult
                {
                    KingdomId = context.KingdomId,
                    Commands = commands
                });
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
                        Commands = BuildFallbackCommands(context),
                        UsedFallback = true,
                        Error = ex.Message
                    });
                }
                else
                {
                    CompletedResults.Enqueue(new AutoPanAiDecisionResult
                    {
                        KingdomId = context.KingdomId,
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

        private static async Task<List<string>> RequestCommandsFromLlm(AutoPanAiRequestContext context)
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
                .Take(2)
                .ToList();
            return commands;
        }

        private static List<string> BuildFallbackCommands(AutoPanAiRequestContext context)
        {
            List<string> commands = new List<string>();
            int upgradeCost = AutoPanConfigHooks.NationUpgradeCostPerLevel * Math.Max(1, context.NationLevel);
            if (context.Treasury >= upgradeCost)
            {
                commands.Add("升级国运");
                return commands;
            }

            if (context.CultivatorChoices.Count > 0 && context.Treasury >= AutoPanConfigHooks.CultivatorRetreatCost)
            {
                commands.Add("修士 1 闭关");
                return commands;
            }

            if (context.AncientChoices.Count > 0 && context.Treasury >= AutoPanConfigHooks.AncientTrainCost)
            {
                commands.Add("古神 1 炼体");
                return commands;
            }

            if (context.BeastChoices.Count > 0 && context.Treasury >= AutoPanConfigHooks.BeastTrainCost)
            {
                commands.Add("妖兽 1 养成");
            }

            return commands;
        }

        private static string BuildSystemPrompt()
        {
            return "你是 WorldBox 国家自动盘 AI。你只能从以下动作里选 0 到 2 个：升级国运、国策 聚灵、宣战 国家名、求和 国家名、修士 1 闭关、古神 1 炼体、妖兽 1 养成。" +
                   "动作必须严格使用这些文本格式，不要解释，不要输出 Markdown，只返回 JSON：{\"actions\":[\"动作1\",\"动作2\"]}。" +
                   $"所有动作都要考虑国库，升级国运成本={AutoPanConfigHooks.NationUpgradeCostPerLevel}*当前国家等级，聚灵={AutoPanConfigHooks.GatherSpiritCost}，宣战={AutoPanConfigHooks.DeclareWarCost}，求和={AutoPanConfigHooks.SeekPeaceCost}，修士闭关={AutoPanConfigHooks.CultivatorRetreatCost}，古神炼体={AutoPanConfigHooks.AncientTrainCost}，妖兽养成={AutoPanConfigHooks.BeastTrainCost}。" +
                   "如果没有合适动作，返回 {\"actions\":[]}。";
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
