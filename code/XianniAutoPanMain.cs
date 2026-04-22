using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using NeoModLoader.api;
using XianniAutoPan.AI;
using XianniAutoPan.Commands;
using XianniAutoPan.Frontend;
using XianniAutoPan.Model;
using XianniAutoPan.Services;
using xn.api;

namespace XianniAutoPan
{
    /// <summary>
    /// 自动盘模组入口。
    /// </summary>
    internal sealed class XianniAutoPanMain : BasicMod<XianniAutoPanMain>
    {
        private Harmony _harmony;
        private string _modFolder;
        private int _lastObservedYear = -1;

        /// <summary>
        /// 模组加载入口。
        /// </summary>
        protected override void OnModLoad()
        {
            _modFolder = GetDeclaration().FolderPath;
            AutoPanConfigHooks.InitializeFromConfig(GetConfig());
            AutoPanConfigHooks.InitializeBackendPolicy(_modFolder);
            AutoPanNotificationService.Initialize(_modFolder);
            AutoPanScoreService.Initialize(_modFolder);
            AutoPanScreenshotService.Initialize(_modFolder);
            AutoPanRoundService.Initialize();
            ValidateCommandBook();

            _harmony = new Harmony("xianni.autopan.runtime");
            _harmony.PatchAll(typeof(AutoPanKingdomRemovePatch));
            _harmony.PatchAll(typeof(AutoPanMapFinishingUpLoadingPatch));
            _harmony.PatchAll(typeof(AutoPanWarEndPatch));
            _harmony.PatchAll(typeof(AutoPanCityCapturePatch));
            _harmony.PatchAll(typeof(AutoPanDefeatedDefendBuildCityPatch));
            _harmony.PatchAll(typeof(AutoPanDefeatedDefendStartCivilizationPatch));
            _harmony.PatchAll(typeof(AutoPanDefeatedDefendCivilizationCheckPatch));
            _harmony.PatchAll(typeof(AutoPanKingdomNameplatePatch));
            _harmony.PatchAll(typeof(AutoPanNameplateManagerUpdatePatch));

            MapBox.on_world_loaded += OnWorldLoaded;
            AutoPanLocalWebServer.Instance.Initialize(_modFolder);
            AutoPanLocalWebServer.Instance.UpdateConfiguration();
            AutoPanLogService.Info("模组已加载，等待世界与前端消息。");
        }

        /// <summary>
        /// 每帧处理网络消息、年度结算与 AI 结果。
        /// </summary>
        public void Update()
        {
            AutoPanLocalWebServer.Instance.UpdateConfiguration();
            ProcessFrontendMessages();
            AutoPanAiService.FlushCompletedResults();
            AutoPanRequestService.Update();
            AutoPanDuelService.Update();
            AutoPanTournamentService.Update();
            AutoPanKingdomSpeechService.Update();

            if (World.world == null || World.world.map_stats == null)
            {
                return;
            }

            int currentYear = Date.getCurrentYear();
            if (currentYear == _lastObservedYear)
            {
                return;
            }

            _lastObservedYear = currentYear;
            AutoPanWorldSpeedService.ApplyScheduledSpeedForYear(currentYear);
            AutoPanConfigHooks.RollRandomPolicyValuesForOperation();
            AutoPanStateRepository.CleanupDeadBindings();
            AutoPanKingdomService.ApplyYearlyIncomeToAll(currentYear);
            AutoPanAiService.ScheduleForYear(currentYear);
            AutoPanRoundService.CheckAutoEndRound(currentYear);
        }

        /// <summary>
        /// 模组销毁时关闭本地服务。
        /// </summary>
        public void OnDestroy()
        {
            AutoPanLocalWebServer.Instance.Stop();
            AutoPanRequestService.ClearAll();
            AutoPanDuelService.ClearAll();
            AutoPanTournamentService.Clear();
            AutoPanKingdomSpeechService.Dispose();
        }

