using HarmonyLib;
using UnityEngine;
using XianniAutoPan.Commands;
using XianniAutoPan.Model;
using XianniAutoPan.Services;
using xn.api;

namespace XianniAutoPan
{
    /// <summary>
    /// 国家灭亡后的绑定清理补丁。
    /// </summary>
    [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
    internal static class AutoPanKingdomRemovePatch
    {
        /// <summary>
        /// 国家销毁后清理绑定。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(Kingdom pKingdom)
        {
            if (pKingdom == null)
            {
                return;
            }

            foreach (AutoPanBindingRecord binding in AutoPanStateRepository.GetBindingsByKingdomId(pKingdom.getID()))
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.UserId))
                {
                    continue;
                }

                AutoPanNotificationService.NotifyKingdomDestroyed(binding, pKingdom);
            }

            AutoPanStateRepository.ClearBindingByKingdomId(pKingdom.getID());
            AutoPanKingdomService.ClearRuntimeEconomyState(pKingdom);
            AutoPanLogService.Info($"国家灭亡：{pKingdom.name}，已清理相关玩家绑定。");
        }
    }

    /// <summary>
    /// 在原版新世界收尾加载完成后执行新局 UI 隐藏。
    /// </summary>
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.finishingUpLoading))]
    internal static class AutoPanMapFinishingUpLoadingPatch
    {
        /// <summary>
        /// 原版会在此处重新打开主 UI，自动盘需要在其后隐藏权能条和清理交互。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            AutoPanRoundUiService.ApplyPendingHideAfterWorldLoad();
        }
    }

    /// <summary>
    /// 战争结束奖励补丁。
    /// </summary>
    [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
    internal static class AutoPanWarEndPatch
    {
        /// <summary>
        /// 记录战前状态，避免重复结算已结束战争。
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(War pWar, out bool __state)
        {
            __state = pWar != null && pWar.isAlive() && !pWar.hasEnded();
        }

        /// <summary>
        /// 战争真正结束后发放奖励。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(War pWar, WarWinner pWinner, bool __state)
        {
            if (!__state)
            {
                return;
            }

            AutoPanKingdomService.HandleWarEnded(pWar, pWinner);
        }
    }

    /// <summary>
    /// 让开启全民皆兵的国家按原版愤怒村民法则参与战争。
    /// </summary>
    [HarmonyPatch(typeof(BaseSimObject), nameof(BaseSimObject.canAttackTarget))]
    internal static class AutoPanMilitiaCivilianAttackPatch
    {
        /// <summary>
        /// 仅在原版平民攻击限制挡住目标时，给全民皆兵国家一个局部放行。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(BaseSimObject __instance, BaseSimObject pTarget, bool pCheckForFactions, bool pAttackBuildings, ref bool __result)
        {
            if (__result || !pCheckForFactions)
            {
                return;
            }

            if (AutoPanKingdomService.ShouldAllowMilitiaCivilianAttack(__instance, pTarget, pAttackBuildings))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 战争动员期间允许城市军队低于原版满编阈值也执行进攻目标。
    /// </summary>
    [HarmonyPatch(typeof(City), nameof(City.isOkToSendArmy))]
    internal static class AutoPanMobilizedArmyDeparturePatch
    {
        /// <summary>
        /// 动员军令只对已设置敌方攻城目标的城市生效。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(City __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (AutoPanKingdomService.ShouldAllowMobilizedArmyDeparture(__instance))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 接管城市占领结算：坚守城池时禁止占领并摧毁城市，开放占领时按被占城市发放补助。
    /// </summary>
    [HarmonyPatch(typeof(City), nameof(City.finishCapture))]
    internal static class AutoPanCityCapturePatch
    {
        private sealed class CaptureState
        {
            /// <summary>
            /// 被占领前的城市所属国家。
            /// </summary>
            public Kingdom PreviousKingdom;

            /// <summary>
            /// 城市名称快照。
            /// </summary>
            public string CityName;

            /// <summary>
            /// 被攻方是否采用开放占领政策。
            /// </summary>
            public bool WasOpenOccupation;
        }

        /// <summary>
        /// 坚守城池政策下，城市不会变更归属，攻破时直接摧毁。
        /// </summary>
        [HarmonyPrefix]
        private static bool Prefix(City __instance, Kingdom pNewKingdom, out CaptureState __state)
        {
            __state = null;
            Kingdom previousKingdom = __instance?.kingdom;
            if (__instance == null || previousKingdom == null || !previousKingdom.isAlive())
            {
                return true;
            }

            __state = new CaptureState
            {
                PreviousKingdom = previousKingdom,
                CityName = __instance.name,
                WasOpenOccupation = AutoPanKingdomService.IsOpenOccupationPolicy(previousKingdom)
            };

            if (!AutoPanKingdomService.IsDefendCityPolicy(previousKingdom) || World.world?.cities == null || World.world.cities.isLocked())
            {
                return true;
            }

            string attackerText = pNewKingdom != null ? AutoPanKingdomService.FormatKingdomLabel(pNewKingdom) : "敌军";
            string text = $"{AutoPanKingdomService.FormatKingdomLabel(previousKingdom)} 采用坚守城池政策，{__instance.name} 被 {attackerText} 攻破后不会被占领，已被摧毁。";
            __instance.clearCapture();
            World.world.cities.removeObject(__instance);

            // 最后一个城市被摧毁时，杀掉该国家所有单位防止无家可归者重建城市
            if (previousKingdom.countCities() <= 0)
            {
                text += $" {previousKingdom.name} 已无城可守，全体国民覆灭。";
                AutoPanKingdomService.KillAllUnits(previousKingdom);
            }

            XianniAutoPanApi.Broadcast(text);
            AutoPanNotificationService.NotifyKingdomOwners(previousKingdom, text);
            AutoPanLogService.Info(text);
            return false;
        }

        /// <summary>
        /// 原版完成占领后，对开放占领政策国家发放逐城补助。
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(City __instance, CaptureState __state)
        {
            if (__state == null || __instance == null || !__state.WasOpenOccupation)
            {
                return;
            }

            Kingdom newKingdom = __instance.kingdom;
            if (newKingdom == null || newKingdom == __state.PreviousKingdom)
            {
                return;
            }

            AutoPanKingdomService.GrantOccupationSubsidy(__state.PreviousKingdom, newKingdom, __state.CityName);
        }
    }

    /// <summary>
    /// 记录国家铭牌屏幕坐标，供聊天气泡锚定。
    /// </summary>
    [HarmonyPatch(typeof(NameplateText), nameof(NameplateText.showTextKingdom))]
    internal static class AutoPanKingdomNameplatePatch
    {
        /// <summary>
        /// 国家铭牌刷新后记录其最终屏幕位置。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(Kingdom pMetaObject, NameplateText __instance, Vector2 pPosition)
        {
            if (pMetaObject == null || __instance == null)
            {
                return;
            }

            AutoPanKingdomSpeechService.RecordNameplatePosition(pMetaObject, __instance.getLastScreenPosition());
        }
    }

    /// <summary>
    /// 在原版铭牌完成刷新后稳定更新自动盘聊天气泡位置。
    /// </summary>
    [HarmonyPatch(typeof(NameplateManager), nameof(NameplateManager.update))]
    internal static class AutoPanNameplateManagerUpdatePatch
    {
        /// <summary>
        /// 原版铭牌布局完成后刷新气泡，避免高倍速下与铭牌刷新顺序抢位置。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            AutoPanKingdomSpeechService.UpdateAnchoredVisuals();
        }
    }

    /// <summary>
    /// 兼容自动盘自定义倍速下的原生“加速一档”逻辑。
    /// </summary>
    [HarmonyPatch(typeof(WorldTimeScaleAsset), nameof(WorldTimeScaleAsset.getNext))]
    internal static class AutoPanCustomWorldSpeedNextPatch
    {
        /// <summary>
        /// 当当前速度为自动盘自定义速度时，返回最接近原生档位的下一档。
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(WorldTimeScaleAsset __instance, bool pCycle, ref WorldTimeScaleAsset __result)
        {
            if (!AutoPanCommandExecutor.IsCustomWorldSpeedAsset(__instance))
            {
                return true;
            }

            __result = AutoPanCommandExecutor.GetAdjacentNativeWorldSpeedAsset(next: true, cycle: pCycle);
            return false;
        }
    }

    /// <summary>
    /// 兼容自动盘自定义倍速下的原生“减速一档”逻辑。
    /// </summary>
    [HarmonyPatch(typeof(WorldTimeScaleAsset), nameof(WorldTimeScaleAsset.getPrevious))]
    internal static class AutoPanCustomWorldSpeedPreviousPatch
    {
        /// <summary>
        /// 当当前速度为自动盘自定义速度时，返回最接近原生档位的上一档。
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(WorldTimeScaleAsset __instance, bool pCycle, ref WorldTimeScaleAsset __result)
        {
            if (!AutoPanCommandExecutor.IsCustomWorldSpeedAsset(__instance))
            {
                return true;
            }

            __result = AutoPanCommandExecutor.GetAdjacentNativeWorldSpeedAsset(next: false, cycle: pCycle);
            return false;
        }
    }
}
