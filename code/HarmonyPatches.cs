using ai.behaviours;
using HarmonyLib;
using System;
using System.Collections.Generic;
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
    /// 阻止原版外交法则让玩家绑定国家进入非自动盘战争。
    /// </summary>
    [HarmonyPatch(typeof(DiplomacyManager), nameof(DiplomacyManager.startWar))]
    internal static class AutoPanNativeDiplomacyWarPatch
    {
        /// <summary>
        /// 绑定国家只能通过自动盘指令进入战争；原版外交、叛乱、魔法等路径不应影响绑定国家。
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset, ref War __result)
        {
            if (!AutoPanDiplomacyGuardService.ShouldBlockNativeWar(pAttacker, pDefender, pAsset))
            {
                return true;
            }

            __result = null;
            return false;
        }
    }

    /// <summary>
    /// 阻止原版力量或外交逻辑强制绑定国家结盟。
    /// </summary>
    [HarmonyPatch(typeof(AllianceManager), nameof(AllianceManager.forceAlliance))]
    internal static class AutoPanNativeForceAlliancePatch
    {
        /// <summary>
        /// 原版强制结盟如果被阻止，需要直接返回 false，避免内部 newAlliance 被拦截后继续访问空联盟。
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Kingdom pKingdom1, Kingdom pKingdom2, ref bool __result)
        {
            if (pKingdom1 == null || pKingdom1.data == null || pKingdom2 == null || pKingdom2.data == null)
            {
                __result = false;
                return false;
            }

            if (!AutoPanDiplomacyGuardService.ShouldBlockNativeAlliance(pKingdom1, pKingdom2))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// 防止 WorldLog.logAllianceCreated 内第三方模组（如 Chinese_Name）索引越界导致崩溃。
    /// 联盟刚创建时 kingdoms_list 可能尚未填充，第三方 patch 访问会抛 ArgumentOutOfRangeException。
    /// </summary>
    [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logAllianceCreated))]
    internal static class AutoPanLogAllianceCreatedGuardPatch
    {
        private static int _suppressedCount;

        /// <summary>
        /// 只吞掉联盟创建日志链路中的异常，避免第三方命名模组读取未填充成员列表时刷屏。
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                _suppressedCount++;
                if (_suppressedCount == 1 || _suppressedCount % 100 == 0)
                {
                    AutoPanLogService.Info($"logAllianceCreated 异常已吞没 {_suppressedCount} 次：{__exception.GetType().Name}: {__exception.Message}");
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 原版联盟成员清理遇到已失效王国数据时只跳过异常，不改变正常退盟逻辑。
    /// </summary>
    [HarmonyPatch(typeof(Alliance), nameof(Alliance.leave))]
    internal static class AutoPanAllianceLeaveRuntimeGuardPatch
    {
        private static int _suppressedCount;

        /// <summary>
        /// 仅吞掉日志中出现的空引用异常，避免坏成员导致联盟更新中断。
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            if (!(__exception is NullReferenceException))
            {
                return __exception;
            }

            _suppressedCount++;
            if (_suppressedCount == 1 || _suppressedCount % 100 == 0)
            {
                AutoPanLogService.Info($"Alliance.leave 空引用异常已吞没 {_suppressedCount} 次：{__exception.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// 城市更新中若同帧移除城市导致枚举器失效，只跳过本帧剩余城市更新，下一帧自动恢复。
    /// </summary>
    [HarmonyPatch(typeof(CityManager), nameof(CityManager.update))]
    internal static class AutoPanCityManagerUpdateGuardPatch
    {
        private static int _suppressedCount;

        /// <summary>
        /// 仅吞掉集合枚举被修改这一类异常，其他城市更新异常继续交给游戏日志暴露。
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            bool isKnownCollectionMutation = __exception is InvalidOperationException
                && (__exception.Message ?? string.Empty).IndexOf("Collection was modified", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isKnownCollectionMutation)
            {
                return __exception;
            }

            _suppressedCount++;
            if (_suppressedCount == 1 || _suppressedCount % 100 == 0)
            {
                AutoPanLogService.Info($"CityManager.update 集合变更异常已吞没 {_suppressedCount} 次：{__exception.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// 阻止原版外交法则修改包含玩家绑定国家的联盟成员。
    /// </summary>
    [HarmonyPatch(typeof(Alliance), nameof(Alliance.join))]
    internal static class AutoPanNativeAllianceJoinPatch
    {
        /// <summary>
        /// 避免原版联盟剧情把绑定国家拉入或拖出自动盘外的联盟关系。
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Alliance __instance, Kingdom pKingdom, ref bool __result)
        {
            if (!AutoPanDiplomacyGuardService.ShouldBlockNativeAllianceMembershipChange(__instance, pKingdom))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// 阻止原版外交法则让玩家绑定国家离开联盟。
    /// </summary>
    [HarmonyPatch(typeof(Alliance), nameof(Alliance.leave))]
    internal static class AutoPanNativeAllianceLeavePatch
    {
        /// <summary>
        /// 绑定国家退盟只允许走自动盘退盟指令。
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Alliance __instance, Kingdom pKingdom)
        {
            return !AutoPanDiplomacyGuardService.ShouldBlockNativeAllianceMembershipChange(__instance, pKingdom);
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
            bool wasLastCity = previousKingdom.countCities() <= 1;
            WorldTile originTile = __instance.getTile();
            List<Actor> displacedUnits = new List<Actor>();
            if (wasLastCity && World.world?.units != null)
            {
                foreach (Actor unit in World.world.units)
                {
                    if (unit != null && unit.isAlive() && unit.kingdom == previousKingdom)
                    {
                        displacedUnits.Add(unit);
                    }
                }
            }
            else
            {
                foreach (Actor unit in __instance.units)
                {
                    if (unit != null && unit.isAlive() && unit.kingdom == previousKingdom)
                    {
                        displacedUnits.Add(unit);
                    }
                }
            }

            __instance.clearCapture();
            World.world.cities.removeObject(__instance);

            int migrated = AutoPanKingdomService.RelocateDefendCitySurvivors(previousKingdom, pNewKingdom, displacedUnits, originTile);
            if (migrated > 0)
            {
                text += $" {migrated} 名无家可归者已迁入现有城市。";
            }

            if (wasLastCity || previousKingdom.countCities() <= 0)
            {
                AutoPanKingdomService.MarkDefendKingdomDefeated(previousKingdom);
                text += $" {previousKingdom.name} 已无城可守，幸存者不会再创建新国家。";
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
    /// 阻止坚守城池灭国后的无城单位通过普通建城任务重建城市。
    /// </summary>
    [HarmonyPatch(typeof(BehCheckBuildCity), nameof(BehCheckBuildCity.execute))]
    internal static class AutoPanDefeatedDefendBuildCityPatch
    {
        /// <summary>
        /// 在原版建城任务真正建城前迁移漏网单位。
        /// </summary>
        [HarmonyPrefix]
        private static bool Prefix(Actor pActor, ref BehResult __result)
        {
            if (!AutoPanKingdomService.TryRelocateDefeatedDefendSettler(pActor, "无城建城任务"))
            {
                return true;
            }

            __result = BehResult.Stop;
            return false;
        }
    }

    /// <summary>
    /// 阻止坚守城池灭国后的单位启动新文明。
    /// </summary>
    [HarmonyPatch(typeof(BehCheckStartCivilization), nameof(BehCheckStartCivilization.execute))]
    internal static class AutoPanDefeatedDefendStartCivilizationPatch
    {
        /// <summary>
        /// 在原版新文明任务真正创建国家前迁移漏网单位。
        /// </summary>
        [HarmonyPrefix]
        private static bool Prefix(Actor pActor, ref BehResult __result)
        {
            if (!AutoPanKingdomService.TryRelocateDefeatedDefendSettler(pActor, "新文明任务"))
            {
                return true;
            }

            __result = BehResult.Stop;
            return false;
        }
    }

    /// <summary>
    /// 兜底阻止代码路径直接检查并创建新文明。
    /// </summary>
    [HarmonyPatch(typeof(CityManager), nameof(CityManager.canStartNewCityCivilizationHere))]
    internal static class AutoPanDefeatedDefendCivilizationCheckPatch
    {
        /// <summary>
        /// 让漏网单位无法通过 canStartNewCityCivilizationHere 检查。
        /// </summary>
        [HarmonyPrefix]
        private static bool Prefix(Actor pActor, ref bool __result)
        {
            if (!AutoPanKingdomService.TryRelocateDefeatedDefendSettler(pActor, "新文明检查"))
            {
                return true;
            }

            __result = false;
            return false;
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
