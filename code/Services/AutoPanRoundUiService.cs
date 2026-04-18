using System;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 管理新局加载完成后的原版 UI 隐藏与交互清理。
    /// </summary>
    internal static class AutoPanRoundUiService
    {
        private static bool _hideRequested;

        /// <summary>
        /// 请求在下一次世界收尾加载完成后隐藏权能条并清理当前交互。
        /// </summary>
        public static void RequestHideAfterNextWorldLoad()
        {
            _hideRequested = true;
        }

        /// <summary>
        /// 若新局请求仍在等待，则在原版收尾加载后应用 UI 隐藏。
        /// </summary>
        public static void ApplyPendingHideAfterWorldLoad()
        {
            if (!_hideRequested)
            {
                return;
            }

            _hideRequested = false;
            HidePowerBarAndInteraction();
        }

        /// <summary>
        /// 隐藏原版权能条并取消当前选中权能、单位与对象。
        /// </summary>
        public static void HidePowerBarAndInteraction()
        {
            try
            {
                Config.ui_main_hidden = true;
                PowerButtonSelector selector = PowerButtonSelector.instance ?? World.world?.selected_buttons;
                selector?.unselectAll();
                selector?.unselectTabs();
                selector?.toggleBottomElements(false, true);
                SelectedUnit.clear();
                SelectedObjects.unselectNanoObject();
                PowersTab.unselect();
                PowerTracker.setPower(null);
                AutoPanLogService.Info("新局已自动隐藏权能条并清理当前交互。");
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"新局隐藏权能条失败：{ex}");
            }
        }
    }
}
