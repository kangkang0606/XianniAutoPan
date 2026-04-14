using HarmonyLib;
using UnityEngine;
using XianniAutoPan.Commands;
using XianniAutoPan.Frontend;
using XianniAutoPan.Model;
using XianniAutoPan.Services;

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

                if (AutoPanStateRepository.TryGetLatestQqSessionForUser(binding.UserId, out AutoPanSessionInfo session))
                {
                    AutoPanLocalWebServer.Instance.SendQqNotice(session, binding.UserId, $"你绑定的国家 {AutoPanKingdomService.FormatKingdomLabel(pKingdom)} 已灭亡，绑定已自动解除。现在可以重新发送“加入人类/加入兽人/加入精灵/加入矮人”。");
                }
            }

            AutoPanStateRepository.ClearBindingByKingdomId(pKingdom.getID());
            AutoPanLogService.Info($"国家灭亡：{pKingdom.name}，已清理相关玩家绑定。");
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
