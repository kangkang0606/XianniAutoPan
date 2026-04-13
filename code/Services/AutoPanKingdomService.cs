using System;
using System.Collections.Generic;
using System.Linq;
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
                    SetTreasury(kingdom, AutoPanConstants.InitialTreasury);
                    SetLevel(kingdom, AutoPanConstants.InitialLevel);
                    kingdom.data.set(AutoPanConstants.KeyOwnerUserId, userId);
                    kingdom.data.set(AutoPanConstants.KeyOwnerName, playerName);
                    AutoPanStateRepository.BindPlayerToKingdom(userId, playerName, raceId, kingdom);
                    ClearSnapshotCache(kingdom.getID());
                    XianniAutoPanApi.Broadcast($"{playerName} 以{raceText}建立了新的国家 {kingdom.name}");
                    message = $"加入成功：已为你创建 {raceText}国家 {kingdom.name}，初始国库 200，国家等级 1。";
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
                return AutoPanConstants.InitialTreasury;
            }

            EnsureKingdomStateInitialized(kingdom);
            kingdom.data.get(AutoPanConstants.KeyTreasury, out int treasury, AutoPanConstants.InitialTreasury);
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
                return AutoPanConstants.InitialLevel;
            }

            EnsureKingdomStateInitialized(kingdom);
            kingdom.data.get(AutoPanConstants.KeyLevel, out int level, AutoPanConstants.InitialLevel);
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
            kingdom.data.set(AutoPanConstants.KeyGatherSpiritUntilYear, baseYear + AutoPanConstants.GatherSpiritDurationYears);
        }

        /// <summary>
        /// 计算国家的有效灵气。
        /// </summary>
        public static int GetEffectiveAura(Kingdom kingdom)
        {
            int aura = XianniAutoPanApi.GetKingdomAura(kingdom);
            if (IsGatherSpiritActive(kingdom))
            {
                aura += kingdom.countCities() * AutoPanConstants.GatherSpiritAuraBonusPerCity;
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
            int income = 10 + 4 * cityCount + population / 20 + 3 * GetLevel(kingdom) + effectiveAura / 200;
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
                int treasury = AddTreasury(kingdom, income);
                ClearSnapshotCache(kingdom.getID());
                AutoPanLogService.Info($"年结算 Y{year}：{kingdom.name} +{income} 金币，当前国库 {treasury}");
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
            return $"{FormatKingdomLabel(kingdom)}：国库 {GetTreasury(kingdom)}，国家等级 {GetLevel(kingdom)}，修真国等级 {snapshot.XiuzhenguoLevel}，城市 {kingdom.countCities()}，人口 {kingdom.getPopulationTotal()}，灵气 {GetEffectiveAura(kingdom)}，年收入 {ComputeYearlyIncome(kingdom)}，聚灵剩余 {gatherSpiritRemain} 年。";
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
                lines.Add($"{i + 1}. {item.ActorName}，{stageLabel}，数值 {item.PowerValue}");
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
            string baseName = string.IsNullOrWhiteSpace(playerName) ? "无名国" : playerName.Trim();
            HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);
            if (World.world?.kingdoms != null)
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom != null && kingdom.isAlive() && kingdom.isCiv())
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
            XianniKingdomSnapshot snapshot = GetSnapshot(kingdom, forceRefresh: true);
            AutoPanAiRequestContext context = new AutoPanAiRequestContext
            {
                KingdomId = kingdom.getID(),
                KingdomName = kingdom.name,
                Treasury = GetTreasury(kingdom),
                NationLevel = GetLevel(kingdom),
                XiuzhenguoLevel = snapshot.XiuzhenguoLevel,
                AnnualIncome = ComputeYearlyIncome(kingdom),
                CityCount = kingdom.countCities(),
                Population = kingdom.getPopulationTotal(),
                TotalAura = GetEffectiveAura(kingdom),
                GatherSpiritActive = IsGatherSpiritActive(kingdom)
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
                if (other == null || !other.isAlive() || other == kingdom)
                {
                    continue;
                }

                if (!kingdom.isEnemy(other))
                {
                    context.CandidateKingdomNames.Add(FormatKingdomLabel(other));
                }
            }

            context.CultivatorChoices = snapshot.Cultivators.Select((item, index) => $"{index + 1}. {item.ActorName} / 境界索引{item.StageIndex} / 修为{item.PowerValue}").ToList();
            context.AncientChoices = snapshot.Ancients.Select((item, index) => $"{index + 1}. {item.ActorName} / {item.StageValue}星 / 古神之力{item.PowerValue}").ToList();
            context.BeastChoices = snapshot.Beasts.Select((item, index) => $"{index + 1}. {item.ActorName} / {item.StageValue}阶 / 妖力{item.PowerValue}").ToList();
            return context;
        }

        /// <summary>
        /// 处理战争结束后的国库奖励。
        /// </summary>
        public static void HandleWarEnded(War war, WarWinner winner)
        {
            if (war == null)
            {
                return;
            }

            Kingdom attacker = war.getMainAttacker();
            Kingdom defender = war.getMainDefender();
            if (winner == WarWinner.Attackers)
            {
                RewardWarResult(attacker, defender, isWinner: true);
                RewardWarResult(defender, attacker, isWinner: false);
            }
            else if (winner == WarWinner.Defenders)
            {
                RewardWarResult(defender, attacker, isWinner: true);
                RewardWarResult(attacker, defender, isWinner: false);
            }
        }

        private static void RewardWarResult(Kingdom target, Kingdom enemy, bool isWinner)
        {
            if (target == null || !target.isAlive() || !target.isCiv())
            {
                return;
            }

            EnsureKingdomStateInitialized(target);
            int reward = isWinner ? 80 + 20 * GetLevel(enemy) : 20;
            int treasury = AddTreasury(target, reward);
            string resultText = isWinner ? "战争获胜" : "战争失利补偿";
            XianniAutoPanApi.Broadcast($"{target.name} {resultText}，获得 {reward} 金币，当前国库 {treasury}");
            AutoPanLogService.Info($"{target.name} {resultText}，奖励 {reward} 金币。");
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
                kingdom.data.set(AutoPanConstants.KeyTreasury, AutoPanConstants.InitialTreasury);
            }

            kingdom.data.get(AutoPanConstants.KeyLevel, out int level, int.MinValue);
            if (level == int.MinValue)
            {
                kingdom.data.set(AutoPanConstants.KeyLevel, AutoPanConstants.InitialLevel);
            }
        }
    }
}
