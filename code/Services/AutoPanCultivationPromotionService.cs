using System;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 自动盘内部的升境兼容服务。
    /// 为避免自动盘编译时强依赖“已重编译的 xianni 公共 API”，这里内置一份稳定的 trait/阈值表。
    /// </summary>
    internal static class AutoPanCultivationPromotionService
    {
        private const string KeyXiuwei = "xn.stat.xiuwei";
        private const string KeyCultivationStop = "xn.cultivation.stop";
        private const string KeyAncientPower = "xn.stat.gushen_power";
        private const string KeyAncientStop = "xn.ancient.stop";
        private const string KeyBeastPower = "xn.stat.yaoli";
        private const string KeyBeastStop = "xn.beast.stop";
        private static readonly string[] RealmIds =
        {
            "realm_01_qi",
            "realm_02_foundation",
            "realm_03_core",
            "realm_04_nascent",
            "realm_05_deity",
            "realm_06_infantchg",
            "realm_07_wending",
            "realm_08_kuinie",
            "realm_09_jingnie",
            "realm_10_suinie",
            "realm_11_kongnie",
            "realm_12_kongling",
            "realm_13_kongxuan",
            "realm_14_gtianzun",
            "realm_15_half_tatian",
            "realm_16_tatian"
        };
        private static readonly string[] AncientStageIds =
        {
            "ancient_01_star",
            "ancient_02_star",
            "ancient_03_star",
            "ancient_04_star",
            "ancient_05_star",
            "ancient_06_star",
            "ancient_07_star",
            "ancient_08_star",
            "ancient_09_star",
            "ancient_10_star"
        };
        private static readonly string[] BeastStageIds =
        {
            "beast_01_stage",
            "beast_02_stage",
            "beast_03_stage",
            "beast_04_stage",
            "beast_05_stage",
            "beast_06_stage",
            "beast_07_stage",
            "beast_08_stage",
            "beast_09_stage",
            "beast_10_stage"
        };
        private static readonly long[] RealmThresholds =
        {
            100000L,
            1500000L,
            4000000L,
            9600000L,
            30000000L,
            80000000L,
            150000000L,
            250000000L,
            400000000L,
            600000000L,
            700000000L,
            800000000L,
            900000000L,
            980000000L,
            1200000000L,
            1500000000L
        };
        private static readonly int[] AncientBeastThresholds =
        {
            5000, 30000, 50000, 100000, 200000, 500000, 1000000, 1500000, 3000000, 5000000
        };

        /// <summary>
        /// 直接将修士提升一个境界。
        /// </summary>
        public static bool TryPromoteCultivatorRealm(Actor actor)
        {
            if (actor == null || !actor.isAlive())
            {
                return false;
            }

            int currentRealmIndex = GetTraitIndex(actor, RealmIds);
            if (currentRealmIndex < 0 || currentRealmIndex >= RealmIds.Length - 1)
            {
                return false;
            }

            int nextRealmIndex = currentRealmIndex + 1;
            ReplaceTraitSet(actor, RealmIds, nextRealmIndex);
            actor.data.set(KeyXiuwei, RealmThresholds[nextRealmIndex]);
            actor.data.set(KeyCultivationStop, 0);

            // 没有明确修炼路线时，默认补一条仙修路线，避免后续判定落空。
            if (!actor.hasTrait("path_01_demonic") && !actor.hasTrait("path_02_immortal"))
            {
                actor.addTrait("path_02_immortal");
            }

            return true;
        }

        /// <summary>
        /// 直接将修士设置到指定境界。
        /// </summary>
        public static bool TrySetCultivatorRealm(Actor actor, int targetRealmIndex)
        {
            if (actor == null || !actor.isAlive())
            {
                return false;
            }

            if (targetRealmIndex < 0 || targetRealmIndex >= RealmIds.Length)
            {
                return false;
            }

            ReplaceTraitSet(actor, RealmIds, targetRealmIndex);
            actor.data.set(KeyXiuwei, RealmThresholds[targetRealmIndex]);
            actor.data.set(KeyCultivationStop, 0);
            if (!actor.hasTrait("path_01_demonic") && !actor.hasTrait("path_02_immortal"))
            {
                actor.addTrait("path_02_immortal");
            }

            return true;
        }

        /// <summary>
        /// 直接将古神提升一星。
        /// </summary>
        public static bool TryPromoteAncientStage(Actor actor)
        {
            return TryPromoteStage(actor, AncientStageIds, KeyAncientPower, KeyAncientStop, "path_04_ancient");
        }

        /// <summary>
        /// 直接将妖兽提升一阶。
        /// </summary>
        public static bool TryPromoteBeastStage(Actor actor)
        {
            return TryPromoteStage(actor, BeastStageIds, KeyBeastPower, KeyBeastStop, "path_03_beast");
        }

        private static bool TryPromoteStage(Actor actor, string[] stageIds, string powerKey, string stopKey, string pathTraitId)
        {
            if (actor == null || !actor.isAlive())
            {
                return false;
            }

            int currentStageIndex = GetTraitIndex(actor, stageIds);
            if (currentStageIndex < 0 || currentStageIndex >= stageIds.Length - 1)
            {
                return false;
            }

            int nextStageIndex = currentStageIndex + 1;
            ReplaceTraitSet(actor, stageIds, nextStageIndex);
            if (!string.IsNullOrWhiteSpace(pathTraitId) && !actor.hasTrait(pathTraitId))
            {
                actor.addTrait(pathTraitId);
            }

            actor.data.get(powerKey, out long currentPower, 0L);
            actor.data.set(powerKey, Math.Max(currentPower, AncientBeastThresholds[nextStageIndex]));
            actor.data.set(stopKey, 0);
            return true;
        }

        private static int GetTraitIndex(Actor actor, string[] traitIds)
        {
            int foundIndex = -1;
            for (int i = 0; i < traitIds.Length; i++)
            {
                if (actor.hasTrait(traitIds[i]))
                {
                    foundIndex = i;
                }
            }

            return foundIndex;
        }

        private static void ReplaceTraitSet(Actor actor, string[] traitIds, int targetIndex)
        {
            for (int i = 0; i < traitIds.Length; i++)
            {
                actor.removeTrait(traitIds[i]);
            }

            int clampedIndex = Math.Max(0, Math.Min(traitIds.Length - 1, targetIndex));
            actor.addTrait(traitIds[clampedIndex]);
        }
    }
}
