using System;
using System.Collections.Generic;
using System.Linq;
using xn.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 提供国家互动型指令，例如血脉创立、削灵、斩首、诅咒、祝福与修士压境。
    /// </summary>
    internal static class AutoPanInteractionService
    {
        private sealed class RankedActor
        {
            /// <summary>
            /// 单位实例。
            /// </summary>
            public Actor Actor { get; set; }

            /// <summary>
            /// 显示阶段值。
            /// </summary>
            public int StageValue { get; set; }

            /// <summary>
            /// 强度分数。
            /// </summary>
            public long Score { get; set; }

            /// <summary>
            /// 强度类型文本。
            /// </summary>
            public string CategoryText { get; set; }
        }

        /// <summary>
        /// 给本国指定或最强可用单位创立血脉。
        /// </summary>
        public static bool TryCreateFounderBloodline(Kingdom kingdom, long actorId, out string message)
        {
            message = string.Empty;
            RankedActor ranked;
            if (actorId > 0)
            {
                ranked = GetRankedActors(kingdom)
                    .FirstOrDefault(item => item.Actor != null && item.Actor.getID() == actorId);
                if (ranked == null)
                {
                    message = $"当前国家不存在 id={actorId} 的上榜强者。";
                    return false;
                }
                if (!XianniAutoPanApi.CanCreateFounderBloodline(ranked.Actor))
                {
                    message = $"{ranked.Actor.getName()} 当前无法创立血脉，请确认该单位尚未拥有血脉且属于修士/古神/妖兽。";
                    return false;
                }
            }
            else
            {
                ranked = GetRankedActors(kingdom)
                    .FirstOrDefault(item => XianniAutoPanApi.CanCreateFounderBloodline(item.Actor));
            }

            if (ranked == null)
            {
                message = "当前国家没有可创立血脉的强者，要求目标为修士、古神或妖兽且尚未拥有血脉。";
                return false;
            }

            int cost = AutoPanCostService.GetBloodlineCreateCost(ranked.Actor);
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            if (!XianniAutoPanApi.TryCreateFounderBloodline(ranked.Actor, out string bloodlineName))
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                message = "血脉创立失败，可能该系血脉已经被世界中的其他家族占用。";
                return false;
            }

            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            message = $"{ranked.Actor.getName()} 已创立 {bloodlineName}，消耗 {cost} 金币。";
            return true;
        }

        /// <summary>
        /// 获取当前国家的最强者。
        /// </summary>
        public static bool TryGetStrongestActor(Kingdom kingdom, out Actor actor, out string summary)
        {
            actor = null;
            summary = string.Empty;
            RankedActor ranked = GetRankedActors(kingdom).FirstOrDefault();
            if (ranked == null)
            {
                summary = $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 当前没有可用的最强者。";
                return false;
            }

            actor = ranked.Actor;
            summary = $"{ranked.Actor.getName()} [id={ranked.Actor.getID()}] / {ranked.CategoryText} / 战力 {ranked.Score}";
            return true;
        }

        /// <summary>
        /// 构建全图天榜前五。
        /// </summary>
        public static string BuildTopPowerBoardText()
        {
            List<(Actor Actor, Kingdom Kingdom, long Score)> topActors = new List<(Actor, Kingdom, long)>();
            if (World.world?.units == null)
            {
                return "当前世界未加载，无法查看天榜。";
            }

            foreach (Actor actor in World.world.units)
            {
                if (actor == null || !actor.isAlive() || actor.kingdom == null || !actor.kingdom.isAlive() || !actor.kingdom.isCiv())
                {
                    continue;
                }

                long score = XianniAutoPanApi.GetPowerScore(actor);
                if (score <= 0)
                {
                    continue;
                }

                topActors.Add((actor, actor.kingdom, score));
            }

            List<(Actor Actor, Kingdom Kingdom, long Score)> rankedList = topActors
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Actor.getID())
                .Take(5)
                .ToList();
            if (rankedList.Count == 0)
            {
                return "当前世界没有可上榜的强者。";
            }

            List<string> lines = new List<string> { "天榜前 5：" };
            for (int index = 0; index < rankedList.Count; index++)
            {
                (Actor actor, Kingdom kingdom, long score) = rankedList[index];
                lines.Add($"{index + 1}. {actor.getName()} [id={actor.getID()}]，所属 {AutoPanKingdomService.FormatKingdomLabel(kingdom)}，战力 {score}");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 对敌国削减灵气。
        /// </summary>
        public static bool TryReduceEnemyAura(Kingdom sourceKingdom, string rawTargetKingdomName, int amount, out string message)
        {
            message = string.Empty;
            if (!TryResolveEnemyKingdom(sourceKingdom, rawTargetKingdomName, out Kingdom targetKingdom, out string targetError))
            {
                message = targetError;
                return false;
            }

            if (amount <= 0)
            {
                message = "削灵数值必须大于 0。";
                return false;
            }

            int cost = AutoPanCostService.GetAuraSabotageCost(amount);
            if (!AutoPanKingdomService.TrySpendTreasury(sourceKingdom, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            if (!XianniAutoPanApi.TryReduceKingdomAura(targetKingdom, amount, out int actualReduced))
            {
                AutoPanKingdomService.AddTreasury(sourceKingdom, cost);
                message = $"{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 当前没有可削减的灵气。";
                return false;
            }

            AutoPanKingdomService.ClearSnapshotCache(sourceKingdom.getID());
            AutoPanKingdomService.ClearSnapshotCache(targetKingdom.getID());
            message = $"{AutoPanKingdomService.FormatKingdomLabel(sourceKingdom)} 已对 {AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 削灵 {actualReduced}，消耗 {cost} 金币。";
            return true;
        }

        /// <summary>
        /// 对敌国执行斩首，击杀其最强单位。
        /// </summary>
        public static bool TryAssassinateStrongest(Kingdom sourceKingdom, string rawTargetKingdomName, out string message)
        {
            message = string.Empty;
            if (!TryResolveEnemyKingdom(sourceKingdom, rawTargetKingdomName, out Kingdom targetKingdom, out string targetError))
            {
                message = targetError;
                return false;
            }

            RankedActor ranked = GetRankedActors(targetKingdom).FirstOrDefault();
            if (ranked == null)
            {
                message = $"{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 当前没有可斩首的强者。";
                return false;
            }

            int cost = AutoPanCostService.GetAssassinateCost(ranked.Actor);
            if (!AutoPanKingdomService.TrySpendTreasury(sourceKingdom, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            if (!ranked.Actor.isAlive())
            {
                AutoPanKingdomService.AddTreasury(sourceKingdom, cost);
                message = "目标最强者状态已变化，请重新尝试。";
                return false;
            }

            string actorName = ranked.Actor.getName();
            ranked.Actor.dieAndDestroy(AttackType.Other);
            AutoPanKingdomService.ClearSnapshotCache(sourceKingdom.getID());
            AutoPanKingdomService.ClearSnapshotCache(targetKingdom.getID());
            message = $"{AutoPanKingdomService.FormatKingdomLabel(sourceKingdom)} 已斩首 {AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 的最强者 {actorName}，消耗 {cost} 金币。";
            return true;
        }

        /// <summary>
        /// 对敌国若干强者施加诅咒。
        /// </summary>
        public static bool TryCurseEnemyUnits(Kingdom sourceKingdom, string rawTargetKingdomName, int count, out string message)
        {
            message = string.Empty;
            if (!TryResolveEnemyKingdom(sourceKingdom, rawTargetKingdomName, out Kingdom targetKingdom, out string targetError))
            {
                message = targetError;
                return false;
            }

            int requestedCount = Math.Max(1, count);
            List<RankedActor> rankedTargets = GetRankedActors(targetKingdom).Take(requestedCount).ToList();
            if (rankedTargets.Count == 0)
            {
                message = $"{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 当前没有可诅咒的强者。";
                return false;
            }

            int expectedCost = AutoPanCostService.GetCurseCost(rankedTargets.Count);
            if (!AutoPanKingdomService.TrySpendTreasury(sourceKingdom, expectedCost, out string spendError))
            {
                message = spendError;
                return false;
            }

            List<string> cursedNames = new List<string>();
            foreach (RankedActor ranked in rankedTargets)
            {
                if (XianniAutoPanApi.TryApplyCurse(ranked.Actor))
                {
                    cursedNames.Add(ranked.Actor.getName());
                }
            }

            if (cursedNames.Count == 0)
            {
                AutoPanKingdomService.AddTreasury(sourceKingdom, expectedCost);
                message = "本次没有成功施加任何诅咒。";
                return false;
            }

            int actualCost = AutoPanCostService.GetCurseCost(cursedNames.Count);
            int refund = expectedCost - actualCost;
            if (refund > 0)
            {
                AutoPanKingdomService.AddTreasury(sourceKingdom, refund);
            }

            message = $"{AutoPanKingdomService.FormatKingdomLabel(sourceKingdom)} 已诅咒 {AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 的 {cursedNames.Count} 人：{BuildNameList(cursedNames)}，消耗 {actualCost} 金币。";
            return true;
        }

        /// <summary>
        /// 给本国若干强者或全员施加祝福。
        /// </summary>
        public static bool TryBlessOwnUnits(Kingdom kingdom, int count, out string message)
        {
            message = string.Empty;
            List<Actor> targets = count <= 0
                ? kingdom.units.Where(item => item != null && item.isAlive()).Distinct().ToList()
                : GetPriorityActors(kingdom, count);
            if (targets.Count == 0)
            {
                message = "当前国家没有可祝福的存活单位。";
                return false;
            }

            int expectedCost = AutoPanCostService.GetBlessCost(targets.Count);
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, expectedCost, out string spendError))
            {
                message = spendError;
                return false;
            }

            int successCount = 0;
            foreach (Actor actor in targets)
            {
                if (XianniAutoPanApi.TryApplyBlessing(actor))
                {
                    successCount++;
                }
            }

            if (successCount == 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, expectedCost);
                message = "本次没有成功施加任何祝福。";
                return false;
            }

            int actualCost = AutoPanCostService.GetBlessCost(successCount);
            int refund = expectedCost - actualCost;
            if (refund > 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, refund);
            }

            message = count <= 0
                ? $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 已为全员施加祝福，共 {successCount} 人，消耗 {actualCost} 金币。"
                : $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 已为 {successCount} 名主力施加祝福，消耗 {actualCost} 金币。";
            return true;
        }

        /// <summary>
        /// 让敌国若干修士下降指定境界。
        /// </summary>
        public static bool TrySuppressEnemyCultivators(Kingdom sourceKingdom, string rawTargetKingdomName, int count, int levels, out string message)
        {
            message = string.Empty;
            if (!TryResolveEnemyKingdom(sourceKingdom, rawTargetKingdomName, out Kingdom targetKingdom, out string targetError))
            {
                message = targetError;
                return false;
            }

            int requestedCount = Math.Max(1, count);
            int requestedLevels = Math.Max(1, levels);
            List<Actor> cultivators = GetTargetCultivators(targetKingdom, requestedCount);
            if (cultivators.Count == 0)
            {
                message = $"{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 当前没有可压境的修士。";
                return false;
            }

            int expectedCost = AutoPanCostService.GetCultivatorSuppressCost(cultivators, requestedLevels);
            if (!AutoPanKingdomService.TrySpendTreasury(sourceKingdom, expectedCost, out string spendError))
            {
                message = spendError;
                return false;
            }

            int actualCost = 0;
            int affectedCount = 0;
            int totalLowered = 0;
            List<string> affectedNames = new List<string>();
            foreach (Actor actor in cultivators)
            {
                int unitCost = AutoPanCostService.GetCultivatorSuppressUnitCost(actor, requestedLevels);
                if (!XianniAutoPanApi.TryLowerCultivatorRealm(actor, requestedLevels, out int actualLowered))
                {
                    continue;
                }

                affectedCount++;
                totalLowered += actualLowered;
                actualCost += unitCost;
                affectedNames.Add($"{actor.getName()}(-{actualLowered})");
            }

            if (affectedCount == 0)
            {
                AutoPanKingdomService.AddTreasury(sourceKingdom, expectedCost);
                message = "修士压境失败，目标修士没有实际下降境界。";
                return false;
            }

            int refund = expectedCost - actualCost;
            if (refund > 0)
            {
                AutoPanKingdomService.AddTreasury(sourceKingdom, refund);
            }

            AutoPanKingdomService.ClearSnapshotCache(sourceKingdom.getID());
            AutoPanKingdomService.ClearSnapshotCache(targetKingdom.getID());
            message = $"{AutoPanKingdomService.FormatKingdomLabel(sourceKingdom)} 已压制 {AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 的 {affectedCount} 名修士，共下降 {totalLowered} 个境界：{BuildNameList(affectedNames)}，消耗 {actualCost} 金币。";
            return true;
        }

        private static bool TryResolveEnemyKingdom(Kingdom sourceKingdom, string rawTargetKingdomName, out Kingdom targetKingdom, out string error)
        {
            targetKingdom = null;
            error = string.Empty;
            if (!AutoPanKingdomService.TryResolveKingdom(rawTargetKingdomName, out targetKingdom, out error))
            {
                return false;
            }

            if (targetKingdom == sourceKingdom)
            {
                error = "不能对自己的国家使用该互动指令。";
                return false;
            }

            return true;
        }

        private static List<Actor> GetPriorityActors(Kingdom kingdom, int count)
        {
            List<Actor> elite = GetRankedActors(kingdom).Select(item => item.Actor).Take(Math.Max(1, count)).ToList();
            if (elite.Count >= count)
            {
                return elite;
            }

            foreach (Actor actor in kingdom.units)
            {
                if (actor == null || !actor.isAlive() || elite.Contains(actor))
                {
                    continue;
                }

                elite.Add(actor);
                if (elite.Count >= count)
                {
                    break;
                }
            }

            return elite;
        }

        private static List<Actor> GetTargetCultivators(Kingdom kingdom, int count)
        {
            XianniKingdomSnapshot snapshot = AutoPanKingdomService.GetSnapshot(kingdom, forceRefresh: true);
            List<Actor> result = new List<Actor>();
            foreach (XianniActorEntry entry in snapshot.Cultivators.Take(Math.Max(1, count)))
            {
                Actor actor = World.world?.units?.get(entry.ActorId);
                if (actor != null && actor.isAlive() && actor.kingdom == kingdom)
                {
                    result.Add(actor);
                }
            }
            return result;
        }

        private static List<RankedActor> GetRankedActors(Kingdom kingdom)
        {
            XianniKingdomSnapshot snapshot = AutoPanKingdomService.GetSnapshot(kingdom, forceRefresh: true);
            Dictionary<long, RankedActor> actors = new Dictionary<long, RankedActor>();
            CollectRankedActors(kingdom, snapshot.Cultivators, actors, "修士", 3);
            CollectRankedActors(kingdom, snapshot.Ancients, actors, "古神", 2);
            CollectRankedActors(kingdom, snapshot.Beasts, actors, "妖兽", 1);
            return actors.Values
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Actor.getID())
                .ToList();
        }

        private static void CollectRankedActors(Kingdom kingdom, IEnumerable<XianniActorEntry> entries, Dictionary<long, RankedActor> target, string categoryText, int categoryWeight)
        {
            if (entries == null)
            {
                return;
            }

            foreach (XianniActorEntry entry in entries)
            {
                Actor actor = World.world?.units?.get(entry.ActorId);
                if (actor == null || !actor.isAlive() || actor.kingdom != kingdom)
                {
                    continue;
                }

                long score = categoryWeight * 1_000_000_000_000L + entry.StageValue * 1_000_000_000L + Math.Max(0L, entry.PowerValue);
                target[actor.getID()] = new RankedActor
                {
                    Actor = actor,
                    StageValue = entry.StageValue,
                    Score = score,
                    CategoryText = categoryText
                };
            }
        }

        /// <summary>
        /// 天运惩罚：随机诅咒目标国家若干单位。
        /// </summary>
        public static bool TryHeavenPunish(string rawTargetKingdomName, out string message)
        {
            message = string.Empty;
            if (!AutoPanKingdomService.TryResolveKingdom(rawTargetKingdomName, out Kingdom targetKingdom, out string error))
            {
                message = error;
                return false;
            }

            int cost = AutoPanConfigHooks.HeavenPunishCost;
            int maxTargets = AutoPanConfigHooks.HeavenPunishMaxTargets;
            int count = maxTargets <= 0 ? 0 : Randy.randomInt(0, maxTargets + 1);

            List<Actor> targets = targetKingdom.units
                .Where(a => a != null && a.isAlive())
                .OrderBy(_ => Randy.randomInt(0, 10000))
                .Take(count)
                .ToList();

            int successCount = 0;
            foreach (Actor actor in targets)
            {
                if (XianniAutoPanApi.TryApplyCurse(actor))
                {
                    successCount++;
                }
            }

            message = successCount == 0
                ? $"天运降临，{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 此次未受惩罚，消耗 {cost} 金币。"
                : $"天运降临，{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 有 {successCount} 人受到惩罚，消耗 {cost} 金币。";
            return true;
        }

        /// <summary>
        /// 天运赐福：随机祝福目标国家若干单位。
        /// </summary>
        public static bool TryHeavenBless(string rawTargetKingdomName, out string message)
        {
            message = string.Empty;
            if (!AutoPanKingdomService.TryResolveKingdom(rawTargetKingdomName, out Kingdom targetKingdom, out string error))
            {
                message = error;
                return false;
            }

            int cost = AutoPanConfigHooks.HeavenBlessCost;
            int maxTargets = AutoPanConfigHooks.HeavenBlessMaxTargets;
            int count = maxTargets <= 0 ? 0 : Randy.randomInt(0, maxTargets + 1);

            List<Actor> targets = targetKingdom.units
                .Where(a => a != null && a.isAlive())
                .OrderBy(_ => Randy.randomInt(0, 10000))
                .Take(count)
                .ToList();

            int successCount = 0;
            foreach (Actor actor in targets)
            {
                if (XianniAutoPanApi.TryApplyBlessing(actor))
                {
                    successCount++;
                }
            }

            message = successCount == 0
                ? $"天运降临，{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 此次未获赐福，消耗 {cost} 金币。"
                : $"天运降临，{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 有 {successCount} 人获得赐福，消耗 {cost} 金币。";
            return true;
        }

        /// <summary>
        /// 扰动国家：按概率夺取目标国家一座非首都城市。
        /// </summary>
        public static bool TryDisturbKingdom(Kingdom sourceKingdom, string rawTargetKingdomName, out string message)
        {
            message = string.Empty;
            if (!AutoPanKingdomService.TryResolveKingdom(rawTargetKingdomName, out Kingdom targetKingdom, out string error))
            {
                message = error;
                return false;
            }

            if (targetKingdom.getID() == sourceKingdom.getID())
            {
                message = "不能扰动自己的国家。";
                return false;
            }

            int cost = AutoPanConfigHooks.DisturbKingdomCost;
            if (!AutoPanKingdomService.TrySpendTreasury(sourceKingdom, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            if (!Randy.randomChance(AutoPanConfigHooks.DisturbSuccessRate / 100f))
            {
                message = $"扰动 {AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 失败，消耗 {cost} 金币。";
                return false;
            }

            List<City> nonCapitalCities = targetKingdom.cities
                .Where(c => c != null && c.isAlive() && !c.isCapitalCity())
                .ToList();

            if (nonCapitalCities.Count == 0)
            {
                message = $"{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 没有可夺取的非首都城市，消耗 {cost} 金币。";
                return false;
            }

            City city = nonCapitalCities[Randy.randomInt(0, nonCapitalCities.Count)];
            string cityName = city.name;
            city.joinAnotherKingdom(sourceKingdom);
            message = $"扰动成功！{AutoPanKingdomService.FormatKingdomLabel(targetKingdom)} 的城市 {cityName} 已归入 {AutoPanKingdomService.FormatKingdomLabel(sourceKingdom)}，消耗 {cost} 金币。";
            return true;
        }

        private static string BuildNameList(List<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return "无";
            }

            if (names.Count <= 4)
            {
                return string.Join("，", names);
            }

            return string.Join("，", names.Take(4)) + $" 等 {names.Count} 人";
        }
    }
}
