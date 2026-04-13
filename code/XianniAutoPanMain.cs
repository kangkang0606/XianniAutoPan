using System.IO;
using System.Linq;
using HarmonyLib;
using NeoModLoader.api;
using XianniAutoPan.AI;
using XianniAutoPan.Commands;
using XianniAutoPan.Frontend;
using XianniAutoPan.Model;
using XianniAutoPan.Services;

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
            ValidateCommandBook();

            _harmony = new Harmony("xianni.autopan.runtime");
            _harmony.PatchAll(typeof(AutoPanKingdomRemovePatch));
            _harmony.PatchAll(typeof(AutoPanWarEndPatch));
            _harmony.PatchAll(typeof(AutoPanKingdomNameplatePatch));

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
            AutoPanStateRepository.CleanupDeadBindings();
            AutoPanKingdomService.ApplyYearlyIncomeToAll(currentYear);
            AutoPanAiService.ScheduleForYear(currentYear);
        }

        /// <summary>
        /// 模组销毁时关闭本地服务。
        /// </summary>
        public void OnDestroy()
        {
            AutoPanLocalWebServer.Instance.Stop();
            AutoPanKingdomSpeechService.Dispose();
        }

        private void OnWorldLoaded()
        {
            AutoPanStateRepository.LoadFromWorld();
            AutoPanStateRepository.CleanupDeadBindings();
            AutoPanKingdomSpeechService.ClearAll();
            _lastObservedYear = Date.getCurrentYear();
            AutoPanLogService.Info($"世界已加载，当前年份 {_lastObservedYear}，自动盘状态已恢复。");
        }

        private void ProcessFrontendMessages()
        {
            int processed = 0;
            while (processed < AutoPanConstants.MaxMessagesPerFrame && AutoPanLocalWebServer.Instance.TryDequeueMessage(out FrontendInboundMessage message))
            {
                AutoPanCommandResult result = AutoPanCommandExecutor.ExecutePlayerMessage(message);
                AutoPanLocalWebServer.Instance.SendReply(message.SessionId, result);
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
                "我的国家",
                "国家信息",
                "血脉创立",
                "城市列表",
                "城市信息 <城市名>",
                "升级国运",
                "国策 聚灵",
                "削灵 <国家名> <数值>",
                "斩首 <国家名>",
                "诅咒 <国家名> <人数>",
                "国家祝福 <全员|人数>",
                "修士降境 <国家名> <人数> <等级>",
                "快速成年 <城市名|全城>",
                "征集军队 <城市名> <人数|全部>",
                "移交城市 <城市名> 给 <国家名>",
                "军备 <城市名> <铜|青铜|白银|铁|钢|秘银|精金> <全军|人数>",
                "宣战 <国家名>",
                "求和 <国家名>",
                "结盟 <国家名>",
                "解盟 <国家名>",
                "修士榜",
                "古神榜",
                "妖兽榜",
                "修士 <序号> 闭关",
                "修士 <序号> 升境",
                "古神 <序号> 炼体",
                "古神 <序号> 升星",
                "妖兽 <序号> 养成",
                "妖兽 <序号> 升阶",
                "#增加国家金币 <国家名> <数值>",
                "#设置国家金币 <国家名> <数值>",
                "#查看国家金币 <国家名>",
                "#全局AI 开",
                "#全局AI 关",
                "#查看绑定 <userId>"
            };

            string[] missing = requiredKeywords.Where(keyword => !text.Contains(keyword)).ToArray();
            if (missing.Length > 0)
            {
                AutoPanLogService.Error("指令书.txt 缺少以下关键字：" + string.Join("，", missing));
            }
        }
    }
}
