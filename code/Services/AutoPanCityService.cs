using System;
using System.Collections.Generic;
using System.Linq;
using XianniAutoPan.Model;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 提供城市列表、征兵、快速成年、移交城市与军备发放能力。
    /// </summary>
    internal static class AutoPanCityService
    {
        private static readonly string[] WeaponPrefixes = { "sword", "axe", "hammer", "spear", "bow" };

        /// <summary>
        /// 构建当前国家的城市列表文本。
        /// </summary>
        public static string BuildCityListText(Kingdom kingdom)
        {
            List<City> cities = GetOwnedCities(kingdom);
            if (cities.Count == 0)
            {
                return "当前国家没有可管理的城市。";
            }

            List<string> lines = new List<string> { $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 城市列表：" };
            for (int index = 0; index < cities.Count; index++)
            {
                City city = cities[index];
                city.checkArmyExistence();
                int armyCount = city.hasArmy() ? CountAliveArmyUnits(city.getArmy()) : 0;
                string capitalText = city.isCapitalCity() ? "首都" : "分城";
                lines.Add($"{index + 1}. {FormatCityLabel(city)}，{capitalText}，人口 {city.getPopulationPeople()}/{city.getPopulationMaximum()}，战士 {city.countWarriors()}，军队 {armyCount}。");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 构建单座城市的详细信息。
        /// </summary>
        public static string BuildCityInfoText(City city)
        {
            if (city == null || !city.isAlive())
            {
                return "目标城市不存在或已失效。";
            }

            city.checkArmyExistence();
            string leaderName = city.hasLeader() ? city.leader.getName() : "无";
            string armyText = city.hasArmy() ? $"{city.getArmy().name} / {CountAliveArmyUnits(city.getArmy())} 人" : "无";
            return $"{FormatCityLabel(city)}：所属 {AutoPanKingdomService.FormatKingdomLabel(city.kingdom)}，类型 {(city.isCapitalCity() ? "首都" : "分城")}，人口 {city.getPopulationPeople()}/{city.getPopulationMaximum()}，战士 {city.countWarriors()}，军队 {armyText}，建筑 {city.buildings.Count}，领袖 {leaderName}。";
        }

        /// <summary>
        /// 解析当前国家名下的城市，支持“城市名 [cityId]”稳定定位。
        /// </summary>
        public static bool TryResolveOwnedCity(Kingdom kingdom, string rawCityName, out City city, out string error)
        {
            city = null;
            error = string.Empty;
            List<City> cities = GetOwnedCities(kingdom);
            if (cities.Count == 0)
            {
                error = "当前国家没有可操作的城市。";
                return false;
            }

            if (TryExtractTaggedId(rawCityName, out long explicitCityId))
            {
                city = cities.FirstOrDefault(item => item.getID() == explicitCityId);
                if (city != null)
                {
                    return true;
                }

                error = $"找不到 cityId={explicitCityId} 对应的本国城市。";
                return false;
            }

            string expected = NormalizeName(rawCityName);
            List<City> exactMatches = cities.Where(item => NormalizeName(item.name) == expected).ToList();
            if (exactMatches.Count == 1)
            {
                city = exactMatches[0];
                return true;
            }

            if (exactMatches.Count > 1)
            {
                error = "城市名重复，请改用“城市名 [cityId]”：" + string.Join("，", exactMatches.Select(FormatCityLabel).ToArray());
                return false;
            }

            List<City> partialMatches = cities.Where(item =>
            {
                string current = NormalizeName(item.name);
                return current.Contains(expected) || expected.Contains(current);
            }).ToList();
            if (partialMatches.Count == 1)
            {
                city = partialMatches[0];
                return true;
            }

            if (partialMatches.Count > 1)
            {
                error = "城市名匹配到多个结果，请改用“城市名 [cityId]”：" + string.Join("，", partialMatches.Select(FormatCityLabel).ToArray());
                return false;
            }

            error = "找不到目标城市。当前可选城市：" + string.Join("，", cities.Select(FormatCityLabel).ToArray());
            return false;
        }

        /// <summary>
        /// 将城市或全城内的未成年单位强制成年。
        /// </summary>
        public static bool TryFastAdult(Kingdom kingdom, string rawCityName, out string message)
        {
            message = string.Empty;
            List<Actor> targets = new List<Actor>();
            if (IsAllCitiesToken(rawCityName))
            {
                foreach (City city in GetOwnedCities(kingdom))
                {
                    CollectGrowTargets(city, targets);
                }
            }
            else
            {
                if (!TryResolveOwnedCity(kingdom, rawCityName, out City city, out string error))
                {
                    message = error;
                    return false;
                }

                CollectGrowTargets(city, targets);
            }

            if (targets.Count == 0)
            {
                message = "当前没有可快速成年的未成年单位。";
                return false;
            }

            int cost = targets.Count * AutoPanConstants.FastAdultCostPerUnit;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            int promoted = 0;
            foreach (Actor actor in targets)
            {
                bool wasBaby = actor.isBaby();
                actor.data.age_overgrowth = Math.Max(actor.data.age_overgrowth, AutoPanConstants.FixedAdultAge);
                actor.calcAgeStates();
                if (wasBaby && actor.isAdult())
                {
                    actor.eventBecomeAdult();
                    promoted++;
                }
            }

            if (promoted <= 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                message = "快速成年没有生效，请稍后再试。";
                return false;
            }

            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            string scopeText = IsAllCitiesToken(rawCityName) ? "全城" : rawCityName;
            message = $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 已对 {scopeText} 完成快速成年，共 {promoted} 人，消耗 {cost} 金币。";
            return true;
        }

        /// <summary>
        /// 从指定城市征集军队。
        /// </summary>
        public static bool TryConscriptArmy(Kingdom kingdom, string rawCityName, int requestedCount, out string message)
        {
            message = string.Empty;
            if (!TryResolveOwnedCity(kingdom, rawCityName, out City city, out string error))
            {
                message = error;
                return false;
            }

            List<Actor> candidates = city.units
                .Where(IsEligibleConscript)
                .Where(city.checkCanMakeWarrior)
                .ToList();
            if (candidates.Count == 0)
            {
                message = $"{FormatCityLabel(city)} 当前没有可征集的成年平民。";
                return false;
            }

            int recruitCount = requestedCount <= 0 ? candidates.Count : Math.Min(requestedCount, candidates.Count);
            int cost = recruitCount * AutoPanConstants.ConscriptCostPerUnit;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out string spendError))
            {
                message = spendError;
                return false;
            }

            List<Actor> recruited = new List<Actor>();
            for (int index = 0; index < recruitCount; index++)
            {
                Actor actor = candidates[index];
                if (!city.checkCanMakeWarrior(actor))
                {
                    continue;
                }

                city.makeWarrior(actor);
                recruited.Add(actor);
            }

            if (recruited.Count == 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                message = $"{FormatCityLabel(city)} 本次没有成功征集任何士兵。";
                return false;
            }

            city.checkArmyExistence();
            if (!city.hasArmy() && city.hasAnyWarriors())
            {
                World.world.armies.newArmy(recruited[0], city);
            }

            if (city.hasArmy())
            {
                Army army = city.getArmy();
                foreach (Actor actor in recruited)
                {
                    if (!actor.hasArmy())
                    {
                        actor.setArmy(army);
                    }
                }
                army.checkCity();
            }

            message = $"{FormatCityLabel(city)} 已征集 {recruited.Count} 名士兵，消耗 {cost} 金币，当前军队人数 {GetArmySizeText(city)}。";
            return true;
        }

        /// <summary>
        /// 将非首都城市移交给其他国家。
        /// </summary>
        public static bool TryTransferCity(Kingdom kingdom, string rawCityName, string rawTargetKingdomName, out string message)
        {
            message = string.Empty;
            if (!TryResolveOwnedCity(kingdom, rawCityName, out City city, out string cityError))
            {
                message = cityError;
                return false;
            }

            if (city.isCapitalCity())
            {
                message = "首都不能移交给其他国家。";
                return false;
            }

            if (!AutoPanKingdomService.TryResolveKingdom(rawTargetKingdomName, out Kingdom targetKingdom, out string targetError))
            {
                message = targetError;
                return false;
            }

            if (targetKingdom == kingdom)
            {
                message = "不能把城市移交给自己的国家。";
                return false;
            }

            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.TransferCityCost, out string spendError))
            {
                message = spendError;
                return false;
            }

            city.joinAnotherKingdom(targetKingdom);
            city.checkArmyExistence();
            if (city.hasArmy())
            {
                city.getArmy().checkCity();
            }

            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            AutoPanKingdomService.ClearSnapshotCache(targetKingdom.getID());
            message = $"{FormatCityLabel(city)} 已移交给 {AutoPanKingdomService.FormatKingdomLabel(targetKingdom)}，消耗 {AutoPanConstants.TransferCityCost} 金币。";
            return true;
        }

        /// <summary>
        /// 按档位为指定城市的军队批量更换整套装备。
        /// </summary>
        public static bool TryEquipArmy(Kingdom kingdom, string rawCityName, string tierText, int requestedCount, out string message)
        {
            message = string.Empty;
            if (!TryResolveOwnedCity(kingdom, rawCityName, out City city, out string cityError))
            {
                message = cityError;
                return false;
            }

            city.checkArmyExistence();
            if (!city.hasArmy())
            {
                message = $"{FormatCityLabel(city)} 当前没有军队。";
                return false;
            }

            if (!TryResolveEquipmentTier(tierText, out string tierSuffix, out int costPerUnit))
            {
                message = "未知军备档位，只支持：铜 / 青铜 / 白银 / 铁 / 钢 / 秘银 / 精金。";
                return false;
            }

            List<Actor> soldiers = city.getArmy().units
                .Where(actor => actor != null && actor.isAlive())
                .ToList();
            if (soldiers.Count == 0)
            {
                message = $"{FormatCityLabel(city)} 当前没有可装备的存活士兵。";
                return false;
            }

            int equipCount = requestedCount <= 0 ? soldiers.Count : Math.Min(requestedCount, soldiers.Count);
            int totalCost = equipCount * costPerUnit;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, totalCost, out string spendError))
            {
                message = spendError;
                return false;
            }

            List<string> fullSetIds = BuildEquipmentSetIds(tierSuffix);
            if (fullSetIds.Count == 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, totalCost);
                message = $"当前世界缺少 {tierText} 档位装备资源，无法发放军备。";
                return false;
            }

            int equipped = 0;
            for (int index = 0; index < equipCount; index++)
            {
                Actor soldier = soldiers[index];
                if (!TryEquipFullSet(city, soldier, kingdom, fullSetIds, tierSuffix))
                {
                    continue;
                }

                equipped++;
            }

            if (equipped == 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, totalCost);
                message = $"{FormatCityLabel(city)} 本次没有成功发放任何整套军备。";
                return false;
            }

            int refunded = totalCost - equipped * costPerUnit;
            if (refunded > 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, refunded);
            }

            message = $"{FormatCityLabel(city)} 已发放 {tierText} 整套军备 {equipped} 套，消耗 {equipped * costPerUnit} 金币。";
            return true;
        }

        private static string GetArmySizeText(City city)
        {
            if (city == null || !city.hasArmy())
            {
                return "0";
            }

            return CountAliveArmyUnits(city.getArmy()).ToString();
        }

        private static int CountAliveArmyUnits(Army army)
        {
            if (army == null)
            {
                return 0;
            }

            return army.units.Count(unit => unit != null && unit.isAlive());
        }

        private static List<string> BuildEquipmentSetIds(string tierSuffix)
        {
            List<string> itemIds = new List<string>
            {
                $"helmet_{tierSuffix}",
                $"armor_{tierSuffix}",
                $"boots_{tierSuffix}",
                $"ring_{tierSuffix}",
                $"amulet_{tierSuffix}",
                $"{WeaponPrefixes[Randy.randomInt(0, WeaponPrefixes.Length - 1)]}_{tierSuffix}"
            };

            if (itemIds.Any(itemId => AssetManager.items.get(itemId) == null))
            {
                return new List<string>();
            }

            return itemIds;
        }

        private static bool TryEquipFullSet(City city, Actor soldier, Kingdom kingdom, List<string> setIds, string tierSuffix)
        {
            if (city == null || soldier == null || !soldier.isAlive() || !soldier.understandsHowToUseItems())
            {
                return false;
            }

            List<string> itemIds = new List<string>(setIds);
            itemIds[itemIds.Count - 1] = $"{WeaponPrefixes[Randy.randomInt(0, WeaponPrefixes.Length - 1)]}_{tierSuffix}";
            foreach (string itemId in itemIds)
            {
                EquipmentAsset asset = AssetManager.items.get(itemId);
                if (asset == null)
                {
                    return false;
                }

                Item item = World.world.items.generateItem(asset, kingdom, "自动盘", 1, soldier, 0, pByPlayer: true);
                if (item == null)
                {
                    return false;
                }

                ActorEquipmentSlot slot = soldier.equipment.getSlot(asset.equipment_type);
                if (!slot.isEmpty())
                {
                    Item previous = slot.getItem();
                    slot.takeAwayItem();
                    if (previous != null)
                    {
                        city.tryToPutItem(previous);
                    }
                }

                slot.setItem(item, soldier);
            }

            soldier.setStatsDirty();
            return true;
        }

        private static bool TryResolveEquipmentTier(string tierText, out string tierSuffix, out int costPerUnit)
        {
            tierSuffix = string.Empty;
            costPerUnit = 0;
            switch ((tierText ?? string.Empty).Trim())
            {
                case "铜":
                    tierSuffix = "copper";
                    costPerUnit = AutoPanConstants.EquipCopperCostPerUnit;
                    return true;
                case "青铜":
                    tierSuffix = "bronze";
                    costPerUnit = AutoPanConstants.EquipBronzeCostPerUnit;
                    return true;
                case "白银":
                    tierSuffix = "silver";
                    costPerUnit = AutoPanConstants.EquipSilverCostPerUnit;
                    return true;
                case "铁":
                    tierSuffix = "iron";
                    costPerUnit = AutoPanConstants.EquipIronCostPerUnit;
                    return true;
                case "钢":
                    tierSuffix = "steel";
                    costPerUnit = AutoPanConstants.EquipSteelCostPerUnit;
                    return true;
                case "秘银":
                    tierSuffix = "mythril";
                    costPerUnit = AutoPanConstants.EquipMythrilCostPerUnit;
                    return true;
                case "精金":
                    tierSuffix = "adamantine";
                    costPerUnit = AutoPanConstants.EquipAdamantineCostPerUnit;
                    return true;
                default:
                    return false;
            }
        }

        private static void CollectGrowTargets(City city, List<Actor> result)
        {
            if (city == null || result == null)
            {
                return;
            }

            foreach (Actor actor in city.units)
            {
                if (actor != null && actor.isAlive() && actor.isBaby())
                {
                    result.Add(actor);
                }
            }
        }

        private static bool IsEligibleConscript(Actor actor)
        {
            return actor != null
                && actor.isAlive()
                && actor.isAdult()
                && !actor.isProfession(UnitProfession.Warrior)
                && !actor.isKing()
                && !actor.isCityLeader();
        }

        private static bool IsAllCitiesToken(string rawCityName)
        {
            string text = (rawCityName ?? string.Empty).Trim();
            return text == "全城" || text == "全部城市" || text == "所有城市";
        }

        private static List<City> GetOwnedCities(Kingdom kingdom)
        {
            if (kingdom == null || !kingdom.isAlive())
            {
                return new List<City>();
            }

            return kingdom.getCities()
                .Where(city => city != null && city.isAlive())
                .OrderByDescending(city => city.isCapitalCity())
                .ThenBy(city => city.name)
                .ThenBy(city => city.getID())
                .ToList();
        }

        private static string FormatCityLabel(City city)
        {
            return city == null ? "未知城市" : $"{city.name} [{city.getID()}]";
        }

        private static string NormalizeName(string rawText)
        {
            return new string((rawText ?? string.Empty).Trim().Where(ch => !char.IsWhiteSpace(ch)).ToArray());
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
    }
}
