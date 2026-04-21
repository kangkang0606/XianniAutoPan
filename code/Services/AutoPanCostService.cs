using System;
using System.Collections.Generic;
using System.Linq;
using xn.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 统一管理自动盘各类成长与互动动作的金币消耗。
    /// </summary>
    internal static class AutoPanCostService
    {
        /// <summary>
        /// 获取修士直接升境成本，随当前境界递增。
        /// </summary>
        public static int GetCultivatorRealmUpCost(Actor actor)
        {
            int realmIndex = Math.Max(0, XianniAutoPanApi.GetCultivatorRealmIndex(actor));
            return AutoPanConfigHooks.CultivatorRealmUpBaseCost + (realmIndex + 1) * AutoPanConfigHooks.CultivatorRealmUpStepCost;
        }

        /// <summary>
        /// 获取古神直接升星成本，随当前星级递增。
        /// </summary>
        public static int GetAncientStageUpCost(Actor actor)
        {
            int stage = Math.Max(1, XianniAutoPanApi.GetAncientStage(actor));
            return AutoPanConfigHooks.AncientStageUpBaseCost + stage * AutoPanConfigHooks.AncientStageUpStepCost;
        }

        /// <summary>
        /// 获取妖兽直接升阶成本，随当前阶级递增。
        /// </summary>
        public static int GetBeastStageUpCost(Actor actor)
        {
            int stage = Math.Max(1, XianniAutoPanApi.GetBeastStage(actor));
            return AutoPanConfigHooks.BeastStageUpBaseCost + stage * AutoPanConfigHooks.BeastStageUpStepCost;
        }

        /// <summary>
        /// 获取血脉创立成本，随目标强度提升。
        /// </summary>
        public static int GetBloodlineCreateCost(Actor actor)
        {
            return AutoPanConfigHooks.BloodlineCreateBaseCost + GetActorStageValue(actor) * AutoPanConfigHooks.BloodlineCreateStageStepCost;
        }

        /// <summary>
        /// 获取削减灵气成本。
        /// </summary>
        public static int GetAuraSabotageCost(int amount)
        {
            int scaled = (int)Math.Ceiling(Math.Max(1, amount) * AutoPanConfigHooks.AuraSabotageCostPer100Aura / 100f);
            return Math.Max(AutoPanConfigHooks.AuraSabotageMinCost, scaled);
        }

        /// <summary>
        /// 获取斩首成本。
        /// </summary>
        public static int GetAssassinateCost(Actor actor)
        {
            return AutoPanConfigHooks.AssassinateBaseCost + GetActorStageValue(actor) * AutoPanConfigHooks.AssassinateStageStepCost;
        }

        /// <summary>
        /// 获取诅咒若干目标的成本。
        /// </summary>
        public static int GetCurseCost(int targetCount)
        {
            return Math.Max(AutoPanConfigHooks.CurseBaseCost, AutoPanConfigHooks.CurseBaseCost + Math.Max(1, targetCount) * AutoPanConfigHooks.CurseCostPerTarget);
        }

        /// <summary>
        /// 获取祝福若干目标的成本。
        /// </summary>
        public static int GetBlessCost(int targetCount)
        {
            return Math.Max(AutoPanConfigHooks.BlessBaseCost, AutoPanConfigHooks.BlessBaseCost + Math.Max(1, targetCount) * AutoPanConfigHooks.BlessCostPerTarget);
        }

        /// <summary>
        /// 获取修士压境成本，按实际目标与下降境界递增。
        /// </summary>
        public static int GetCultivatorSuppressCost(IEnumerable<Actor> actors, int levels)
        {
            if (actors == null)
            {
                return 0;
            }

            int cost = 0;
            int safeLevels = Math.Max(1, levels);
            foreach (Actor actor in actors.Where(item => item != null))
            {
                cost += GetCultivatorSuppressUnitCost(actor, safeLevels);
            }

            return Math.Max(0, cost);
        }

        /// <summary>
        /// 获取单个修士压境成本。
        /// </summary>
        public static int GetCultivatorSuppressUnitCost(Actor actor, int levels)
        {
            int realmIndex = Math.Max(0, XianniAutoPanApi.GetCultivatorRealmIndex(actor));
            int safeLevels = Math.Max(1, levels);
            return AutoPanConfigHooks.CultivatorSuppressBaseCost + (realmIndex + 1) * AutoPanConfigHooks.CultivatorSuppressStageStepCost * safeLevels;
        }

        /// <summary>
        /// 获取古神降星成本，使用独立的古神降星基础与阶梯配置。
        /// </summary>
        public static int GetAncientSuppressUnitCost(Actor actor, int levels)
        {
            int stage = Math.Max(1, XianniAutoPanApi.GetAncientStage(actor));
            int safeLevels = Math.Max(1, levels);
            return AutoPanConfigHooks.AncientSuppressBaseCost + stage * AutoPanConfigHooks.AncientSuppressStageStepCost * safeLevels;
        }

        /// <summary>
        /// 获取妖兽降阶成本，使用独立的妖兽降阶基础与阶梯配置。
        /// </summary>
        public static int GetBeastSuppressUnitCost(Actor actor, int levels)
        {
            int stage = Math.Max(1, XianniAutoPanApi.GetBeastStage(actor));
            int safeLevels = Math.Max(1, levels);
            return AutoPanConfigHooks.BeastSuppressBaseCost + stage * AutoPanConfigHooks.BeastSuppressStageStepCost * safeLevels;
        }

        /// <summary>
        /// 获取单位当前用于成本计算的阶段值。
        /// </summary>
        public static int GetActorStageValue(Actor actor)
        {
            if (actor == null)
            {
                return 1;
            }

            int cultivatorRealm = XianniAutoPanApi.GetCultivatorRealmIndex(actor);
            if (cultivatorRealm >= 0)
            {
                return cultivatorRealm + 1;
            }

            int ancientStage = XianniAutoPanApi.GetAncientStage(actor);
            if (ancientStage > 0)
            {
                return ancientStage;
            }

            int beastStage = XianniAutoPanApi.GetBeastStage(actor);
            return beastStage > 0 ? beastStage : 1;
        }
    }
}
