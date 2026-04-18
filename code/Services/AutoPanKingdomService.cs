using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using XianniAutoPan.Model;
using xn.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 自动盘国家、国库与榜单服务。
    /// </summary>
    internal static class AutoPanKingdomService
    {
        private sealed class XiuzhenguoLevelRequirement
        {
            /// <summary>
            /// 修真国等级。
            /// </summary>
            public int Level { get; set; }

            /// <summary>
            /// 主要境界索引。
            /// </summary>
            public int RequiredRealmIndex { get; set; }

            /// <summary>
            /// 主要境界人数。
            /// </summary>
            public int RequiredCount { get; set; }

            /// <summary>
            /// 次要境界索引。
            /// </summary>
            public int SecondaryRealmIndex { get; set; }

            /// <summary>
            /// 次要境界人数。
            /// </summary>
            public int SecondaryCount { get; set; }
        }

        private sealed class SnapshotCacheEntry
        {
            /// <summary>
            /// 快照对应年份。
            /// </summary>
            public int Year { get; set; }

            /// <summary>
            /// xianni 快照内容。
            /// </summary>
            public XianniKingdomSnapshot Snapshot { get; set; }
        }

        private static readonly Dictionary<long, SnapshotCacheEntry> SnapshotCache = new Dictionary<long, SnapshotCacheEntry>();
        private static readonly XiuzhenguoLevelRequirement[] XiuzhenguoRequirements =
        {
            new XiuzhenguoLevelRequirement { Level = 0, RequiredRealmIndex = -1, RequiredCount = 0, SecondaryRealmIndex = -1, SecondaryCount = 0 },
            new XiuzhenguoLevelRequirement { Level = 1, RequiredRealmIndex = 1, RequiredCount = 5, SecondaryRealmIndex = -1, SecondaryCount = 0 },
            new XiuzhenguoLevelRequirement { Level = 2, RequiredRealmIndex = 2, RequiredCount = 3, SecondaryRealmIndex = -1, SecondaryCount = 0 },
            new XiuzhenguoLevelRequirement { Level = 3, RequiredRealmIndex = 3, RequiredCount = 2, SecondaryRealmIndex = -1, SecondaryCount = 0 },
            new XiuzhenguoLevelRequirement { Level = 4, RequiredRealmIndex = 4, RequiredCount = 2, SecondaryRealmIndex = -1, SecondaryCount = 0 },
            new XiuzhenguoLevelRequirement { Level = 5, RequiredRealmIndex = 5, RequiredCount = 1, SecondaryRealmIndex = -1, SecondaryCount = 0 },
            new XiuzhenguoLevelRequirement { Level = 6, RequiredRealmIndex = 6, RequiredCount = 1, SecondaryRealmIndex = 5, SecondaryCount = 5 },
            new XiuzhenguoLevelRequirement { Level = 7, RequiredRealmIndex = 9, RequiredCount = 1, SecondaryRealmIndex = 7, SecondaryCount = 5 },
            new XiuzhenguoLevelRequirement { Level = 8, RequiredRealmIndex = 13, RequiredCount = 1, SecondaryRealmIndex = 10, SecondaryCount = 10 },
            new XiuzhenguoLevelRequirement { Level = 9, RequiredRealmIndex = 14, RequiredCount = 1, SecondaryRealmIndex = 13, SecondaryCount = 5 },
            new XiuzhenguoLevelRequirement { Level = 10, RequiredRealmIndex = 15, RequiredCount = 1, SecondaryRealmIndex = 14, SecondaryCount = 5 }
        };

        /// <summary>
        /// 自动盘支持推进的最高修真国等级。
        /// </summary>
        public static int MaxXiuzhenguoLevel => XiuzhenguoRequirements.Length - 1;

        /// <summary>
        /// 根据加入命令创建玩家国家。
        /// </summary>
        public static bool TryCreatePlayerKingdom(string userId, string playerName, AutoPanCommandType joinType, out Kingdom kingdom, out string message)
        {
            kingdom = null;
            message = string.Empty;
            AutoPanStateRepository.EnsureBindingValidForUser(userId);
            if (AutoPanStateRepository.TryGetLiveBinding(userId, out AutoPanBindingRecord binding, out Kingdom boundKingdom))
            {
                message = $"你已经绑定国家 {binding.KingdomName}，当前国家仍然存活。";
                kingdom = boundKingdom;
                return false;
            }

            if (World.world == null || World.world.city_zone_helper == null)
            {
                message = "当前世界尚未完全加载，暂时不能加入国家。";
                return false;
            }

            string actorAssetId = GetActorAssetId(joinType);
            string raceId = GetRaceId(joinType);
            string raceText = GetRaceText(joinType);
            if (string.IsNullOrEmpty(actorAssetId))
            {
                message = "未知种族，无法创建国家。";
                return false;
            }

            string kingdomName = BuildUniqueKingdomName(playerName);

            CityPlaceFinder finder = World.world.city_zone_helper.city_place_finder;
            finder.setDirty();
            finder.recalc();
            if (!finder.hasPossibleZones())
            {
                message = "当前地图没有可用建国区域。";
                return false;
            }

            int startIndex = StableIndex(userId, finder.zones.Count);
            for (int offset = 0; offset < finder.zones.Count; offset++)
            {
                TileZone zone = finder.zones[(startIndex + offset) % finder.zones.Count];
                if (zone == null || zone.centerTile == null)
                {
                    continue;
                }

                if (TryCreateKingdomInZone(zone, actorAssetId, kingdomName, out kingdom))
                {
                    EnsureKingdomStateInitialized(kingdom);
                    SetTreasury(kingdom, AutoPanConfigHooks.InitialTreasury);
                    SetLevel(kingdom, AutoPanConfigHooks.InitialLevel);
                    kingdom.data.set(AutoPanConstants.KeyOwnerUserId, userId);
                    kingdom.data.set(AutoPanConstants.KeyOwnerName, playerName);
                    AutoPanStateRepository.BindPlayerToKingdom(userId, playerName, raceId, kingdom);
                    ClearSnapshotCache(kingdom.getID());
                    XianniAutoPanApi.Broadcast($"{playerName} 以{raceText}建立了新的国家 {kingdom.name}");
                    message = $"加入成功：已为你创建 {raceText}国家 {kingdom.name}，初始国库 {AutoPanConfigHooks.InitialTreasury}，国家等级 {AutoPanConfigHooks.InitialLevel}。开局政策为开放占领，可发送“政策 坚守城池”或“政策 开放占领”变更。";
                    return true;
                }
            }

            message = "尝试了全部建国区域，仍然无法找到可用出生点。";
            return false;
        }

        /// <summary>
        /// 管理员生成一个无绑定的随机国家。
        /// </summary>
        public static bool TrySpawnUnboundKingdom(string raceText, out string message)
        {
            message = string.Empty;
            string actorAssetId = raceText switch
            {
                "人类" => "human",
                "兽人" => "orc",
                "精灵" => "elf",
                "矮人" => "dwarf",
                _ => null
            };

            if (string.IsNullOrEmpty(actorAssetId))
            {
                message = $"未知种族：{raceText}。支持：人类/兽人/精灵/矮人。";
                return false;
            }

            if (World.world == null || World.world.city_zone_helper == null)
            {
                message = "当前世界尚未完全加载。";
                return false;
            }

            CityPlaceFinder finder = World.world.city_zone_helper.city_place_finder;
            finder.setDirty();
            finder.recalc();
            if (!finder.hasPossibleZones())
            {
                message = "当前地图没有可用建国区域。";
                return false;
            }

            int startIndex = Randy.randomInt(0, finder.zones.Count);
            for (int offset = 0; offset < finder.zones.Count; offset++)
            {
                TileZone zone = finder.zones[(startIndex + offset) % finder.zones.Count];
                if (zone == null || zone.centerTile == null)
                {
                    continue;
                }

                if (TryCreateKingdomInZone(zone, actorAssetId, BuildUniqueKingdomName(raceText), out Kingdom kingdom))
                {
                    EnsureKingdomStateInitialized(kingdom);
                    SetTreasury(kingdom, AutoPanConfigHooks.InitialTreasury);
                    SetLevel(kingdom, AutoPanConfigHooks.InitialLevel);
                    ClearSnapshotCache(kingdom.getID());
                    message = $"已生成{raceText}国家 {kingdom.name}（无绑定）。";
                    return true;
                }
            }

            message = "尝试了全部建国区域，仍然无法找到可用出生点。";
            return false;
        }

        /// <summary>
        /// 获取国家国库。
        /// </summary>
        public static int GetTreasury(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return AutoPanConfigHooks.InitialTreasury;
            }

            EnsureKingdomStateInitialized(kingdom);
            kingdom.data.get(AutoPanConstants.KeyTreasury, out int treasury, AutoPanConfigHooks.InitialTreasury);
            return Math.Max(0, treasury);
        }

        /// <summary>
        /// 设置国家国库。
        /// </summary>
        public static void SetTreasury(Kingdom kingdom, int value)
        {
            if (kingdom == null)
            {
                return;
            }

            kingdom.data.set(AutoPanConstants.KeyTreasury, Math.Max(0, value));
        }

        /// <summary>
        /// 杀掉国家所有单位，用于坚守城池最后城市被摧毁时防止无家可归者重建。
        /// </summary>
        public static void KillAllUnits(Kingdom kingdom)
        {
            if (kingdom == null || World.world?.units == null)
            {
                return;
            }

            List<Actor> toKill = new List<Actor>();
            foreach (Actor actor in World.world.units)
            {
                if (actor != null && actor.isAlive() && actor.kingdom == kingdom)
                {
                    toKill.Add(actor);
                }
            }

            foreach (Actor actor in toKill)
            {
                actor.die(pDestroy: true, AttackType.Other);
            }

            AutoPanLogService.Info($"坚守城池灭国：{kingdom.name} 共清除 {toKill.Count} 个单位。");
        }

        /// <summary>
        /// 清理即将销毁国家上的自动盘经济与绑定字段，避免复用对象时继承旧国库。
        /// </summary>
        public static void ClearRuntimeEconomyState(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return;
            }

            kingdom.data.set(AutoPanConstants.KeyTreasury, 0);
            kingdom.data.set(AutoPanConstants.KeyLevel, AutoPanConfigHooks.InitialLevel);
            kingdom.data.set(AutoPanConstants.KeyGatherSpiritUntilYear, 0);
            kingdom.data.set(AutoPanConstants.KeyOccupationPolicy, AutoPanConstants.OccupationPolicyOpen);
            kingdom.data.set(AutoPanConstants.KeyMilitiaUntilYear, 0);
            kingdom.data.set(AutoPanConstants.KeyOwnerUserId, string.Empty);
            kingdom.data.set(AutoPanConstants.KeyOwnerName, string.Empty);
            ClearSnapshotCache(kingdom.getID());
        }

        /// <summary>
        /// 增减国家国库。
        /// </summary>
        public static int AddTreasury(Kingdom kingdom, int delta)
        {
            int next = GetTreasury(kingdom) + delta;
            SetTreasury(kingdom, next);
            return GetTreasury(kingdom);
        }

        /// <summary>
        /// 获取国家等级。
        /// </summary>
        public static int GetLevel(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return AutoPanConfigHooks.InitialLevel;
            }

            EnsureKingdomStateInitialized(kingdom);
            kingdom.data.get(AutoPanConstants.KeyLevel, out int level, AutoPanConfigHooks.InitialLevel);
            return Math.Max(1, level);
        }

        /// <summary>
        /// 设置国家等级。
        /// </summary>
        public static void SetLevel(Kingdom kingdom, int level)
        {
            if (kingdom == null)
            {
                return;
            }

            kingdom.data.set(AutoPanConstants.KeyLevel, Math.Max(1, level));
        }

        /// <summary>
        /// 获取聚灵截止年份。
        /// </summary>
        public static int GetGatherSpiritUntilYear(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return 0;
            }

            EnsureKingdomStateInitialized(kingdom);
            kingdom.data.get(AutoPanConstants.KeyGatherSpiritUntilYear, out int untilYear, 0);
            return untilYear;
        }

        /// <summary>
        /// 判断聚灵是否处于生效期。
        /// </summary>
        public static bool IsGatherSpiritActive(Kingdom kingdom)
        {
            return kingdom != null && GetGatherSpiritUntilYear(kingdom) >= Date.getCurrentYear();
        }

        /// <summary>
        /// 激活或续费聚灵国策。
        /// </summary>
        public static void ActivateGatherSpirit(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return;
            }

            int currentYear = Date.getCurrentYear();
            int baseYear = Math.Max(currentYear, GetGatherSpiritUntilYear(kingdom));
            kingdom.data.set(AutoPanConstants.KeyGatherSpiritUntilYear, baseYear + AutoPanConfigHooks.GatherSpiritDurationYears);
        }

        /// <summary>
        /// 获取国家当前占领政策的显示文本。
        /// </summary>
        public static string GetOccupationPolicyText(Kingdom kingdom)
        {
            string policy = GetOccupationPolicy(kingdom);
            return policy == AutoPanConstants.OccupationPolicyDefend ? "坚守城池" : "开放占领";
        }

        /// <summary>
        /// 判断国家是否允许占领并领取被占补助。
        /// </summary>
        public static bool IsOpenOccupationPolicy(Kingdom kingdom)
        {
            return GetOccupationPolicy(kingdom) == AutoPanConstants.OccupationPolicyOpen;
        }

        /// <summary>
        /// 判断国家是否坚守城池，城镇不可被占领。
        /// </summary>
        public static bool IsDefendCityPolicy(Kingdom kingdom)
        {
            return GetOccupationPolicy(kingdom) == AutoPanConstants.OccupationPolicyDefend;
        }

        /// <summary>
        /// 变更国家占领政策。
        /// </summary>
        public static bool TryChangeOccupationPolicy(Kingdom kingdom, string rawPolicyText, out string message)
        {
            message = string.Empty;
            if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
            {
                message = "当前国家状态无效。";
                return false;
            }

            string targetPolicy = NormalizeOccupationPolicy(rawPolicyText);
            if (string.IsNullOrWhiteSpace(targetPolicy))
            {
                message = "未知国家政策，只支持：开放占领、坚守城池。";
                return false;
            }

            EnsureKingdomStateInitialized(kingdom);
            if (GetOccupationPolicy(kingdom) == targetPolicy)
            {
                message = $"{FormatKingdomLabel(kingdom)} 当前已经是 {GetOccupationPolicyText(kingdom)} 政策。";
                return false;
            }

            int cost = AutoPanConfigHooks.OccupationPolicyChangeCost;
            if (!TrySpendTreasury(kingdom, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            kingdom.data.set(AutoPanConstants.KeyOccupationPolicy, targetPolicy);
            message = $"{FormatKingdomLabel(kingdom)} 已切换为 {GetOccupationPolicyText(kingdom)} 政策，消耗 {cost} 金币。";
            return true;
        }

        /// <summary>
        /// 获取全民皆兵截止年份。
        /// </summary>
        public static int GetMilitiaUntilYear(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return 0;
            }

            EnsureKingdomStateInitialized(kingdom);
            kingdom.data.get(AutoPanConstants.KeyMilitiaUntilYear, out int untilYear, 0);
            return untilYear;
        }

        /// <summary>
        /// 判断全民皆兵是否处于生效期。
        /// </summary>
        public static bool IsMilitiaActive(Kingdom kingdom)
        {
            return kingdom != null && GetMilitiaUntilYear(kingdom) >= Date.getCurrentYear();
        }

        /// <summary>
        /// 开启全民皆兵状态，并立即执行一次全国征兵。
        /// </summary>
        public static bool TryActivateNationalMilitia(Kingdom kingdom, out string message)
        {
            message = string.Empty;
            if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
            {
                message = "当前国家状态无效。";
                return false;
            }

            int cost = AutoPanConfigHooks.NationalMilitiaCost;
            if (!TrySpendTreasury(kingdom, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            int currentYear = Date.getCurrentYear();
            int baseYear = Math.Max(currentYear, GetMilitiaUntilYear(kingdom));
            int untilYear = baseYear + AutoPanConfigHooks.NationalMilitiaDurationYears;
            kingdom.data.set(AutoPanConstants.KeyMilitiaUntilYear, untilYear);
            int drafted = AutoPanCityService.ApplyNationalMilitiaDraft(kingdom);
            message = $"{FormatKingdomLabel(kingdom)} 已开启全民皆兵，持续到第 {untilYear} 年，本次补充军队 {drafted} 人，消耗 {cost} 金币。";
            return true;
        }

        /// <summary>
        /// 计算国家的有效灵气。
        /// </summary>
        public static int GetEffectiveAura(Kingdom kingdom)
        {
            int aura = XianniAutoPanApi.GetKingdomAura(kingdom);
            if (IsGatherSpiritActive(kingdom))
            {
                aura += kingdom.countCities() * AutoPanConfigHooks.GatherSpiritAuraBonusPerCity;
            }
            return aura;
        }

        /// <summary>
        /// 计算国家的年度收入。
        /// </summary>
        public static int ComputeYearlyIncome(Kingdom kingdom)
        {
            if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
            {
                return 0;
            }

            EnsureKingdomStateInitialized(kingdom);
            int cityCount = kingdom.countCities();
            int population = kingdom.getPopulationTotal();
            int effectiveAura = GetEffectiveAura(kingdom);
            int populationGain = AutoPanConfigHooks.IncomePopulationDivisor <= 0 ? population : population / AutoPanConfigHooks.IncomePopulationDivisor;
            int auraGain = AutoPanConfigHooks.IncomeAuraDivisor <= 0 ? effectiveAura : effectiveAura / AutoPanConfigHooks.IncomeAuraDivisor;
            int income = AutoPanConfigHooks.IncomeBase
                + AutoPanConfigHooks.IncomePerCity * cityCount
                + populationGain
                + AutoPanConfigHooks.IncomePerLevel * GetLevel(kingdom)
                + auraGain;
            return Math.Max(0, income);
        }

        /// <summary>
        /// 对全图文明国家发放年度收入。
        /// </summary>
        public static void ApplyYearlyIncomeToAll(int year)
        {
            if (World.world?.kingdoms == null)
            {
                return;
            }

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
                {
                    continue;
                }

                EnsureKingdomStateInitialized(kingdom);
                int income = ComputeYearlyIncome(kingdom);
                AddTreasury(kingdom, income);
                UpdateNationalMilitiaForYear(kingdom, year);
                ClearSnapshotCache(kingdom.getID());
            }
        }

        /// <summary>
        /// 尝试消耗国家金币。
        /// </summary>
        public static bool TrySpendTreasury(Kingdom kingdom, int cost, out string error)
        {
            error = string.Empty;
            if (kingdom == null || cost < 0)
            {
                error = "国家状态无效。";
                return false;
            }

            int treasury = GetTreasury(kingdom);
            if (treasury < cost)
            {
                error = $"国家金币不足，当前 {treasury}，需要 {cost}。";
                return false;
            }

            SetTreasury(kingdom, treasury - cost);
            return true;
        }

        /// <summary>
        /// 获取国家修仙快照。
        /// </summary>
        public static XianniKingdomSnapshot GetSnapshot(Kingdom kingdom, bool forceRefresh = false)
        {
            if (kingdom == null || !kingdom.isAlive())
            {
                return new XianniKingdomSnapshot();
            }

            int currentYear = Date.getCurrentYear();
            if (!forceRefresh && SnapshotCache.TryGetValue(kingdom.getID(), out SnapshotCacheEntry entry) && entry.Year == currentYear)
            {
                return entry.Snapshot;
            }

            XianniKingdomSnapshot snapshot = XianniAutoPanApi.GetKingdomSnapshot(kingdom, 5, 3, 3);
            SnapshotCache[kingdom.getID()] = new SnapshotCacheEntry
            {
                Year = currentYear,
                Snapshot = snapshot
            };
            return snapshot;
        }

        /// <summary>
        /// 清理指定国家的榜单缓存。
        /// </summary>
        public static void ClearSnapshotCache(long kingdomId)
        {
            SnapshotCache.Remove(kingdomId);
        }

        /// <summary>
        /// 获取国家简要信息。
        /// </summary>
        public static string BuildKingdomInfoText(Kingdom kingdom)
        {
            EnsureKingdomStateInitialized(kingdom);
            XianniKingdomSnapshot snapshot = GetSnapshot(kingdom);
            int gatherSpiritUntil = GetGatherSpiritUntilYear(kingdom);
            int gatherSpiritRemain = Math.Max(0, gatherSpiritUntil - Date.getCurrentYear());
            int militiaRemain = Math.Max(0, GetMilitiaUntilYear(kingdom) - Date.getCurrentYear());
            return $"{FormatKingdomLabel(kingdom)}：国库 {GetTreasury(kingdom)}，国家等级 {GetLevel(kingdom)}，修真国等级 {snapshot.XiuzhenguoLevel}，城市 {kingdom.countCities()}，人口 {kingdom.getPopulationTotal()}，军队 {CountArmyUnits(kingdom)}，灵气 {GetEffectiveAura(kingdom)}，年收入 {ComputeYearlyIncome(kingdom)}，国家政策 {GetOccupationPolicyText(kingdom)}，聚灵剩余 {gatherSpiritRemain} 年，全民皆兵剩余 {militiaRemain} 年。";
        }

        /// <summary>
        /// 构建当前世界全部存活文明国家信息。
        /// </summary>
        public static string BuildAllKingdomInfoText()
        {
            if (World.world?.kingdoms == null)
            {
                return "当前世界未加载国家信息。";
            }

            List<Kingdom> kingdoms = World.world.kingdoms
                .Where(item => item != null && item.isAlive() && item.isCiv())
                .OrderByDescending(GetLevel)
                .ThenByDescending(item => item.getPopulationTotal())
                .ThenBy(item => item.getID())
                .ToList();
            if (kingdoms.Count == 0)
            {
                return "当前没有存活的文明国家。";
            }

            List<string> lines = new List<string> { "当前全部国家信息：" };
            Dictionary<long, string> aiShortcuts = AutoPanConfigHooks.EnableLlmAi
                ? GetAiShortcutKingdoms()
                    .Select((item, index) => new { KingdomId = item.getID(), Label = $"ai{index + 1}" })
                    .ToDictionary(item => item.KingdomId, item => item.Label)
                : new Dictionary<long, string>();
            foreach (Kingdom kingdom in kingdoms)
            {
                kingdom.data.get(AutoPanConstants.KeyOwnerName, out string ownerName, null);
                kingdom.data.get(AutoPanConstants.KeyOwnerUserId, out string ownerUserId, null);
                string ownerText = !string.IsNullOrWhiteSpace(ownerName)
                    ? $"{ownerName}({(!string.IsNullOrWhiteSpace(ownerUserId) ? ownerUserId : "未知QQ")})"
                    : "AI/无人绑定";
                string aiShortcutText = aiShortcuts.TryGetValue(kingdom.getID(), out string shortcut) ? $"，AI快捷 {shortcut}/ai({shortcut.Substring(2)})" : string.Empty;
                lines.Add($"{BuildKingdomInfoText(kingdom)}，拥有者 {ownerText}{aiShortcutText}。");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 构建指定榜单的文本输出。
        /// </summary>
        public static string BuildBoardText(Kingdom kingdom, AutoPanCommandType boardType)
        {
            XianniKingdomSnapshot snapshot = GetSnapshot(kingdom, forceRefresh: true);
            List<XianniActorEntry> source = boardType switch
            {
                AutoPanCommandType.CultivatorBoard => snapshot.Cultivators,
                AutoPanCommandType.AncientBoard => snapshot.Ancients,
                AutoPanCommandType.BeastBoard => snapshot.Beasts,
                _ => new List<XianniActorEntry>()
            };

            if (source.Count == 0)
            {
                return boardType switch
                {
                    AutoPanCommandType.CultivatorBoard => "当前国家没有上榜修士。",
                    AutoPanCommandType.AncientBoard => "当前国家没有上榜古神。",
                    AutoPanCommandType.BeastBoard => "当前国家没有上榜妖兽。",
                    _ => "当前没有可显示的数据。"
                };
            }

            string title = boardType switch
            {
                AutoPanCommandType.CultivatorBoard => "修士榜",
                AutoPanCommandType.AncientBoard => "古神榜",
                AutoPanCommandType.BeastBoard => "妖兽榜",
                _ => "榜单"
            };

            List<string> lines = new List<string> { $"{FormatKingdomLabel(kingdom)} {title}：" };
            for (int i = 0; i < source.Count; i++)
            {
                XianniActorEntry item = source[i];
                string stageLabel = boardType switch
                {
                    AutoPanCommandType.CultivatorBoard => $"境界索引 {item.StageIndex}",
                    AutoPanCommandType.AncientBoard => $"{item.StageValue} 星",
                    AutoPanCommandType.BeastBoard => $"{item.StageValue} 阶",
                    _ => item.StageValue.ToString()
                };
                lines.Add($"{i + 1}. {item.ActorName} [id={item.ActorId}]，{stageLabel}，数值 {item.PowerValue}");
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// 按槽位获取榜单单位。
        /// </summary>
        public static bool TryGetBoardActor(Kingdom kingdom, AutoPanCommandType boardType, int slotIndex, out Actor actor, out string error)
        {
            actor = null;
            error = string.Empty;
            if (slotIndex < 1)
            {
                error = "榜单序号必须从 1 开始。";
                return false;
            }

            XianniKingdomSnapshot snapshot = GetSnapshot(kingdom, forceRefresh: true);
            List<XianniActorEntry> source = boardType switch
            {
                AutoPanCommandType.CultivatorBoard => snapshot.Cultivators,
                AutoPanCommandType.AncientBoard => snapshot.Ancients,
                AutoPanCommandType.BeastBoard => snapshot.Beasts,
                _ => null
            };
            if (source == null || source.Count < slotIndex)
            {
                error = "榜单上没有这个序号。";
                return false;
            }

            actor = World.world?.units?.get(source[slotIndex - 1].ActorId);
            if (actor == null || !actor.isAlive() || actor.kingdom != kingdom)
            {
                ClearSnapshotCache(kingdom.getID());
                error = "榜单已过期，请重新查看榜单后再操作。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 按单位 ID 获取榜单成员，避免依赖榜单序号。
        /// </summary>
        public static bool TryGetBoardActorById(Kingdom kingdom, AutoPanCommandType boardType, long actorId, out Actor actor, out string error)
        {
            actor = null;
            error = string.Empty;
            if (actorId <= 0)
            {
                error = "单位 id 必须大于 0。";
                return false;
            }

            XianniKingdomSnapshot snapshot = GetSnapshot(kingdom, forceRefresh: true);
            List<XianniActorEntry> source = boardType switch
            {
                AutoPanCommandType.CultivatorBoard => snapshot.Cultivators,
                AutoPanCommandType.AncientBoard => snapshot.Ancients,
                AutoPanCommandType.BeastBoard => snapshot.Beasts,
                _ => null
            };
            if (source == null || source.All(item => item.ActorId != actorId))
            {
                error = "当前榜单中没有这个单位 id。";
                return false;
            }

            actor = World.world?.units?.get(actorId);
            if (actor == null || !actor.isAlive() || actor.kingdom != kingdom)
            {
                ClearSnapshotCache(kingdom.getID());
                error = "榜单已过期，请重新查看榜单后再操作。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 精确查找同名国家。
        /// </summary>
        public static Kingdom FindKingdomByNameExact(string kingdomName)
        {
            if (string.IsNullOrWhiteSpace(kingdomName) || World.world?.kingdoms == null)
            {
                return null;
            }

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom != null && kingdom.isAlive() && string.Equals(kingdom.name, kingdomName, StringComparison.Ordinal))
                {
                    return kingdom;
                }
            }
            return null;
        }

        /// <summary>
        /// 构建当前世界内不重复的国家名。
        /// </summary>
        public static string BuildUniqueKingdomName(string playerName)
        {
            return BuildUniqueKingdomName(playerName, 0);
        }

        /// <summary>
        /// 构建当前世界内不重复的国家名，可忽略指定国家自身。
        /// </summary>
        public static string BuildUniqueKingdomName(string playerName, long ignoreKingdomId)
        {
            string baseName = string.IsNullOrWhiteSpace(playerName) ? "无名国" : playerName.Trim();
            HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);
            if (World.world?.kingdoms != null)
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom != null && kingdom.isAlive() && kingdom.isCiv() && kingdom.getID() != ignoreKingdomId)
                    {
                        usedNames.Add(NormalizeKingdomName(kingdom.name));
                    }
                }
            }

            if (!usedNames.Contains(NormalizeKingdomName(baseName)))
            {
                return baseName;
            }

            for (int index = 2; index < 10000; index++)
            {
                string candidate = $"{baseName}#{index}";
                if (!usedNames.Contains(NormalizeKingdomName(candidate)))
                {
                    return candidate;
                }
            }

            return $"{baseName}#{Date.getCurrentYear()}";
        }

        /// <summary>
        /// 尝试为国家改名，并自动处理重名后缀。
        /// </summary>
        public static bool TryRenameKingdom(Kingdom kingdom, string rawName, out string finalName, out string error)
        {
            finalName = string.Empty;
            error = string.Empty;
            if (kingdom == null || !kingdom.isAlive())
            {
                error = "当前国家状态无效。";
                return false;
            }

            string desiredName = (rawName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(desiredName))
            {
                error = "国家新名字不能为空。";
                return false;
            }

            finalName = BuildUniqueKingdomName(desiredName, kingdom.getID());
            if (string.Equals(kingdom.name, finalName, StringComparison.Ordinal))
            {
                error = "新的国家名与当前一致，无需修改。";
                return false;
            }

            kingdom.setName(finalName);
            ClearSnapshotCache(kingdom.getID());
            AutoPanStateRepository.UpdateBoundKingdomName(kingdom.getID(), finalName);
            return true;
        }

        /// <summary>
        /// 返回带有 kingdomId 的国家标签。
        /// </summary>
        public static string FormatKingdomLabel(Kingdom kingdom)
        {
            return kingdom == null ? "未知国家" : $"{kingdom.name} [{kingdom.getID()}]";
        }

        /// <summary>
        /// 解析外交或管理员命令中的国家名，优先精确匹配，其次唯一包含匹配。
        /// </summary>
        public static bool TryResolveKingdom(string rawName, out Kingdom kingdom, out string error)
        {
            kingdom = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(rawName) || World.world?.kingdoms == null)
            {
                error = "目标国家名不能为空。";
                return false;
            }

            if (TryResolveKingdomByMention(rawName, out kingdom, out error))
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            if (TryResolveAiShortcut(rawName, out kingdom, out error))
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            if (TryExtractTaggedId(rawName, out long explicitKingdomId))
            {
                kingdom = World.world.kingdoms.get(explicitKingdomId);
                if (kingdom != null && kingdom.isAlive() && kingdom.isCiv())
                {
                    return true;
                }

                error = $"找不到 kingdomId={explicitKingdomId} 对应的存活文明国家。";
                return false;
            }

            string expected = NormalizeKingdomName(rawName);
            List<Kingdom> aliveKingdoms = new List<Kingdom>();
            foreach (Kingdom item in World.world.kingdoms)
            {
                if (item != null && item.isAlive() && item.isCiv())
                {
                    aliveKingdoms.Add(item);
                }
            }

            List<Kingdom> exactMatches = new List<Kingdom>();
            foreach (Kingdom item in aliveKingdoms)
            {
                if (NormalizeKingdomName(item.name) == expected)
                {
                    exactMatches.Add(item);
                }
            }

            if (exactMatches.Count == 1)
            {
                kingdom = exactMatches[0];
                return true;
            }

            if (exactMatches.Count > 1)
            {
                error = "国家名重复，请改用“国家名 [kingdomId]”：" + string.Join("，", exactMatches.Select(FormatKingdomLabel).Take(8).ToArray());
                return false;
            }

            List<Kingdom> partialMatches = new List<Kingdom>();
            foreach (Kingdom item in aliveKingdoms)
            {
                string currentName = NormalizeKingdomName(item.name);
                if (currentName.Contains(expected) || expected.Contains(currentName))
                {
                    partialMatches.Add(item);
                }
            }

            if (partialMatches.Count == 1)
            {
                kingdom = partialMatches[0];
                return true;
            }

            if (partialMatches.Count > 1)
            {
                error = "国家名匹配到多个结果，请改用“国家名 [kingdomId]”：" + string.Join("，", partialMatches.Select(FormatKingdomLabel).Take(8).ToArray());
                return false;
            }

            error = "找不到目标国家。当前可选国家：" + string.Join("，", aliveKingdoms.Select(FormatKingdomLabel).Take(10).ToArray());
            return false;
        }

        private static bool TryResolveAiShortcut(string rawName, out Kingdom kingdom, out string error)
        {
            kingdom = null;
            error = string.Empty;
            Match match = Regex.Match((rawName ?? string.Empty).Trim(), @"^ai(?:[\(（](\d+)[\)）]|(\d+))$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }
            if (!AutoPanConfigHooks.EnableLlmAi)
            {
                error = "全局自动盘 AI 未开启，无法使用 ai 快捷目标。";
                return false;
            }

            string indexText = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!int.TryParse(indexText, out int shortcutIndex) || shortcutIndex <= 0)
            {
                error = "AI 快捷目标格式错误，请使用 ai1、ai(1) 或 ai（1）。";
                return false;
            }

            List<Kingdom> aiKingdoms = GetAiShortcutKingdoms();
            if (shortcutIndex > aiKingdoms.Count)
            {
                error = $"当前只有 {aiKingdoms.Count} 个 AI 国家可用，找不到 ai{shortcutIndex}。";
                return false;
            }

            kingdom = aiKingdoms[shortcutIndex - 1];
            return true;
        }

        private static List<Kingdom> GetAiShortcutKingdoms()
        {
            if (World.world?.kingdoms == null)
            {
                return new List<Kingdom>();
            }

            return World.world.kingdoms
                .Where(item => item != null && item.isAlive() && item.isCiv() && !AutoPanStateRepository.IsPlayerOwnedKingdom(item))
                .OrderByDescending(item => item.countCities())
                .ThenByDescending(item => item.getPopulationTotal())
                .ThenBy(item => item.name)
                .ThenBy(item => item.getID())
                .ToList();
        }

        private static bool TryResolveKingdomByMention(string rawName, out Kingdom kingdom, out string error)
        {
            kingdom = null;
            error = string.Empty;
            if (!TryExtractMentionedUserId(rawName, out string userId))
            {
                return false;
            }

            if (!AutoPanStateRepository.TryGetLiveBinding(userId, out AutoPanBindingRecord binding, out kingdom))
            {
                error = $"@目标 QQ {userId} 当前没有存活绑定国家。";
                return false;
            }

            if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
            {
                error = $"@目标 QQ {userId} 绑定的国家当前不可用。";
                return false;
            }

            return true;
        }

        private static bool TryExtractMentionedUserId(string rawName, out string userId)
        {
            userId = string.Empty;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return false;
            }

            Match cqMatch = Regex.Match(rawName, @"\[CQ:at,qq=(\d+)\]", RegexOptions.IgnoreCase);
            if (cqMatch.Success)
            {
                userId = cqMatch.Groups[1].Value;
                return true;
            }

            Match plainMatch = Regex.Match(rawName.Trim(), @"^@(\d{5,})$");
            if (plainMatch.Success)
            {
                userId = plainMatch.Groups[1].Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 查找当前国家与目标国家之间的战争关系，兼容攻守双方任意方向。
        /// </summary>
        public static bool TryFindWarWith(Kingdom kingdom, Kingdom target, out War war)
        {
            war = null;
            if (kingdom == null || target == null)
            {
                return false;
            }

            foreach (War item in kingdom.getWars())
            {
                if (item != null && !item.hasEnded() && item.isInWarWith(kingdom, target))
                {
                    war = item;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 调整自动盘国家等级。
        /// </summary>
        public static bool TryAdjustNationLevel(Kingdom kingdom, int delta, out int newLevel, out string error)
        {
            error = string.Empty;
            newLevel = GetLevel(kingdom);
            if (kingdom == null || !kingdom.isAlive())
            {
                error = "当前国家状态无效。";
                return false;
            }

            int nextLevel = Mathf.Clamp(newLevel + delta, 1, 99);
            if (nextLevel == newLevel)
            {
                error = delta > 0 ? "当前国家等级已达到上限。" : "当前国家等级已是最低等级。";
                return false;
            }

            SetLevel(kingdom, nextLevel);
            newLevel = nextLevel;
            return true;
        }

        /// <summary>
        /// 通过召唤符合要求境界的修士，自然推动修真国等级提升 1 级。
        /// </summary>
        public static bool TryPromoteXiuzhenguoNaturally(Kingdom kingdom, out int previousLevel, out int targetLevel, out int spawnedCount, out string error)
        {
            error = string.Empty;
            spawnedCount = 0;
            previousLevel = XianniAutoPanApi.CalculateXiuzhenguoLevel(kingdom);
            targetLevel = Math.Min(previousLevel + 1, XiuzhenguoRequirements.Length - 1);
            if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
            {
                error = "当前国家状态无效。";
                return false;
            }

            if (targetLevel == previousLevel)
            {
                return true;
            }

            XiuzhenguoLevelRequirement requirement = XiuzhenguoRequirements[targetLevel];
            List<int> spawnRealmPlan = BuildPromotionSpawnPlan(kingdom, requirement);
            if (spawnRealmPlan.Count == 0)
            {
                XianniAutoPanApi.ClearXiuzhenguoManualOffset(kingdom);
                int refreshedLevel = XianniAutoPanApi.RefreshXiuzhenguoLevel(kingdom);
                if (refreshedLevel >= targetLevel)
                {
                    previousLevel = refreshedLevel - 1;
                    return true;
                }

                error = "当前国家未能满足下一级修真国需求。";
                return false;
            }

            string actorAssetId = GetSpawnActorAssetId(kingdom);
            List<City> cities = kingdom.getCities()
                .Where(city => city != null && city.isAlive())
                .OrderByDescending(city => city.isCapitalCity())
                .ThenBy(city => city.getID())
                .ToList();
            if (cities.Count == 0)
            {
                error = "当前国家没有可召唤修士的城市。";
                return false;
            }

            List<Actor> spawnedActors = new List<Actor>();
            for (int index = 0; index < spawnRealmPlan.Count; index++)
            {
                City city = cities[index % cities.Count];
                if (!TrySpawnCultivatorForCity(kingdom, city, actorAssetId, spawnRealmPlan[index], out Actor spawned))
                {
                    continue;
                }

                spawnedActors.Add(spawned);
            }

            XianniAutoPanApi.ClearXiuzhenguoManualOffset(kingdom);
            int naturalLevel = XianniAutoPanApi.RefreshXiuzhenguoLevel(kingdom);
            if (naturalLevel < targetLevel)
            {
                int delta = targetLevel - naturalLevel;
                if (!XianniAutoPanApi.TryAdjustXiuzhenguoLevel(kingdom, delta, out int visibleLevel, out _)
                    || visibleLevel < targetLevel)
                {
                    error = "修真国等级刷新失败，已召来达标修士但未能推进等级。";
                    return false;
                }
            }

            spawnedCount = spawnedActors.Count;
            ClearSnapshotCache(kingdom.getID());
            return true;
        }

        /// <summary>
        /// 通过斩首满足条件的修士，自然压低修真国等级。
        /// </summary>
        public static bool TryLowerXiuzhenguoNaturally(Kingdom kingdom, int levels, out int previousLevel, out int resultLevel, out int killedCount, out string error)
        {
            error = string.Empty;
            killedCount = 0;
            previousLevel = XianniAutoPanApi.CalculateXiuzhenguoLevel(kingdom);
            resultLevel = previousLevel;
            if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
            {
                error = "目标国家状态无效。";
                return false;
            }

            int targetLevel = Math.Max(0, previousLevel - Math.Max(1, levels));
            if (targetLevel == previousLevel)
            {
                error = "目标修真国等级已经无法继续下降。";
                return false;
            }

            XianniAutoPanApi.ClearXiuzhenguoManualOffset(kingdom);
            while (resultLevel > targetLevel)
            {
                XiuzhenguoLevelRequirement requirement = XiuzhenguoRequirements[resultLevel];
                List<Actor> primaryCandidates = GetCultivatorsAtOrAbove(kingdom, requirement.RequiredRealmIndex);
                List<Actor> secondaryCandidates = GetCultivatorsAtOrAbove(kingdom, requirement.SecondaryRealmIndex);
                int primaryNeed = requirement.RequiredRealmIndex < 0 ? int.MaxValue : Math.Max(1, primaryCandidates.Count - requirement.RequiredCount + 1);
                int secondaryNeed = requirement.SecondaryRealmIndex < 0 ? int.MaxValue : Math.Max(1, secondaryCandidates.Count - requirement.SecondaryCount + 1);

                List<Actor> victims;
                if (primaryNeed <= secondaryNeed)
                {
                    victims = primaryCandidates.Take(primaryNeed).ToList();
                }
                else
                {
                    victims = secondaryCandidates.Take(secondaryNeed).ToList();
                }

                if (victims.Count == 0)
                {
                    error = "没有找到符合当前修真国门槛的修士，无法自然降低修真国等级。";
                    return false;
                }

                foreach (Actor actor in victims)
                {
                    if (actor != null && actor.isAlive())
                    {
                        actor.dieAndDestroy(AttackType.Other);
                        killedCount++;
                    }
                }

                resultLevel = XianniAutoPanApi.RefreshXiuzhenguoLevel(kingdom);
            }

            ClearSnapshotCache(kingdom.getID());
            return killedCount > 0;
        }

        /// <summary>
        /// 强制合并或创建联盟，兼容多个国家加入同一联盟。
        /// </summary>
        public static bool TryCreateOrMergeAlliance(Kingdom source, Kingdom target)
        {
            if (source == null || target == null || !source.isAlive() || !target.isAlive())
            {
                return false;
            }

            Alliance sourceAlliance = source.getAlliance();
            Alliance targetAlliance = target.getAlliance();
            if (Alliance.isSame(sourceAlliance, targetAlliance))
            {
                return true;
            }

            if (sourceAlliance == null && targetAlliance == null)
            {
                World.world.alliances.forceAlliance(source, target);
                return Alliance.isSame(source.getAlliance(), target.getAlliance());
            }

            if (sourceAlliance != null && targetAlliance == null)
            {
                return sourceAlliance.join(target, pRecalc: true, pForce: true);
            }

            if (sourceAlliance == null && targetAlliance != null)
            {
                return targetAlliance.join(source, pRecalc: true, pForce: true);
            }

            List<Kingdom> migratingMembers = targetAlliance.kingdoms_list
                .Where(item => item != null && item.isAlive())
                .ToList();
            foreach (Kingdom member in migratingMembers)
            {
                targetAlliance.leave(member, pRecalc: false);
            }
            targetAlliance.recalculate();

            foreach (Kingdom member in migratingMembers)
            {
                sourceAlliance.join(member, pRecalc: false, pForce: true);
            }
            sourceAlliance.recalculate();

            if (targetAlliance.kingdoms_hashset.Count == 0)
            {
                World.world.alliances.dissolveAlliance(targetAlliance);
            }

            return Alliance.isSame(source.getAlliance(), target.getAlliance());
        }

        /// <summary>
        /// 让当前国家主动退盟。
        /// </summary>
        public static bool TryLeaveAlliance(Kingdom kingdom, out string allianceName)
        {
            allianceName = string.Empty;
            Alliance alliance = kingdom?.getAlliance();
            if (alliance == null)
            {
                return false;
            }

            allianceName = alliance.name;
            alliance.leave(kingdom);
            if (alliance.kingdoms_hashset.Count < 2)
            {
                World.world.alliances.dissolveAlliance(alliance);
            }
            return true;
        }

        /// <summary>
        /// 为当前国家增加指定数量的同种族成年人口。
        /// </summary>
        public static bool TryAddPopulation(Kingdom kingdom, int count, out string message)
        {
            message = string.Empty;
            if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
            {
                message = "当前国家状态无效。";
                return false;
            }

            if (count <= 0)
            {
                message = "增加人数的数量必须大于 0。";
                return false;
            }

            if (count > AutoPanConstants.MaxAddPopulationPerCommand)
            {
                message = $"增加人数一次最多只能增加 {AutoPanConstants.MaxAddPopulationPerCommand} 人。";
                return false;
            }

            int safeCount = count;
            int totalCost = safeCount * AutoPanConfigHooks.AddPopulationCostPerUnit;
            if (!TrySpendTreasury(kingdom, totalCost, out string spendError))
            {
                message = spendError;
                return false;
            }

            string actorAssetId = GetSpawnActorAssetId(kingdom);
            List<City> cities = kingdom.getCities()
                .Where(city => city != null && city.isAlive())
                .OrderByDescending(city => city.isCapitalCity())
                .ThenBy(city => city.getID())
                .ToList();
            if (cities.Count == 0)
            {
                AddTreasury(kingdom, totalCost);
                message = "当前国家没有可放置新人口的城市。";
                return false;
            }

            int created = 0;
            for (int index = 0; index < safeCount; index++)
            {
                City city = cities[index % cities.Count];
                if (!TrySpawnCitizenForCity(kingdom, city, actorAssetId))
                {
                    continue;
                }

                created++;
            }

            if (created == 0)
            {
                AddTreasury(kingdom, totalCost);
                message = "本次没有成功增加任何人口，请稍后再试。";
                return false;
            }

            int refund = totalCost - created * AutoPanConfigHooks.AddPopulationCostPerUnit;
            if (refund > 0)
            {
                AddTreasury(kingdom, refund);
            }

            ClearSnapshotCache(kingdom.getID());
            message = $"{FormatKingdomLabel(kingdom)} 已增加 {created} 名成年人口，消耗 {created * AutoPanConfigHooks.AddPopulationCostPerUnit} 金币。";
            return true;
        }

        /// <summary>
        /// 向目标国家转账国家金币。
        /// </summary>
        public static bool TryTransferTreasury(Kingdom source, Kingdom target, int amount, out string message)
        {
            message = string.Empty;
            if (source == null || target == null || !source.isAlive() || !target.isAlive())
            {
                message = "转账目标无效。";
                return false;
            }

            if (source == target)
            {
                message = "不能向自己的国家转账。";
                return false;
            }

            if (amount <= 0)
            {
                message = "转账金额必须大于 0。";
                return false;
            }

            if (!TrySpendTreasury(source, amount, out string spendError))
            {
                message = spendError;
                return false;
            }

            AddTreasury(target, amount);
            message = $"{FormatKingdomLabel(source)} 已向 {FormatKingdomLabel(target)} 转账 {amount} 金币。";
            return true;
        }

        /// <summary>
        /// 构建前端仪表盘需要的全图国家摘要。
        /// </summary>
        public static List<AutoPanKingdomDashboardInfo> BuildDashboardKingdoms()
        {
            List<AutoPanKingdomDashboardInfo> result = new List<AutoPanKingdomDashboardInfo>();
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

                XianniKingdomSnapshot snapshot = GetSnapshot(kingdom);
                kingdom.data.get(AutoPanConstants.KeyOwnerName, out string ownerName, string.Empty);
                kingdom.data.get(AutoPanConstants.KeyOwnerUserId, out string ownerUserId, string.Empty);
                Alliance alliance = kingdom.getAlliance();
                result.Add(new AutoPanKingdomDashboardInfo
                {
                    KingdomId = kingdom.getID(),
                    KingdomName = kingdom.name,
                    OwnerName = ownerName,
                    OwnerUserId = ownerUserId,
                    Treasury = GetTreasury(kingdom),
                    NationLevel = GetLevel(kingdom),
                    XiuzhenguoLevel = snapshot.XiuzhenguoLevel,
                    CityCount = kingdom.countCities(),
                    Population = kingdom.getPopulationTotal(),
                    TotalAura = GetEffectiveAura(kingdom),
                    AnnualIncome = ComputeYearlyIncome(kingdom),
                    AllianceName = alliance?.name ?? string.Empty,
                    AtWar = kingdom.getWars().Any()
                });
            }

            return result
                .OrderByDescending(item => item.Population)
                .ThenByDescending(item => item.CityCount)
                .ThenBy(item => item.KingdomName)
                .ToList();
        }

        private static string NormalizeKingdomName(string name)
        {
            return new string((name ?? string.Empty).Trim().Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        }

        private static string GetOccupationPolicy(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return AutoPanConstants.OccupationPolicyOpen;
            }

            EnsureKingdomStateInitialized(kingdom);
            kingdom.data.get(AutoPanConstants.KeyOccupationPolicy, out string policy, AutoPanConstants.OccupationPolicyOpen);
            return string.IsNullOrWhiteSpace(policy) ? AutoPanConstants.OccupationPolicyOpen : policy;
        }

        private static string NormalizeOccupationPolicy(string rawPolicyText)
        {
            string text = (rawPolicyText ?? string.Empty).Trim();
            return text switch
            {
                "开放占领" => AutoPanConstants.OccupationPolicyOpen,
                "坚守城池" => AutoPanConstants.OccupationPolicyDefend,
                _ => string.Empty
            };
        }

        private static void UpdateNationalMilitiaForYear(Kingdom kingdom, int year)
        {
            int untilYear = GetMilitiaUntilYear(kingdom);
            if (untilYear <= 0)
            {
                return;
            }

            if (untilYear < year)
            {
                kingdom.data.set(AutoPanConstants.KeyMilitiaUntilYear, 0);
                string text = $"{FormatKingdomLabel(kingdom)} 的全民皆兵状态已结束。";
                XianniAutoPanApi.Broadcast(text);
                AutoPanNotificationService.NotifyKingdomOwners(kingdom, text);
                return;
            }

            int drafted = AutoPanCityService.ApplyNationalMilitiaDraft(kingdom);
            if (drafted > 0)
            {
                AutoPanLogService.Info($"{kingdom.name} 全民皆兵年度补充 {drafted} 人。");
            }
        }

        /// <summary>
        /// 统计国家当前军队数量。
        /// </summary>
        public static int CountArmyUnits(Kingdom kingdom)
        {
            if (kingdom == null || !kingdom.isAlive())
            {
                return 0;
            }

            int total = 0;
            foreach (City city in kingdom.getCities())
            {
                if (city == null || !city.isAlive())
                {
                    continue;
                }

                total += city.countWarriors();
            }

            return total;
        }

        private static bool TryExtractTaggedId(string rawText, out long objectId)
        {
            objectId = 0L;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return false;
            }

            int left = rawText.LastIndexOf('[');
            int right = rawText.LastIndexOf(']');
            if (left < 0 || right <= left)
            {
                return false;
            }

            string idText = rawText.Substring(left + 1, right - left - 1).Trim();
            return long.TryParse(idText, out objectId);
        }

        /// <summary>
        /// 构建 AI 决策所需快照。
        /// </summary>
        public static AutoPanAiRequestContext BuildAiContext(Kingdom kingdom)
        {
            int currentYear = Date.getCurrentYear();
            XianniKingdomSnapshot snapshot = GetSnapshot(kingdom, forceRefresh: true);
            Alliance alliance = kingdom.getAlliance();
            AutoPanAiRequestContext context = new AutoPanAiRequestContext
            {
                CurrentYear = currentYear,
                DeclareWarStartYear = AutoPanConfigHooks.PlayerDecisionStartYear,
                CanDeclareWar = currentYear >= AutoPanConfigHooks.PlayerDecisionStartYear,
                RequestTimeoutSeconds = AutoPanConfigHooks.RequestTimeoutSeconds,
                DecisionIntensity = AutoPanConfigHooks.AiDecisionIntensity,
                MaxActions = Math.Max(1, Math.Min(5, AutoPanConfigHooks.AiDecisionIntensity)),
                KingdomId = kingdom.getID(),
                KingdomName = kingdom.name,
                Treasury = GetTreasury(kingdom),
                NationLevel = GetLevel(kingdom),
                XiuzhenguoLevel = snapshot.XiuzhenguoLevel,
                AnnualIncome = ComputeYearlyIncome(kingdom),
                CityCount = kingdom.countCities(),
                Population = kingdom.getPopulationTotal(),
                TotalAura = GetEffectiveAura(kingdom),
                ArmyCount = CountArmyUnits(kingdom),
                OccupationPolicy = GetOccupationPolicyText(kingdom),
                AllianceName = alliance?.name ?? string.Empty,
                AtWar = kingdom.getWars().Any(),
                GatherSpiritActive = IsGatherSpiritActive(kingdom),
                GatherSpiritRemainYears = Math.Max(0, GetGatherSpiritUntilYear(kingdom) - currentYear),
                MilitiaRemainYears = Math.Max(0, GetMilitiaUntilYear(kingdom) - currentYear)
            };

            foreach (War war in kingdom.getWars())
            {
                Kingdom other = war.isMainAttacker(kingdom) ? war.getMainDefender() : war.getMainAttacker();
                if (other != null && other.isAlive())
                {
                    context.EnemyKingdomNames.Add(FormatKingdomLabel(other));
                }
            }

            foreach (Kingdom other in World.world.kingdoms)
            {
                if (other == null || !other.isAlive() || !other.isCiv() || other == kingdom)
                {
                    continue;
                }

                if (context.CanDeclareWar && !kingdom.isEnemy(other) && !Alliance.isSame(kingdom.getAlliance(), other.getAlliance()))
                {
                    context.CandidateKingdomNames.Add(FormatKingdomLabel(other));
                }
            }

            foreach (Kingdom other in World.world.kingdoms)
            {
                if (other == null || !other.isAlive() || !other.isCiv())
                {
                    continue;
                }

                XianniKingdomSnapshot otherSnapshot = GetSnapshot(other);
                other.data.get(AutoPanConstants.KeyOwnerName, out string ownerName, string.Empty);
                Alliance otherAlliance = other.getAlliance();
                context.AllKingdoms.Add(new AutoPanAiKingdomSummary
                {
                    Label = FormatKingdomLabel(other),
                    IsSelf = other == kingdom,
                    IsPlayerOwned = AutoPanStateRepository.IsPlayerOwnedKingdom(other),
                    OwnerName = ownerName ?? string.Empty,
                    RelationToSelf = BuildAiRelationText(kingdom, other),
                    Treasury = GetTreasury(other),
                    NationLevel = GetLevel(other),
                    XiuzhenguoLevel = otherSnapshot.XiuzhenguoLevel,
                    CityCount = other.countCities(),
                    Population = other.getPopulationTotal(),
                    ArmyCount = CountArmyUnits(other),
                    TotalAura = GetEffectiveAura(other),
                    AnnualIncome = ComputeYearlyIncome(other),
                    OccupationPolicy = GetOccupationPolicyText(other),
                    AllianceName = otherAlliance?.name ?? string.Empty,
                    AtWar = other.getWars().Any()
                });
            }

            context.CultivatorChoices = snapshot.Cultivators.Select((item, index) => $"{index + 1}. {item.ActorName} [id={item.ActorId}] / 境界索引{item.StageIndex} / 修为{item.PowerValue} / 可用：修士 {item.ActorId} 闭关、修士 {item.ActorId} 升境").ToList();
            context.AncientChoices = snapshot.Ancients.Select((item, index) => $"{index + 1}. {item.ActorName} [id={item.ActorId}] / {item.StageValue}星 / 古神之力{item.PowerValue} / 可用：古神 {item.ActorId} 炼体、古神 {item.ActorId} 升星").ToList();
            context.BeastChoices = snapshot.Beasts.Select((item, index) => $"{index + 1}. {item.ActorName} [id={item.ActorId}] / {item.StageValue}阶 / 妖力{item.PowerValue} / 可用：妖兽 {item.ActorId} 养成、妖兽 {item.ActorId} 升阶").ToList();
            context.AllKingdoms = context.AllKingdoms
                .OrderByDescending(item => item.IsSelf)
                .ThenByDescending(item => item.CityCount)
                .ThenByDescending(item => item.Population)
                .ThenBy(item => item.Label)
                .ToList();
            return context;
        }

        private static string BuildAiRelationText(Kingdom self, Kingdom other)
        {
            if (self == null || other == null)
            {
                return "未知";
            }
            if (self == other)
            {
                return "本国";
            }
            if (self.isEnemy(other))
            {
                return "战争";
            }
            if (Alliance.isSame(self.getAlliance(), other.getAlliance()))
            {
                return "同盟";
            }
            return "可外交";
        }

        /// <summary>
        /// 处理战争结束后的国库奖励。
        /// </summary>
        public static void HandleWarEnded(War war, WarWinner winner)
        {
            AutoPanLogService.Info("战争结束：固定胜败金币奖励已关闭，改为按开放占领政策的被占城市逐座补助。");
        }

        /// <summary>
        /// 对开放占领政策国家发放被占城市补助。
        /// </summary>
        public static void GrantOccupationSubsidy(Kingdom loser, Kingdom occupier, string cityName)
        {
            if (loser == null || !loser.isAlive() || !loser.isCiv() || !IsOpenOccupationPolicy(loser))
            {
                return;
            }

            int min = Math.Max(0, AutoPanConfigHooks.OccupationSubsidyMin);
            int max = Math.Max(min, AutoPanConfigHooks.OccupationSubsidyMax);
            int reward = max <= min ? min : Randy.randomInt(min, max + 1);
            if (reward <= 0)
            {
                return;
            }

            int treasury = AddTreasury(loser, reward);
            string occupierText = occupier != null ? FormatKingdomLabel(occupier) : "敌国";
            string text = $"{FormatKingdomLabel(loser)} 采用开放占领政策，城市 {cityName} 被 {occupierText} 占领后获得 {reward} 金币补助，当前国库 {treasury}。";
            XianniAutoPanApi.Broadcast(text);
            AutoPanNotificationService.NotifyKingdomOwners(loser, text);
            AutoPanLogService.Info(text);
        }

        private static List<int> BuildPromotionSpawnPlan(Kingdom kingdom, XiuzhenguoLevelRequirement requirement)
        {
            List<int> plan = new List<int>();
            int primaryCount = CountCultivatorsAtOrAbove(kingdom, requirement.RequiredRealmIndex);
            int secondaryCount = CountCultivatorsAtOrAbove(kingdom, requirement.SecondaryRealmIndex);

            int primaryDeficit = requirement.RequiredRealmIndex < 0 ? 0 : Math.Max(0, requirement.RequiredCount - primaryCount);
            for (int i = 0; i < primaryDeficit; i++)
            {
                plan.Add(requirement.RequiredRealmIndex);
                if (requirement.SecondaryRealmIndex >= 0 && requirement.RequiredRealmIndex >= requirement.SecondaryRealmIndex)
                {
                    secondaryCount++;
                }
            }

            int secondaryDeficit = requirement.SecondaryRealmIndex < 0 ? 0 : Math.Max(0, requirement.SecondaryCount - secondaryCount);
            for (int i = 0; i < secondaryDeficit; i++)
            {
                plan.Add(requirement.SecondaryRealmIndex);
            }

            return plan;
        }

        private static int CountCultivatorsAtOrAbove(Kingdom kingdom, int realmIndex)
        {
            if (kingdom == null || kingdom.units == null || realmIndex < 0)
            {
                return 0;
            }

            int count = 0;
            foreach (Actor actor in kingdom.units)
            {
                if (actor == null || !actor.isAlive())
                {
                    continue;
                }

                if (XianniAutoPanApi.GetCultivatorRealmIndex(actor) >= realmIndex)
                {
                    count++;
                }
            }

            return count;
        }

        private static List<Actor> GetCultivatorsAtOrAbove(Kingdom kingdom, int realmIndex)
        {
            if (kingdom == null || kingdom.units == null || realmIndex < 0)
            {
                return new List<Actor>();
            }

            return kingdom.units
                .Where(actor => actor != null && actor.isAlive() && XianniAutoPanApi.GetCultivatorRealmIndex(actor) >= realmIndex)
                .OrderBy(actor => XianniAutoPanApi.GetCultivatorRealmIndex(actor))
                .ThenBy(actor => XianniAutoPanApi.GetPowerScore(actor))
                .ThenBy(actor => actor.getID())
                .ToList();
        }

        private static bool TrySpawnCultivatorForCity(Kingdom kingdom, City city, string actorAssetId, int realmIndex, out Actor actor)
        {
            actor = null;
            if (city == null || !city.isAlive() || realmIndex < 0)
            {
                return false;
            }

            foreach (WorldTile tile in CollectCityGroundTiles(city))
            {
                Actor spawned = SpawnAdult(actorAssetId, tile);
                if (spawned == null)
                {
                    continue;
                }

                spawned.joinKingdom(kingdom);
                spawned.joinCity(city);
                if (!AutoPanCultivationPromotionService.TrySetCultivatorRealm(spawned, realmIndex))
                {
                    spawned.dieAndDestroy(AttackType.Other);
                    continue;
                }

                actor = spawned;
                return true;
            }

            return false;
        }

        private static bool TryCreateKingdomInZone(TileZone zone, string actorAssetId, string kingdomName, out Kingdom kingdom)
        {
            kingdom = null;
            List<WorldTile> candidateTiles = CollectZoneGroundTiles(zone);
            if (candidateTiles.Count < AutoPanConstants.JoinUnitCount)
            {
                return false;
            }

            for (int founderIndex = 0; founderIndex < candidateTiles.Count; founderIndex++)
            {
                WorldTile founderTile = candidateTiles[founderIndex];
                Actor founder = SpawnAdult(actorAssetId, founderTile);
                if (founder == null)
                {
                    continue;
                }

                if (!CanFounderStartCivilization(founder) || !founder.buildCityAndStartCivilization())
                {
                    World.world.units.destroyObject(founder);
                    continue;
                }

                kingdom = founder.kingdom;
                if (kingdom == null || !kingdom.isAlive())
                {
                    if (founder != null && founder.isAlive())
                    {
                        founder.dieAndDestroy(AttackType.None);
                    }
                    kingdom = null;
                    continue;
                }

                City capital = founder.city ?? founder.current_zone?.city ?? kingdom.capital;
                if (capital == null || !capital.isAlive())
                {
                    AutoPanLogService.Error($"国家 {kingdomName} 建立后未能找到首都城市，已放弃本次建国结果。");
                    if (founder.isAlive())
                    {
                        founder.dieAndDestroy(AttackType.None);
                    }
                    kingdom = null;
                    continue;
                }

                kingdom.setCapital(capital);
                World.world.kingdoms.updateDirtyCities();
                kingdom.setName(kingdomName);
                int supporterCount = 0;
                for (int i = 0; i < candidateTiles.Count && supporterCount < AutoPanConstants.JoinUnitCount - 1; i++)
                {
                    if (i == founderIndex)
                    {
                        continue;
                    }

                    Actor supporter = SpawnAdult(actorAssetId, candidateTiles[i]);
                    if (supporter == null)
                    {
                        continue;
                    }

                    supporter.joinKingdom(kingdom);
                    supporter.joinCity(capital);
                    supporterCount++;
                }

                if (supporterCount < AutoPanConstants.JoinUnitCount - 1)
                {
                    AutoPanLogService.Error($"国家 {kingdomName} 建立时仅成功补充 {supporterCount + 1}/{AutoPanConstants.JoinUnitCount} 个单位，已保留当前国家。");
                }
                return true;
            }

            return false;
        }

        private static bool TrySpawnCitizenForCity(Kingdom kingdom, City city, string actorAssetId)
        {
            if (city == null || !city.isAlive())
            {
                return false;
            }

            foreach (WorldTile tile in CollectCityGroundTiles(city))
            {
                Actor actor = SpawnAdult(actorAssetId, tile);
                if (actor == null)
                {
                    continue;
                }

                actor.joinKingdom(kingdom);
                actor.joinCity(city);
                actor.data.age_overgrowth = AutoPanConstants.FixedAdultAge;
                return true;
            }

            return false;
        }

        private static List<WorldTile> CollectCityGroundTiles(City city)
        {
            List<WorldTile> tiles = new List<WorldTile>();
            if (city == null)
            {
                return tiles;
            }

            WorldTile centerTile = city.getTile();
            if (centerTile != null)
            {
                AddTileIfValid(centerTile.zone, centerTile, tiles);
                WorldTile[] chunkTiles = centerTile.chunk?.tiles;
                if (chunkTiles != null)
                {
                    for (int i = 0; i < chunkTiles.Length; i++)
                    {
                        AddTileIfValid(centerTile.zone, chunkTiles[i], tiles);
                    }
                }
            }

            foreach (TileZone zone in city.zones)
            {
                if (zone?.centerTile == null)
                {
                    continue;
                }

                AddTileIfValid(zone, zone.centerTile, tiles);
                WorldTile[] chunkTiles = zone.centerTile.chunk?.tiles;
                if (chunkTiles == null)
                {
                    continue;
                }

                for (int i = 0; i < chunkTiles.Length; i++)
                {
                    AddTileIfValid(zone, chunkTiles[i], tiles);
                }
            }

            return tiles;
        }

        private static bool CanFounderStartCivilization(Actor founder)
        {
            if (founder == null || founder.current_tile == null || founder.current_zone == null)
            {
                return false;
            }

            if (founder.kingdom?.asset != null && founder.kingdom.asset.is_forced_by_trait)
            {
                return false;
            }

            if (!founder.canBuildNewCity())
            {
                return false;
            }

            KingdomAsset kingdomAsset = AssetManager.kingdoms.get(founder.asset.kingdom_id_civilization);
            if (kingdomAsset == null || !kingdomAsset.civ)
            {
                return false;
            }

            TileZone zone = founder.current_zone;
            TileZone[] neighbours = zone.neighbours;
            if (neighbours == null)
            {
                return true;
            }

            for (int i = 0; i < neighbours.Length; i++)
            {
                TileZone neighbour = neighbours[i];
                if (neighbour == null || !neighbour.hasCity())
                {
                    continue;
                }

                WorldTile neighbourTile = neighbour.city?.getTile();
                if (neighbourTile != null && neighbourTile.isSameIsland(founder.current_tile))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<WorldTile> CollectZoneGroundTiles(TileZone zone)
        {
            List<WorldTile> tiles = new List<WorldTile>();
            if (zone?.centerTile == null)
            {
                return tiles;
            }

            AddTileIfValid(zone, zone.centerTile, tiles);
            WorldTile[] chunkTiles = zone.centerTile.chunk?.tiles;
            if (chunkTiles != null)
            {
                for (int i = 0; i < chunkTiles.Length; i++)
                {
                    AddTileIfValid(zone, chunkTiles[i], tiles);
                }
            }
            return tiles;
        }

        private static void AddTileIfValid(TileZone expectedZone, WorldTile tile, List<WorldTile> list)
        {
            if (tile == null || tile.zone != expectedZone || tile.Type == null || !tile.Type.ground || list.Contains(tile))
            {
                return;
            }

            list.Add(tile);
        }

        private static Actor SpawnAdult(string actorAssetId, WorldTile tile)
        {
            Actor actor = World.world?.units?.spawnNewUnit(actorAssetId, tile, pSpawnSound: false, pMiracleSpawn: true, pSpawnHeight: 6f, pSubspecies: null, pGiveOwnerlessItems: true, pAdultAge: true);
            if (actor == null)
            {
                return null;
            }

            actor.data.age_overgrowth = AutoPanConstants.FixedAdultAge;
            return actor;
        }

        private static int StableIndex(string input, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int hash = (input ?? string.Empty).GetHashCode();
            return (int)((uint)hash % (uint)count);
        }

        private static string GetActorAssetId(AutoPanCommandType joinType)
        {
            return joinType switch
            {
                AutoPanCommandType.JoinHuman => "human",
                AutoPanCommandType.JoinOrc => "orc",
                AutoPanCommandType.JoinElf => "elf",
                AutoPanCommandType.JoinDwarf => "dwarf",
                _ => null
            };
        }

        private static string GetSpawnActorAssetId(Kingdom kingdom)
        {
            if (!string.IsNullOrWhiteSpace(kingdom?.data?.original_actor_asset))
            {
                return kingdom.data.original_actor_asset;
            }

            ActorAsset founderSpecies = kingdom?.getFounderSpecies();
            return founderSpecies?.id ?? "human";
        }

        private static string GetRaceId(AutoPanCommandType joinType)
        {
            return joinType switch
            {
                AutoPanCommandType.JoinHuman => "human",
                AutoPanCommandType.JoinOrc => "orc",
                AutoPanCommandType.JoinElf => "elf",
                AutoPanCommandType.JoinDwarf => "dwarf",
                _ => string.Empty
            };
        }

        private static string GetRaceText(AutoPanCommandType joinType)
        {
            return joinType switch
            {
                AutoPanCommandType.JoinHuman => "人类",
                AutoPanCommandType.JoinOrc => "兽人",
                AutoPanCommandType.JoinElf => "精灵",
                AutoPanCommandType.JoinDwarf => "矮人",
                _ => "未知"
            };
        }

        private static void EnsureKingdomStateInitialized(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return;
            }

            kingdom.data.get(AutoPanConstants.KeyTreasury, out int treasury, int.MinValue);
            if (treasury == int.MinValue)
            {
                kingdom.data.set(AutoPanConstants.KeyTreasury, AutoPanConfigHooks.InitialTreasury);
            }

            kingdom.data.get(AutoPanConstants.KeyLevel, out int level, int.MinValue);
            if (level == int.MinValue)
            {
                kingdom.data.set(AutoPanConstants.KeyLevel, AutoPanConfigHooks.InitialLevel);
            }

            kingdom.data.get(AutoPanConstants.KeyGatherSpiritUntilYear, out int gatherSpiritUntilYear, int.MinValue);
            if (gatherSpiritUntilYear == int.MinValue)
            {
                kingdom.data.set(AutoPanConstants.KeyGatherSpiritUntilYear, 0);
            }

            kingdom.data.get(AutoPanConstants.KeyOccupationPolicy, out string occupationPolicy, string.Empty);
            if (string.IsNullOrWhiteSpace(occupationPolicy))
            {
                kingdom.data.set(AutoPanConstants.KeyOccupationPolicy, AutoPanConstants.OccupationPolicyOpen);
            }

            kingdom.data.get(AutoPanConstants.KeyMilitiaUntilYear, out int militiaUntilYear, int.MinValue);
            if (militiaUntilYear == int.MinValue)
            {
                kingdom.data.set(AutoPanConstants.KeyMilitiaUntilYear, 0);
            }
        }
    }
}