        private void OnWorldLoaded()
        {
            AutoPanStateRepository.LoadFromWorld();
            ResetFreshWorldYearIfNeeded();
            AutoPanStateRepository.CleanupDeadBindings();
            ClearLegacyXiuzhenguoOffsets();
            AutoPanKingdomService.ClearDefeatedDefendSettlementGuards();
            AutoPanRequestService.ClearAll();
            AutoPanDuelService.ClearAll();
            AutoPanTournamentService.Clear();
            AutoPanKingdomSpeechService.ClearAll();
            AutoPanRoundService.OnWorldLoaded();
            AutoPanWorldSpeedService.ApplyScheduledSpeedForYear(Date.getCurrentYear(), force: true);
            _lastObservedYear = Date.getCurrentYear();
            AutoPanLogService.Info($"世界已加载，当前年份 {_lastObservedYear}，自动盘状态已恢复。");
        }

        private void ResetFreshWorldYearIfNeeded()
        {
            if (World.world?.map_stats == null)
            {
                return;
            }

            World.world.map_stats.world_time = 0.0;
            World.world.map_stats.history_current_year = -1;
            AutoPanLogService.Info("已将年份重置为 1 年。");
            AutoPanStateRepository.MarkWorldInitialized();
        }

        private static void ClearLegacyXiuzhenguoOffsets()
        {
            if (World.world?.kingdoms == null)
            {
                return;
            }

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom == null || !kingdom.isAlive())
                {
                    continue;
                }

                XianniAutoPanApi.ClearXiuzhenguoManualOffset(kingdom);
                XianniAutoPanApi.RefreshXiuzhenguoLevel(kingdom);
            }
        }

        private void ProcessFrontendMessages()
        {
            int processed = 0;
            while (processed < AutoPanConstants.MaxMessagesPerFrame && AutoPanLocalWebServer.Instance.TryDequeueMessage(out FrontendInboundMessage message))
            {
                AutoPanCommandResult result = AutoPanCommandExecutor.ExecutePlayerMessage(message);
                AutoPanLocalWebServer.Instance.SendReply(message, result);
                processed++;
            }
        }

        private void ValidateCommandBook()
        {
            string commandBookPath = Path.Combine(_modFolder, "指令书.txt");
            if (!File.Exists(commandBookPath))
            {
                AutoPanLogService.Error("模组根目录缺少 指令书.txt。");
                return;
            }

            string text = File.ReadAllText(commandBookPath);
            string[] requiredKeywords =
            {
                "加入人类",
                "加入兽人",
                "加入精灵",
                "加入矮人",
                "加入 国家名",
                "加入文明单位",
                "帮助",
                "我的国家",
                "国家信息",
                "查看所有国家信息",
                "国家改名",
                "血脉创立 12345",
                "城市列表",
                "城市信息",
                "升级国运",
                "升级修真国",
                "政策 开放占领",
                "政策 坚守城池",
                "降低国运",
                "国策 聚灵",
                "全民皆兵",
                "动员",
                "增加人数",
                "放置遗迹",
                "转账",
                "天榜",
                "战力榜",
                "削灵",
                "斩首",
                "诅咒",
                "国家祝福",
                "修士降境",
                "古神降星",
                "妖兽降阶",
                "快速成年",
                "征集军队",
                "移交城市",
                "随机一座城市",
                "军备",
                "约斗",
                "宣战",
                "求和",
                "结盟",
                "同意结盟",
                "拒绝结盟",
                "同意约斗",
                "拒绝约斗",
                "退盟",
                "修士榜",
                "古神榜",
                "妖兽榜",
                "修士 12345 闭关",
                "修士 12345 升境",
                "古神 12345 炼体",
                "古神 12345 升星",
                "妖兽 12345 养成",
                "妖兽 12345 升阶",
                "陨石",
                "开启比武大会",
                "#增加国家金币",
                "#设置国家金币",
                "#查看国家金币",
                "#全局AI 开",
                "#全局AI 关",
                "#查看绑定",
                "#查看政策",
                "#设置政策",
                "#查看倍速计划",
                "#设置倍速计划",
                "#设置AI自动加入数",
                "#设置AI开始决策年份",
                "#设置玩家开始决策年份",
                "#当前局势",
                "#结盘",
                "玩家排名",
                "当前局势"
            };

            string[] missing = requiredKeywords.Where(keyword => !text.Contains(keyword)).ToArray();
            if (missing.Length > 0)
            {
                AutoPanLogService.Error("指令书.txt 缺少以下关键字：" + string.Join("，", missing));
            }
        }
    }
}
