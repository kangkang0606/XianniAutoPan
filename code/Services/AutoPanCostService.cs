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
            return 180 + (realmIndex + 1) * 90;
        }

        /// <summary>
        /// 获取古神直接升星成本，随当前星级递增。
        /// </summary>
        public static int GetAncientStageUpCost(Actor actor)
        {
            int stage = Math.Max(1, XianniAutoPanApi.GetAncientStage(actor));
            return 170 + stage * 110;
        }

        /// <summary>
        /// 获取妖兽直接升阶成本，随当前阶级递增。
        /// </summary>
        public static int GetBeastStageUpCost(Actor actor)
        {
            int stage = Math.Max(1, XianniAutoPanApi.GetBeastStage(actor));
            return 160 + stage * 100;
        }

        /// <summary>
        /// 获取血脉创立成本，随目标强度提升。
        /// </summary>
        public static int GetBloodlineCreateCost(Actor actor)
        {
            return 320 + GetActorStageValue(actor) * 140;
        }

        /// <summary>
        /// 获取削减灵气成本。
        /// </summary>
        public static int GetAuraSabotageCost(int amount)
        {
            return Math.Max(80, (int)Math.Ceiling(Math.Max(1, amount) * 0.35f));
        }

        /// <summary>
        /// 获取斩首成本。
        /// </summary>
        public static int GetAssassinateCost(Actor actor)
        {
            return 280 + GetActorStageValue(actor) * 120;
        }

        /// <summary>
        /// 获取诅咒若干目标的成本。
        /// </summary>
        public static int GetCurseCost(int targetCount)
        {
            return Math.Max(60, Math.Max(1, targetCount) * 70);
        }

        /// <summary>
        /// 获取祝福若干目标的成本。
        /// </summary>
        public static int GetBlessCost(int targetCount)
        {
            return Math.Max(40, Math.Max(1, targetCount) * 50);
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
            return 90 + (realmIndex + 1) * 35 * safeLevels;
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
