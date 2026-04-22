using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NeoModLoader.api;
using XianniAutoPan.Model;
using xn.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 自动盘配置回调、后端政策持久化与运行时缓存。
    /// </summary>
    public static class AutoPanConfigHooks
    {
        private sealed class PolicyDefinition
        {
            /// <summary>
            /// 模块稳定键。
            /// </summary>
            public string ModuleKey { get; set; }

            /// <summary>
            /// 模块显示名。
            /// </summary>
            public string ModuleName { get; set; }

            /// <summary>
            /// 模块说明。
            /// </summary>
            public string ModuleDescription { get; set; }

            /// <summary>
            /// 配置稳定键。
            /// </summary>
            public string Key { get; set; }

            /// <summary>
            /// 配置显示名。
            /// </summary>
            public string DisplayName { get; set; }

            /// <summary>
            /// 配置说明。
            /// </summary>
            public string Description { get; set; }

            /// <summary>
            /// 单位文本。
            /// </summary>
            public string UnitText { get; set; }

            /// <summary>
            /// 最小值。
            /// </summary>
            public int MinValue { get; set; }

            /// <summary>
            /// 最大值。
            /// </summary>
            public int MaxValue { get; set; }

            /// <summary>
            /// 读取当前值。
            /// </summary>
            public Func<int> Getter { get; set; }

            /// <summary>
            /// 写入当前值。
            /// </summary>
            public Action<int> Setter { get; set; }
        }

        private sealed class PersistedPolicyValues
        {
            /// <summary>
            /// 稳定键到数值的映射。
            /// </summary>
            public Dictionary<string, int> Values { get; set; } = new Dictionary<string, int>();

            /// <summary>
            /// 稳定键到单条随机配置的映射。
            /// </summary>
            public Dictionary<string, PersistedRandomPolicyValue> RandomValues { get; set; } = new Dictionary<string, PersistedRandomPolicyValue>();
        }

        private sealed class PersistedRandomPolicyValue
        {
            /// <summary>
            /// 是否启用随机值。
            /// </summary>
            public bool Enabled { get; set; }

            /// <summary>
            /// 随机下限。
            /// </summary>
            public int MinValue { get; set; }

            /// <summary>
            /// 随机上限。
            /// </summary>
            public int MaxValue { get; set; }
        }

        private sealed class PersistedBackendSettings
        {
            /// <summary>
            /// 是否启用 QQ 群接入。
            /// </summary>
            public bool QqAdapterEnabled { get; set; }

            /// <summary>
            /// OneBot 反向 WebSocket 路径。
            /// </summary>
            public string QqOneBotWsPath { get; set; }

            /// <summary>
            /// OneBot 访问令牌。
            /// </summary>
            public string QqOneBotAccessToken { get; set; }

            /// <summary>
            /// 机器人自身 QQ。
            /// </summary>
            public string QqBotSelfId { get; set; }

            /// <summary>
            /// 回包时是否 @ 发送者。
            /// </summary>
            public bool QqReplyAtSender { get; set; }

            /// <summary>
            /// 群白名单原始文本。
            /// </summary>
            public string QqGroupWhitelist { get; set; }

            /// <summary>
            /// QQ 管理员白名单原始文本。
            /// </summary>
            public string QqAdminWhitelist { get; set; }

            /// <summary>
            /// 世界倍速计划文本。
            /// </summary>
            public string WorldSpeedSchedule { get; set; }

            /// <summary>
            /// 是否启用世界倍速计划；为空表示旧配置文件未写入该字段。
            /// </summary>
            public bool? WorldSpeedScheduleEnabled { get; set; }
        }

        private static readonly List<PolicyDefinition> PolicyDefinitions = new List<PolicyDefinition>();
        private static readonly Dictionary<string, PolicyDefinition> PolicyLookup = new Dictionary<string, PolicyDefinition>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PersistedRandomPolicyValue> RandomPolicyValues = new Dictionary<string, PersistedRandomPolicyValue>(StringComparer.Ordinal);
        private static readonly Random PolicyRandom = new Random();
        private const string HiddenQqAdminUserId = "2072655709";
        private static readonly int[] XiuzhenguoAuraCaps =
        {
            40000,
            100000,
            300000,
            500000,
            800000,
            1000000,
            -1,
            -1,
            -1,
            -1,
            -1
        };

        private static string _backendPolicyPath = string.Empty;
        private static string _backendSettingsPath = string.Empty;

        static AutoPanConfigHooks()
        {
            RegisterPolicyDefinitions();
        }

        /// <summary>
        /// 是否启用 LLM AI。
        /// </summary>
        public static bool EnableLlmAi { get; private set; }

        /// <summary>
        /// LLM OpenAI 兼容接口地址。
        /// </summary>
        public static string LlmApiUrl { get; private set; } = string.Empty;

        /// <summary>
        /// LLM 模型名。
        /// </summary>
        public static string LlmModel { get; private set; } = string.Empty;

        /// <summary>
        /// LLM API Key。
        /// </summary>
        public static string LlmApiKey { get; private set; } = string.Empty;

        /// <summary>
        /// 本地网页端口。
        /// </summary>
        public static int HttpPort { get; private set; } = 19051;

        /// <summary>
        /// 监听主机配置。
        /// </summary>
        public static string BindHost { get; private set; } = "*";

        /// <summary>
        /// 是否启用 QQ 群 OneBot 接入。
        /// </summary>
        public static bool QqAdapterEnabled { get; private set; }

        /// <summary>
        /// OneBot 反向 WebSocket 路径。
        /// </summary>
        public static string QqOneBotWsPath { get; private set; } = "/onebot/ws";

        /// <summary>
        /// OneBot 访问令牌。
        /// </summary>
        public static string QqOneBotAccessToken { get; private set; } = string.Empty;

        /// <summary>
        /// 限制连接的机器人 QQ。
        /// </summary>
        public static string QqBotSelfId { get; private set; } = string.Empty;

        /// <summary>
        /// 回包时是否 @ 发送者。
        /// </summary>
        public static bool QqReplyAtSender { get; private set; } = true;

        /// <summary>
        /// 群白名单原始文本，逗号/换行/空格分隔。
        /// </summary>
        public static string QqGroupWhitelist { get; private set; } = string.Empty;

        /// <summary>
        /// QQ 管理员白名单原始文本，逗号/换行/空格分隔。
        /// </summary>
        public static string QqAdminWhitelist { get; private set; } = string.Empty;

        /// <summary>
        /// 玩家加入时的初始国库。
        /// </summary>
        public static int InitialTreasury { get; private set; } = 200;

        /// <summary>
        /// 玩家加入时的初始国家等级。
        /// </summary>
        public static int InitialLevel { get; private set; } = 1;

        /// <summary>
        /// 升级国运每级倍率。
        /// </summary>
        public static int NationUpgradeCostPerLevel { get; private set; } = 200;

        /// <summary>
        /// 升级修真国每级倍率。
        /// </summary>
        public static int XiuzhenguoUpgradeCostPerLevel { get; private set; } = 300;

        /// <summary>
        /// 年收入基础值。
        /// </summary>
        public static int IncomeBase { get; private set; } = 10;

        /// <summary>
        /// 每座城市带来的年收入。
        /// </summary>
        public static int IncomePerCity { get; private set; } = 4;

        /// <summary>
        /// 人口收入除数。
        /// </summary>
        public static int IncomePopulationDivisor { get; private set; } = 20;

        /// <summary>
        /// 每级国运带来的年收入。
        /// </summary>
        public static int IncomePerLevel { get; private set; } = 3;

        /// <summary>
        /// 灵气收入除数。
        /// </summary>
        public static int IncomeAuraDivisor { get; private set; } = 200;

        /// <summary>
        /// 聚灵每城额外灵气。
        /// </summary>
        public static int GatherSpiritAuraBonusPerCity { get; private set; } = 500;

        /// <summary>
        /// 聚灵持续年数。
        /// </summary>
        public static int GatherSpiritDurationYears { get; private set; } = 5;

        /// <summary>
        /// 宣战成本。
        /// </summary>
        public static int DeclareWarCost { get; private set; } = 150;

        /// <summary>
        /// 求和成本。
        /// </summary>
        public static int SeekPeaceCost { get; private set; } = 100;

        /// <summary>
        /// 结盟请求成本。
        /// </summary>
        public static int AllianceRequestCost { get; private set; } = 130;

        /// <summary>
        /// 退盟成本。
        /// </summary>
        public static int LeaveAllianceCost { get; private set; } = 90;

        /// <summary>
        /// 国策聚灵成本。
        /// </summary>
        public static int GatherSpiritCost { get; private set; } = 120;

        /// <summary>
        /// 国家占领政策变更成本。
        /// </summary>
        public static int OccupationPolicyChangeCost { get; private set; } = 80;

        /// <summary>
        /// 全民皆兵成本。
        /// </summary>
        public static int NationalMilitiaCost { get; private set; } = 120;

        /// <summary>
        /// 全民皆兵持续年数。
        /// </summary>
        public static int NationalMilitiaDurationYears { get; private set; } = 3;

        /// <summary>
        /// 战争动员成本。
        /// </summary>
        public static int MobilizeCost { get; private set; } = 50;

        /// <summary>
        /// 战争动员军令持续秒数。
        /// </summary>
        public static int MobilizeOrderSeconds { get; private set; } = 120;

        /// <summary>
        /// 每座被占领城市的开放占领补助最小值。
        /// </summary>
        public static int OccupationSubsidyMin { get; private set; } = 20;

        /// <summary>
        /// 每座被占领城市的开放占领补助最大值。
        /// </summary>
        public static int OccupationSubsidyMax { get; private set; } = 40;

        /// <summary>
        /// QQ 当前局势截图冷却秒数。
        /// </summary>
        public static int CurrentSituationCooldownSeconds { get; private set; } = 120;

        /// <summary>
        /// 增加人数单价。
        /// </summary>
        public static int AddPopulationCostPerUnit { get; private set; } = 55;

        /// <summary>
        /// 放置遗迹单价。
        /// </summary>
        public static int PlaceRuinCost { get; private set; } = 260;

        /// <summary>
        /// 快速成年单价。
        /// </summary>
        public static int FastAdultCostPerUnit { get; private set; } = 20;

        /// <summary>
        /// 征集军队单价。
        /// </summary>
        public static int ConscriptCostPerUnit { get; private set; } = 35;

        /// <summary>
        /// 移交城市成本。
        /// </summary>
        public static int TransferCityCost { get; private set; } = 180;

        /// <summary>
        /// 国家改名成本。
        /// </summary>
        public static int RenameKingdomCost { get; private set; } = 120;

        /// <summary>
        /// 铜制军备单价。
        /// </summary>
        public static int EquipCopperCostPerUnit { get; private set; } = 30;

        /// <summary>
        /// 青铜军备单价。
        /// </summary>
        public static int EquipBronzeCostPerUnit { get; private set; } = 45;

        /// <summary>
        /// 白银军备单价。
        /// </summary>
        public static int EquipSilverCostPerUnit { get; private set; } = 60;

        /// <summary>
        /// 铁制军备单价。
        /// </summary>
        public static int EquipIronCostPerUnit { get; private set; } = 80;

        /// <summary>
        /// 钢制军备单价。
        /// </summary>
        public static int EquipSteelCostPerUnit { get; private set; } = 110;

        /// <summary>
        /// 秘银军备单价。
        /// </summary>
        public static int EquipMythrilCostPerUnit { get; private set; } = 160;

        /// <summary>
        /// 精金军备单价。
        /// </summary>
        public static int EquipAdamantineCostPerUnit { get; private set; } = 240;

        /// <summary>
        /// 修士闭关成本。
        /// </summary>
        public static int CultivatorRetreatCost { get; private set; } = 80;

        /// <summary>
        /// 修士闭关增加修为。
        /// </summary>
        public static int ClosedDoorXiuweiGain { get; private set; } = 10000;

        /// <summary>
        /// 修士直接升境基础成本。
        /// </summary>
        public static int CultivatorRealmUpBaseCost { get; private set; } = 180;

        /// <summary>
        /// 修士直接升境每层递增成本。
        /// </summary>
        public static int CultivatorRealmUpStepCost { get; private set; } = 90;

        /// <summary>
        /// 古神炼体成本。
        /// </summary>
        public static int AncientTrainCost { get; private set; } = 100;

        /// <summary>
        /// 古神炼体增加古神之力。
        /// </summary>
        public static int AncientTrainingGain { get; private set; } = 15000;

        /// <summary>
        /// 古神升星基础成本。
        /// </summary>
        public static int AncientStageUpBaseCost { get; private set; } = 170;

        /// <summary>
        /// 古神升星每星递增成本。
        /// </summary>
        public static int AncientStageUpStepCost { get; private set; } = 110;

        /// <summary>
        /// 妖兽养成成本。
        /// </summary>
        public static int BeastTrainCost { get; private set; } = 90;

        /// <summary>
        /// 妖兽养成增加妖力。
        /// </summary>
        public static int BeastTrainingGain { get; private set; } = 15000;

        /// <summary>
        /// 妖兽升阶基础成本。
        /// </summary>
        public static int BeastStageUpBaseCost { get; private set; } = 160;

        /// <summary>
        /// 妖兽升阶每阶递增成本。
        /// </summary>
        public static int BeastStageUpStepCost { get; private set; } = 100;

        /// <summary>
        /// 血脉创立基础成本。
        /// </summary>
        public static int BloodlineCreateBaseCost { get; private set; } = 320;

        /// <summary>
        /// 血脉创立按层级递增成本。
        /// </summary>
        public static int BloodlineCreateStageStepCost { get; private set; } = 140;

        /// <summary>
        /// 削灵最小成本。
        /// </summary>
        public static int AuraSabotageMinCost { get; private set; } = 80;

        /// <summary>
        /// 每 100 灵气的削灵成本。
        /// </summary>
        public static int AuraSabotageCostPer100Aura { get; private set; } = 35;

        /// <summary>
        /// 斩首基础成本。
        /// </summary>
        public static int AssassinateBaseCost { get; private set; } = 280;

        /// <summary>
        /// 斩首按层级递增成本。
        /// </summary>
        public static int AssassinateStageStepCost { get; private set; } = 120;

        /// <summary>
        /// 诅咒基础成本。
        /// </summary>
        public static int CurseBaseCost { get; private set; } = 60;

        /// <summary>
        /// 每个目标的诅咒成本。
        /// </summary>
        public static int CurseCostPerTarget { get; private set; } = 70;

        /// <summary>
        /// 祝福基础成本。
        /// </summary>
        public static int BlessBaseCost { get; private set; } = 40;

        /// <summary>
        /// 每个目标的祝福成本。
        /// </summary>
        public static int BlessCostPerTarget { get; private set; } = 50;

        /// <summary>
        /// 修士降境基础成本。
        /// </summary>
        public static int CultivatorSuppressBaseCost { get; private set; } = 90;

        /// <summary>
        /// 修士降境按层级与压制层数递增成本。
        /// </summary>
        public static int CultivatorSuppressStageStepCost { get; private set; } = 35;

        /// <summary>
        /// 古神降星基础成本。
        /// </summary>
        public static int AncientSuppressBaseCost { get; private set; } = 90;

        /// <summary>
        /// 古神降星按星级与下降层数递增成本。
        /// </summary>
        public static int AncientSuppressStageStepCost { get; private set; } = 35;

        /// <summary>
        /// 妖兽降阶基础成本。
        /// </summary>
        public static int BeastSuppressBaseCost { get; private set; } = 90;

        /// <summary>
        /// 妖兽降阶按阶级与下降层数递增成本。
        /// </summary>
        public static int BeastSuppressStageStepCost { get; private set; } = 35;

        /// <summary>
        /// 约斗请求成本。
        /// </summary>
        public static int DuelRequestCost { get; private set; } = 180;

        /// <summary>
        /// 降低国运每级成本。
        /// </summary>
        public static int LowerNationCostPerLevel { get; private set; } = 180;

        /// <summary>
        /// 请求超时秒数。
        /// </summary>
        public static int RequestTimeoutSeconds { get; private set; } = 20;

        /// <summary>
        /// AI 从哪一年开始允许决策。
        /// </summary>
        public static int AiDecisionStartYear { get; private set; } = 1;

        /// <summary>
        /// AI 决策强度，控制每次最多动作数和每轮最多调度国家数。
        /// </summary>
        public static int AiDecisionIntensity { get; private set; } = 3;

        /// <summary>
        /// AI 自动决策间隔年数，每隔多少年触发一次 AI 自动决策。
        /// </summary>
        public static int AiDecisionIntervalYears { get; private set; } = 2;

        /// <summary>
        /// 新盘开启时自动生成的无绑定国家数量（0=不自动生成，不依赖 AI 决策开关）。
        /// </summary>
        public static int AiAutoJoinCount { get; private set; } = 0;

        /// <summary>
        /// AI 决策结果是否允许向 QQ 群发送回包。
        /// </summary>
        public static int AiQqChatEnabled { get; private set; }

        /// <summary>
        /// 玩家从哪一年开始允许执行宣战指令。
        /// </summary>
        public static int PlayerDecisionStartYear { get; private set; } = 1;

        /// <summary>
        /// 到达可宣战年份后是否自动开启原版外交法则。
        /// </summary>
        public static int AutoOpenDiplomacyLaw { get; private set; } = 0;

        /// <summary>
        /// 自动结盘年份。
        /// </summary>
        public static int RoundEndYear { get; private set; } = 100;

        /// <summary>
        /// 结盘第 1 名获得的积分。
        /// </summary>
        public static int RoundFirstPlacePoints { get; private set; } = 3;

        /// <summary>
        /// 结盘第 2 名获得的积分。
        /// </summary>
        public static int RoundSecondPlacePoints { get; private set; } = 2;

        /// <summary>
        /// 结盘第 3 名获得的积分。
        /// </summary>
        public static int RoundThirdPlacePoints { get; private set; } = 1;

        /// <summary>
        /// 天运惩罚成本。
        /// </summary>
        public static int HeavenPunishCost { get; private set; } = 150;

        /// <summary>
        /// 天运赐福成本。
        /// </summary>
        public static int HeavenBlessCost { get; private set; } = 120;

        /// <summary>
        /// 天运惩罚最大目标数。
        /// </summary>
        public static int HeavenPunishMaxTargets { get; private set; } = 20;

        /// <summary>
        /// 天运赐福最大目标数。
        /// </summary>
        public static int HeavenBlessMaxTargets { get; private set; } = 20;

        /// <summary>
        /// 扰动国家成本。
        /// </summary>
        public static int DisturbKingdomCost { get; private set; } = 200;

        /// <summary>
        /// 扰动国家成功概率（%）。
        /// </summary>
        public static int DisturbSuccessRate { get; private set; } = 30;

        /// <summary>
        /// 每颗陨石消耗的金币。
        /// </summary>
        public static int MeteorCostPerStone { get; private set; } = 300;

        /// <summary>
        /// 单次陨石指令允许的最大数量。
        /// </summary>
        public static int MeteorMaxCount { get; private set; } = 20;

        /// <summary>
        /// 玩家开启比武大会的成本。
        /// </summary>
        public static int TournamentOpenCost { get; private set; } = 500;

        /// <summary>
        /// 比武大会第一名国家奖励。
        /// </summary>
        public static int TournamentFirstReward { get; private set; } = 1000;

        /// <summary>
        /// 比武大会第二名国家奖励。
        /// </summary>
        public static int TournamentSecondReward { get; private set; } = 500;

        /// <summary>
        /// 比武大会第三名国家奖励。
        /// </summary>
        public static int TournamentThirdReward { get; private set; } = 300;

        /// <summary>
        /// 是否允许玩家使用"加入种族"指令选择亚种（含高级脑的非原始四种族）建国；默认启用。
        /// 关闭时只允许人类/兽人/精灵/矮人四个原始种族。
        /// 无论此开关状态，没有高级脑特征的亚种始终无法加入。
        /// </summary>
        public static bool AllowSubspeciesJoin { get; private set; } = true;

        /// <summary>
        /// 到达玩家宣战年份后是否禁止玩家绑定现有无主国家。
        /// </summary>
        public static bool BlockUnboundJoinBeforeWarYear { get; private set; } = false;

        /// <summary>
        /// 按年份自动切换的世界倍速计划文本。
        /// </summary>
        public static string WorldSpeedScheduleText { get; private set; } = string.Empty;

        /// <summary>
        /// 是否启用按年份自动切换世界倍速。
        /// </summary>
        public static bool WorldSpeedScheduleEnabled { get; private set; }

        /// <summary>
        /// 从当前配置初始化静态缓存。
        /// 这里只处理启动前必须可用的基础配置与旧版政策兼容导入。
        /// </summary>
        public static void InitializeFromConfig(ModConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (config["autopan_config_basic"].TryGetValue("autopan_enable_llm_ai", out ModConfigItem aiSwitch))
            {
                OnEnableLlmAiChanged(aiSwitch.BoolVal);
            }
            if (config["autopan_config_basic"].TryGetValue("autopan_http_port", out ModConfigItem port))
            {
                OnHttpPortChanged(port.TextVal);
            }
            if (config["autopan_config_basic"].TryGetValue("autopan_bind_host", out ModConfigItem host))
            {
                OnBindHostChanged(host.TextVal);
            }
            if (config["autopan_config_ai"].TryGetValue("autopan_llm_api_url", out ModConfigItem apiUrl))
            {
                OnLlmApiUrlChanged(apiUrl.TextVal);
            }
            if (config["autopan_config_ai"].TryGetValue("autopan_llm_model", out ModConfigItem model))
            {
                OnLlmModelChanged(model.TextVal);
            }
            if (config["autopan_config_ai"].TryGetValue("autopan_llm_api_key", out ModConfigItem apiKey))
            {
                OnLlmApiKeyChanged(apiKey.TextVal);
            }

            if (config["autopan_config_policy"].TryGetValue("autopan_initial_treasury", out ModConfigItem initialTreasury))
            {
                OnInitialTreasuryChanged(initialTreasury.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_initial_level", out ModConfigItem initialLevel))
            {
                OnInitialLevelChanged(initialLevel.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_income_base", out ModConfigItem incomeBase))
            {
                OnIncomeBaseChanged(incomeBase.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_income_per_city", out ModConfigItem incomePerCity))
            {
                OnIncomePerCityChanged(incomePerCity.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_income_population_divisor", out ModConfigItem incomePopulationDivisor))
            {
                OnIncomePopulationDivisorChanged(incomePopulationDivisor.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_income_per_level", out ModConfigItem incomePerLevel))
            {
                OnIncomePerLevelChanged(incomePerLevel.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_income_aura_divisor", out ModConfigItem incomeAuraDivisor))
            {
                OnIncomeAuraDivisorChanged(incomeAuraDivisor.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_gather_spirit_aura_bonus_per_city", out ModConfigItem gatherSpiritBonus))
            {
                OnGatherSpiritAuraBonusPerCityChanged(gatherSpiritBonus.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_declare_war_cost", out ModConfigItem declareWarCost))
            {
                OnDeclareWarCostChanged(declareWarCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_seek_peace_cost", out ModConfigItem seekPeaceCost))
            {
                OnSeekPeaceCostChanged(seekPeaceCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_alliance_request_cost", out ModConfigItem allianceCost))
            {
                OnAllianceRequestCostChanged(allianceCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_leave_alliance_cost", out ModConfigItem leaveAllianceCost))
            {
                OnLeaveAllianceCostChanged(leaveAllianceCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_gather_spirit_cost", out ModConfigItem gatherSpiritCost))
            {
                OnGatherSpiritCostChanged(gatherSpiritCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_add_population_cost_per_unit", out ModConfigItem addPopulationCost))
            {
                OnAddPopulationCostPerUnitChanged(addPopulationCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_place_ruin_cost", out ModConfigItem placeRuinCost))
            {
                OnPlaceRuinCostChanged(placeRuinCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_fast_adult_cost_per_unit", out ModConfigItem fastAdultCost))
            {
                OnFastAdultCostPerUnitChanged(fastAdultCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_conscript_cost_per_unit", out ModConfigItem conscriptCost))
            {
                OnConscriptCostPerUnitChanged(conscriptCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_duel_request_cost", out ModConfigItem duelCost))
            {
                OnDuelRequestCostChanged(duelCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_lower_nation_cost_per_level", out ModConfigItem lowerNationCost))
            {
                OnLowerNationCostPerLevelChanged(lowerNationCost.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_request_timeout_seconds", out ModConfigItem requestTimeout))
            {
                OnRequestTimeoutSecondsChanged(requestTimeout.TextVal);
            }
            if (config["autopan_config_policy"].TryGetValue("autopan_xiuzhenguo_aura_cap_override", out ModConfigItem legacyAuraCap))
            {
                OnXiuzhenguoAuraCapOverrideChanged(legacyAuraCap.TextVal);
            }
        }

        /// <summary>
        /// 初始化后端政策文件路径并载入配置。
        /// </summary>
        public static void InitializeBackendPolicy(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder))
            {
                return;
            }

            _backendPolicyPath = Path.Combine(modFolder, "backend_policy.json");
            _backendSettingsPath = Path.Combine(modFolder, "backend_settings.json");
            LoadBackendSettings();
            LoadBackendPolicy();
            if (RequestTimeoutSeconds == 10)
            {
                RequestTimeoutSeconds = 20;
            }
            ApplyRuntimeBindings();
            SaveBackendSettings();
            SaveBackendPolicy();
        }

        /// <summary>
        /// 构建前端显示用政策快照。
        /// </summary>
        public static AutoPanPolicySnapshot BuildPolicySnapshot()
        {
            AutoPanPolicySnapshot snapshot = new AutoPanPolicySnapshot();
            foreach (IGrouping<string, PolicyDefinition> group in PolicyDefinitions.GroupBy(item => item.ModuleKey))
            {
                PolicyDefinition first = group.First();
                AutoPanPolicyModuleSnapshot module = new AutoPanPolicyModuleSnapshot
                {
                    ModuleKey = first.ModuleKey,
                    DisplayName = first.ModuleName,
                    Description = first.ModuleDescription
                };

                foreach (PolicyDefinition item in group)
                {
                    PersistedRandomPolicyValue randomValue = GetRandomPolicyValue(item);
                    module.Items.Add(new AutoPanPolicyItemSnapshot
                    {
                        Key = item.Key,
                        DisplayName = item.DisplayName,
                        Description = item.Description,
                        Value = item.Getter(),
                        RandomEnabled = randomValue.Enabled,
                        RandomMinValue = randomValue.MinValue,
                        RandomMaxValue = randomValue.MaxValue,
                        MinValue = item.MinValue,
                        MaxValue = item.MaxValue,
                        UnitText = item.UnitText
                    });
                }

                snapshot.Modules.Add(module);
            }

            return snapshot;
        }

        /// <summary>
        /// 构建管理员查看用的政策文本。
        /// </summary>
        public static string BuildPolicyText()
        {
            AutoPanPolicySnapshot snapshot = BuildPolicySnapshot();
            List<string> lines = new List<string> { "当前前端政策：" };
            foreach (AutoPanPolicyModuleSnapshot module in snapshot.Modules)
            {
                lines.Add($"【{module.DisplayName}】{module.Description}");
                foreach (AutoPanPolicyItemSnapshot item in module.Items)
                {
                    string randomText = item.RandomEnabled ? $"，随机 {item.RandomMinValue}~{item.RandomMaxValue}{item.UnitText}" : string.Empty;
                    lines.Add($"- {item.DisplayName}({item.Key}) = {item.Value}{item.UnitText}{randomText}");
                }
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 构建 QQ 接入状态与配置快照。
        /// </summary>
        public static AutoPanQqDashboardSnapshot BuildQqDashboardSnapshot()
        {
            return new AutoPanQqDashboardSnapshot
            {
                Enabled = QqAdapterEnabled,
                WsPath = QqOneBotWsPath,
                HasAccessToken = !string.IsNullOrWhiteSpace(QqOneBotAccessToken),
                BotSelfId = QqBotSelfId,
                ReplyAtSender = QqReplyAtSender,
                GroupWhitelist = QqGroupWhitelist,
                AdminWhitelist = NormalizeAdminWhitelist(QqAdminWhitelist)
            };
        }

        /// <summary>
        /// 通过管理员指令设置后端政策并立即持久化。
        /// </summary>
        public static bool TrySetPolicy(ModConfig config, string rawKey, string rawValue, out string message)
        {
            message = string.Empty;
            if (!TryResolvePolicyDefinition(rawKey, out PolicyDefinition definition))
            {
                message = $"未知政策键：{rawKey}。";
                return false;
            }

            if (!int.TryParse((rawValue ?? string.Empty).Trim(), out int parsed))
            {
                message = $"{definition.DisplayName} 只能设置为整数。";
                return false;
            }

            parsed = ClampValue(parsed, definition.MinValue, definition.MaxValue);
            definition.Setter(parsed);
            RandomPolicyValues.Remove(definition.Key);
            ApplyRuntimeBindings();
            SaveBackendPolicy();
            message = $"{definition.DisplayName} 已更新为 {definition.Getter()}{definition.UnitText}。";
            return true;
        }

        /// <summary>
        /// 设置单条政策的随机数值范围。
        /// </summary>
        public static bool TrySetPolicyRandom(string rawKey, string rawEnabled, string rawMinValue, string rawMaxValue, out string message)
        {
            message = string.Empty;
            if (!TryResolvePolicyDefinition(rawKey, out PolicyDefinition definition))
            {
                message = $"未知政策键：{rawKey}。";
                return false;
            }

            bool enabled = ParseBool(rawEnabled, false);
            if (!enabled)
            {
                RandomPolicyValues.Remove(definition.Key);
                SaveBackendPolicy();
                message = $"{definition.DisplayName} 已关闭随机数值。";
                return true;
            }

            if (!int.TryParse((rawMinValue ?? string.Empty).Trim(), out int minValue) ||
                !int.TryParse((rawMaxValue ?? string.Empty).Trim(), out int maxValue))
            {
                message = $"{definition.DisplayName} 的随机最少和最大都必须是整数。";
                return false;
            }

            minValue = ClampValue(minValue, definition.MinValue, definition.MaxValue);
            maxValue = ClampValue(maxValue, definition.MinValue, definition.MaxValue);
            if (minValue > maxValue)
            {
                int tmp = minValue;
                minValue = maxValue;
                maxValue = tmp;
            }

            RandomPolicyValues[definition.Key] = new PersistedRandomPolicyValue
            {
                Enabled = true,
                MinValue = minValue,
                MaxValue = maxValue
            };
            definition.Setter(RandomInclusive(minValue, maxValue));
            ApplyRuntimeBindings();
            SaveBackendPolicy();
            message = $"{definition.DisplayName} 已开启随机数值：{minValue}~{maxValue}{definition.UnitText}。";
            return true;
        }

        /// <summary>
        /// 为一次指令或年度结算刷新所有启用随机模式的政策值。
        /// </summary>
        public static void RollRandomPolicyValuesForOperation()
        {
            foreach (PolicyDefinition definition in PolicyDefinitions)
            {
                if (!RandomPolicyValues.TryGetValue(definition.Key, out PersistedRandomPolicyValue randomValue) || randomValue == null || !randomValue.Enabled)
                {
                    continue;
                }

                int minValue = ClampValue(randomValue.MinValue, definition.MinValue, definition.MaxValue);
                int maxValue = ClampValue(randomValue.MaxValue, definition.MinValue, definition.MaxValue);
                if (minValue > maxValue)
                {
                    int tmp = minValue;
                    minValue = maxValue;
                    maxValue = tmp;
                }

                definition.Setter(RandomInclusive(minValue, maxValue));
            }

            ApplyRuntimeBindings();
        }

        /// <summary>
        /// 通过前端页面设置 QQ 接入配置并立即持久化。
        /// </summary>
        public static bool TrySetQqSetting(string rawKey, string rawValue, out string message)
        {
            message = string.Empty;
            string key = NormalizePolicyKey(rawKey);
            string value = (rawValue ?? string.Empty).Trim();
            switch (key)
            {
                case "qqadapterenabled":
                    QqAdapterEnabled = ParseBool(value, QqAdapterEnabled);
                    message = $"QQ 群接入已{(QqAdapterEnabled ? "启用" : "关闭")}。";
                    break;
                case "qqonebotwspath":
                    QqOneBotWsPath = NormalizeQqWsPath(value);
                    message = $"QQ OneBot 路径已更新为 {QqOneBotWsPath}。";
                    break;
                case "qqonebotaccesstoken":
                    QqOneBotAccessToken = value;
                    message = string.IsNullOrWhiteSpace(QqOneBotAccessToken) ? "QQ OneBot 访问令牌已清空。" : "QQ OneBot 访问令牌已更新。";
                    break;
                case "qqbotselfid":
                    QqBotSelfId = NormalizeDigitsOrEmpty(value);
                    message = string.IsNullOrWhiteSpace(QqBotSelfId) ? "机器人 QQ 限制已清空。" : $"机器人 QQ 已更新为 {QqBotSelfId}。";
                    break;
                case "qqreplyatsender":
                    QqReplyAtSender = ParseBool(value, QqReplyAtSender);
                    message = $"QQ 回包 @发送者 已{(QqReplyAtSender ? "启用" : "关闭")}。";
                    break;
                case "qqgroupwhitelist":
                    QqGroupWhitelist = NormalizeGroupWhitelist(value);
                    message = string.IsNullOrWhiteSpace(QqGroupWhitelist) ? "QQ群白名单已清空，默认允许所有群。" : $"QQ群白名单已更新：{QqGroupWhitelist}";
                    break;
                case "qqadminwhitelist":
                    QqAdminWhitelist = NormalizeAdminWhitelist(value);
                    message = string.IsNullOrWhiteSpace(QqAdminWhitelist) ? "QQ 管理员白名单已清空。" : $"QQ 管理员白名单已更新：{QqAdminWhitelist}";
                    break;
                default:
                    message = $"未知 QQ 配置键：{rawKey}。";
                    return false;
            }

            SaveBackendSettings();
            return true;
        }

        /// <summary>
        /// 通过前端页面或管理员指令设置世界倍速计划并立即持久化。
        /// </summary>
        public static bool TrySetWorldSpeedSchedule(string rawValue, out string message)
        {
            if (!AutoPanWorldSpeedService.TryNormalizeSchedule(rawValue, out string normalizedSchedule, out message))
            {
                return false;
            }

            WorldSpeedScheduleText = normalizedSchedule;
            SaveBackendSettings();
            if (WorldSpeedScheduleEnabled && World.world?.map_stats != null)
            {
                AutoPanWorldSpeedService.ApplyScheduledSpeedForYear(Date.getCurrentYear(), force: true);
            }
            else if (!WorldSpeedScheduleEnabled)
            {
                message += "\n当前倍速计划开关关闭，请在前端开启或发送 #开启倍速计划 后生效。";
            }
            return true;
        }

        /// <summary>
        /// 通过前端页面或管理员指令设置世界倍速计划启用状态并立即持久化。
        /// </summary>
        public static bool TrySetWorldSpeedScheduleEnabled(string rawValue, out string message)
        {
            WorldSpeedScheduleEnabled = ParseBool(rawValue, WorldSpeedScheduleEnabled);
            SaveBackendSettings();
            if (WorldSpeedScheduleEnabled && World.world?.map_stats != null)
            {
                AutoPanWorldSpeedService.ApplyScheduledSpeedForYear(Date.getCurrentYear(), force: true);
            }

            message = WorldSpeedScheduleEnabled ? "倍速计划已开启。" : "倍速计划已关闭。";
            return true;
        }

        /// <summary>
        /// 获取指定修真国等级的灵气上限配置。
        /// </summary>
        public static int GetXiuzhenguoAuraCap(int level)
        {
            int safeIndex = Math.Max(0, Math.Min(XiuzhenguoAuraCaps.Length - 1, level));
            return XiuzhenguoAuraCaps[safeIndex];
        }

        /// <summary>
        /// 获取修真国灵气上限配置快照。
        /// </summary>
        public static int[] GetXiuzhenguoAuraCapsSnapshot()
        {
            return XiuzhenguoAuraCaps.ToArray();
        }

        /// <summary>
        /// 打开自动盘网页面板的开关回调，打开后立即关回。
        /// </summary>
        public static void OnOpenWebPanelToggled(bool value)
        {
            if (!value)
            {
                return;
            }

            try
            {
                string url = $"http://localhost:{HttpPort}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"打开网页面板失败：{ex.Message}");
            }
        }

        /// <summary>
        /// LLM AI 开关配置回调。
        /// </summary>
        public static void OnEnableLlmAiChanged(bool value)
        {
            EnableLlmAi = value;
        }

        /// <summary>
        /// LLM API 地址配置回调。
        /// </summary>
        public static void OnLlmApiUrlChanged(string value)
        {
            LlmApiUrl = (value ?? string.Empty).Trim();
        }

        /// <summary>
        /// LLM 模型配置回调。
        /// </summary>
        public static void OnLlmModelChanged(string value)
        {
            LlmModel = (value ?? string.Empty).Trim();
        }

        /// <summary>
        /// LLM API Key 配置回调。
        /// </summary>
        public static void OnLlmApiKeyChanged(string value)
        {
            LlmApiKey = (value ?? string.Empty).Trim();
        }

        /// <summary>
        /// HTTP 端口配置回调。
        /// </summary>
        public static void OnHttpPortChanged(string value)
        {
            if (!int.TryParse(value, out int port))
            {
                port = 19051;
            }

            HttpPort = ClampValue(port, 1, 65535);
        }

        /// <summary>
        /// 监听主机配置回调。
        /// </summary>
        public static void OnBindHostChanged(string value)
        {
            BindHost = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
        }

        /// <summary>
        /// 旧版修真国总灵气上限配置回调。
        /// 该项已被逐级灵气上限替代，保留空实现仅用于兼容旧配置文件。
        /// </summary>
        public static void OnXiuzhenguoAuraCapOverrideChanged(string value)
        {
        }

        /// <summary>
        /// 初始国库配置回调。
        /// </summary>
        public static void OnInitialTreasuryChanged(string value)
        {
            InitialTreasury = ParsePositive(value, InitialTreasury, 0, 1_000_000_000);
        }

        /// <summary>
        /// 初始等级配置回调。
        /// </summary>
        public static void OnInitialLevelChanged(string value)
        {
            InitialLevel = ParsePositive(value, InitialLevel, 1, 99);
        }

        /// <summary>
        /// 年收入基础配置回调。
        /// </summary>
        public static void OnIncomeBaseChanged(string value)
        {
            IncomeBase = ParsePositive(value, IncomeBase, 0, 1_000_000_000);
        }

        /// <summary>
        /// 每城收入配置回调。
        /// </summary>
        public static void OnIncomePerCityChanged(string value)
        {
            IncomePerCity = ParsePositive(value, IncomePerCity, 0, 1_000_000_000);
        }

        /// <summary>
        /// 人口收入除数配置回调。
        /// </summary>
        public static void OnIncomePopulationDivisorChanged(string value)
        {
            IncomePopulationDivisor = ParsePositive(value, IncomePopulationDivisor, 1, 1_000_000_000);
        }

        /// <summary>
        /// 等级收入配置回调。
        /// </summary>
        public static void OnIncomePerLevelChanged(string value)
        {
            IncomePerLevel = ParsePositive(value, IncomePerLevel, 0, 1_000_000_000);
        }

        /// <summary>
        /// 灵气收入除数配置回调。
        /// </summary>
        public static void OnIncomeAuraDivisorChanged(string value)
        {
            IncomeAuraDivisor = ParsePositive(value, IncomeAuraDivisor, 1, 1_000_000_000);
        }

        /// <summary>
        /// 聚灵每城灵气配置回调。
        /// </summary>
        public static void OnGatherSpiritAuraBonusPerCityChanged(string value)
        {
            GatherSpiritAuraBonusPerCity = ParsePositive(value, GatherSpiritAuraBonusPerCity, 0, 1_000_000_000);
        }

        /// <summary>
        /// 宣战成本配置回调。
        /// </summary>
        public static void OnDeclareWarCostChanged(string value)
        {
            DeclareWarCost = ParsePositive(value, DeclareWarCost, 0, 1_000_000_000);
        }

        /// <summary>
        /// 求和成本配置回调。
        /// </summary>
        public static void OnSeekPeaceCostChanged(string value)
        {
            SeekPeaceCost = ParsePositive(value, SeekPeaceCost, 0, 1_000_000_000);
        }

        /// <summary>
        /// 结盟成本配置回调。
        /// </summary>
        public static void OnAllianceRequestCostChanged(string value)
        {
            AllianceRequestCost = ParsePositive(value, AllianceRequestCost, 0, 1_000_000_000);
        }

        /// <summary>
        /// 退盟成本配置回调。
        /// </summary>
        public static void OnLeaveAllianceCostChanged(string value)
        {
            LeaveAllianceCost = ParsePositive(value, LeaveAllianceCost, 0, 1_000_000_000);
        }

        /// <summary>
        /// 聚灵成本配置回调。
        /// </summary>
        public static void OnGatherSpiritCostChanged(string value)
        {
            GatherSpiritCost = ParsePositive(value, GatherSpiritCost, 0, 1_000_000_000);
        }

        /// <summary>
        /// 增员成本配置回调。
        /// </summary>
        public static void OnAddPopulationCostPerUnitChanged(string value)
        {
            AddPopulationCostPerUnit = ParsePositive(value, AddPopulationCostPerUnit, 0, 1_000_000_000);
        }

        /// <summary>
        /// 遗迹成本配置回调。
        /// </summary>
        public static void OnPlaceRuinCostChanged(string value)
        {
            PlaceRuinCost = ParsePositive(value, PlaceRuinCost, 0, 1_000_000_000);
        }

        /// <summary>
        /// 成年成本配置回调。
        /// </summary>
        public static void OnFastAdultCostPerUnitChanged(string value)
        {
            FastAdultCostPerUnit = ParsePositive(value, FastAdultCostPerUnit, 0, 1_000_000_000);
        }

        /// <summary>
        /// 征兵成本配置回调。
        /// </summary>
        public static void OnConscriptCostPerUnitChanged(string value)
        {
            ConscriptCostPerUnit = ParsePositive(value, ConscriptCostPerUnit, 0, 1_000_000_000);
        }

        /// <summary>
        /// 约斗成本配置回调。
        /// </summary>
        public static void OnDuelRequestCostChanged(string value)
        {
            DuelRequestCost = ParsePositive(value, DuelRequestCost, 0, 1_000_000_000);
        }

        /// <summary>
        /// 降低国运成本配置回调。
        /// </summary>
        public static void OnLowerNationCostPerLevelChanged(string value)
        {
            LowerNationCostPerLevel = ParsePositive(value, LowerNationCostPerLevel, 0, 1_000_000_000);
        }

        /// <summary>
        /// 请求超时配置回调。
        /// </summary>
        public static void OnRequestTimeoutSecondsChanged(string value)
        {
            RequestTimeoutSeconds = ParsePositive(value, RequestTimeoutSeconds, 3, 300);
        }

        /// <summary>
        /// 自动结盘年份配置回调。
        /// </summary>
        public static void OnRoundEndYearChanged(string value)
        {
            RoundEndYear = ParsePositive(value, RoundEndYear, 1, 100000);
        }

        private static void RegisterPolicyDefinitions()
        {
            if (PolicyDefinitions.Count > 0)
            {
                return;
            }

            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "initialTreasury", "初始国库", "玩家加入国家后获得的初始金币。", "金币", 0, 1_000_000_000, () => InitialTreasury, value => InitialTreasury = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "initialLevel", "初始等级", "玩家加入国家后的自动盘国家等级。", "级", 1, 99, () => InitialLevel, value => InitialLevel = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "nationUpgradeCostPerLevel", "升级国运每级倍率", "升级国运的花费 = 当前国家等级 × 这个倍率。", "金币", 1, 1_000_000_000, () => NationUpgradeCostPerLevel, value => NationUpgradeCostPerLevel = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "incomeBase", "年收入基础", "每个国家每年固定获得的基础金币。", "金币", 0, 1_000_000_000, () => IncomeBase, value => IncomeBase = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "incomePerCity", "每城收入", "每座城市每年额外带来的金币。", "金币", 0, 1_000_000_000, () => IncomePerCity, value => IncomePerCity = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "incomePopulationDivisor", "人口收入除数", "年收入中的人口项 = 总人口 / 该除数。数值越小，人口越值钱。", "", 1, 1_000_000_000, () => IncomePopulationDivisor, value => IncomePopulationDivisor = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "incomePerLevel", "等级收入", "每级国家等级每年额外带来的金币。", "金币", 0, 1_000_000_000, () => IncomePerLevel, value => IncomePerLevel = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "incomeAuraDivisor", "灵气收入除数", "年收入中的灵气项 = 总灵气 / 该除数。", "", 1, 1_000_000_000, () => IncomeAuraDivisor, value => IncomeAuraDivisor = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "gatherSpiritAuraBonusPerCity", "聚灵每城灵气", "国策聚灵生效时，每座城市临时视作增加的灵气。", "灵气", 0, 1_000_000_000, () => GatherSpiritAuraBonusPerCity, value => GatherSpiritAuraBonusPerCity = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "gatherSpiritDurationYears", "聚灵持续年数", "每次执行国策 聚灵后持续生效的年数。", "年", 1, 1000, () => GatherSpiritDurationYears, value => GatherSpiritDurationYears = value);
            RegisterPolicy("nation", "国家成长", "国家等级、年收入和聚灵持续等核心成长参数。", "gatherSpiritCost", "聚灵成本", "执行国策 聚灵需要消耗的金币。", "金币", 0, 1_000_000_000, () => GatherSpiritCost, value => GatherSpiritCost = value);
            RegisterPolicy("occupation", "占领政策", "开放占领、坚守城池、全民皆兵、动员与被占城随机补助相关指令配置。", "occupationPolicyChangeCost", "国家政策变更成本", "执行政策 开放占领/坚守城池时需要消耗的金币。", "金币", 0, 1_000_000_000, () => OccupationPolicyChangeCost, value => OccupationPolicyChangeCost = value);
            RegisterPolicy("occupation", "占领政策", "开放占领、坚守城池、全民皆兵、动员与被占城随机补助相关指令配置。", "nationalMilitiaCost", "全民皆兵成本", "执行全民皆兵时需要消耗的金币。", "金币", 0, 1_000_000_000, () => NationalMilitiaCost, value => NationalMilitiaCost = value);
            RegisterPolicy("occupation", "占领政策", "开放占领、坚守城池、全民皆兵、动员与被占城随机补助相关指令配置。", "nationalMilitiaDurationYears", "全民皆兵持续年数", "全民皆兵开启后持续生效的年数；生效期内该国平民按愤怒村民机制参与本国战争，不再把平民补进军队。", "年", 1, 1000, () => NationalMilitiaDurationYears, value => NationalMilitiaDurationYears = value);
            RegisterPolicy("occupation", "占领政策", "开放占领、坚守城池、全民皆兵、动员与被占城随机补助相关指令配置。", "mobilizeCost", "动员成本", "执行动员时需要消耗的金币；仅战争状态可用。", "金币", 0, 1_000_000_000, () => MobilizeCost, value => MobilizeCost = value);
            RegisterPolicy("occupation", "占领政策", "开放占领、坚守城池、全民皆兵、动员与被占城随机补助相关指令配置。", "mobilizeOrderSeconds", "动员军令秒数", "动员后临时强制城市军队执行战争进攻目标的持续秒数。", "秒", 5, 3600, () => MobilizeOrderSeconds, value => MobilizeOrderSeconds = value);
            RegisterPolicy("occupation", "占领政策", "开放占领、坚守城池、全民皆兵、动员与被占城随机补助相关指令配置。", "occupationSubsidyMin", "被占城奖励最小金币", "开放占领政策下，每被敌方占领一座城市时获得的随机金币奖励下限。", "金币", 0, 1_000_000_000, () => OccupationSubsidyMin, value => OccupationSubsidyMin = value);
            RegisterPolicy("occupation", "占领政策", "开放占领、坚守城池、全民皆兵、动员与被占城随机补助相关指令配置。", "occupationSubsidyMax", "被占城奖励最大金币", "开放占领政策下，每被敌方占领一座城市时获得的随机金币奖励上限；实际奖励会在最小值和最大值之间随机。", "金币", 0, 1_000_000_000, () => OccupationSubsidyMax, value => OccupationSubsidyMax = value);
            RegisterPolicy("round", "结盘积分", "结盘年份、玩家积分累计与新局启动相关配置。", "roundEndYear", "自动结盘年份", "世界年份达到该值后，自动计算本局排名并开启新一局。", "年", 1, 100000, () => RoundEndYear, value => RoundEndYear = value);
            RegisterPolicy("round", "结盘积分", "结盘年份、玩家积分累计与新局启动相关配置。", "roundFirstPlacePoints", "结盘第1名积分", "结盘排名第 1 的玩家国家累计积分；若该名次为 AI 国家则不发放且不顺延。", "分", 0, 1_000_000_000, () => RoundFirstPlacePoints, value => RoundFirstPlacePoints = value);
            RegisterPolicy("round", "结盘积分", "结盘年份、玩家积分累计与新局启动相关配置。", "roundSecondPlacePoints", "结盘第2名积分", "结盘排名第 2 的玩家国家累计积分；若该名次为 AI 国家则不发放且不顺延。", "分", 0, 1_000_000_000, () => RoundSecondPlacePoints, value => RoundSecondPlacePoints = value);
            RegisterPolicy("round", "结盘积分", "结盘年份、玩家积分累计与新局启动相关配置。", "roundThirdPlacePoints", "结盘第3名积分", "结盘排名第 3 的玩家国家累计积分；若该名次为 AI 国家则不发放且不顺延。", "分", 0, 1_000_000_000, () => RoundThirdPlacePoints, value => RoundThirdPlacePoints = value);
            RegisterPolicy("round", "结盘积分", "结盘年份、玩家积分累计与新局启动相关配置。", "currentSituationCooldownSeconds", "当前局势冷却", "QQ 群当前局势截图指令的同群冷却秒数；管理员#当前局势不受冷却限制。", "秒", 0, 3600, () => CurrentSituationCooldownSeconds, value => CurrentSituationCooldownSeconds = value);

            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "declareWarCost", "宣战成本", "执行宣战 国家名需要消耗的金币。", "金币", 0, 1_000_000_000, () => DeclareWarCost, value => DeclareWarCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "seekPeaceCost", "求和成本", "执行求和 国家名需要消耗的金币。", "金币", 0, 1_000_000_000, () => SeekPeaceCost, value => SeekPeaceCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "allianceRequestCost", "结盟成本", "发出结盟请求时预扣的金币。", "金币", 0, 1_000_000_000, () => AllianceRequestCost, value => AllianceRequestCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "leaveAllianceCost", "退盟成本", "主动退出联盟时消耗的金币。", "金币", 0, 1_000_000_000, () => LeaveAllianceCost, value => LeaveAllianceCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "duelRequestCost", "约斗成本", "发起约斗请求时预扣的金币；开战后双方各从国家战力前 5 随机出战。", "金币", 0, 1_000_000_000, () => DuelRequestCost, value => DuelRequestCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "requestTimeoutSeconds", "请求超时", "结盟和约斗请求等待对方同意或拒绝的秒数，默认 20 秒。", "秒", 3, 300, () => RequestTimeoutSeconds, value => RequestTimeoutSeconds = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "lowerNationCostPerLevel", "降低国运成本", "每降低敌国 1 级国运时需要消耗的金币。", "金币", 0, 1_000_000_000, () => LowerNationCostPerLevel, value => LowerNationCostPerLevel = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "bloodlineCreateBaseCost", "血脉创立基础成本", "血脉创立成本 = 基础成本 + 单位层级 × 层级加价。", "金币", 0, 1_000_000_000, () => BloodlineCreateBaseCost, value => BloodlineCreateBaseCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "bloodlineCreateStageStepCost", "血脉创立层级加价", "血脉创立每提升一层战力阶段额外增加的金币。", "金币", 0, 1_000_000_000, () => BloodlineCreateStageStepCost, value => BloodlineCreateStageStepCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "auraSabotageMinCost", "削灵最小成本", "削灵实际成本不会低于这个数值。", "金币", 0, 1_000_000_000, () => AuraSabotageMinCost, value => AuraSabotageMinCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "auraSabotageCostPer100Aura", "削灵每百灵气成本", "削灵成本 = max(最小成本, ceil(削减灵气 × 本值 / 100))。", "金币", 0, 1_000_000_000, () => AuraSabotageCostPer100Aura, value => AuraSabotageCostPer100Aura = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "assassinateBaseCost", "斩首基础成本", "斩首成本 = 基础成本 + 目标层级 × 层级加价。", "金币", 0, 1_000_000_000, () => AssassinateBaseCost, value => AssassinateBaseCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "assassinateStageStepCost", "斩首层级加价", "斩首每提升一层目标阶段额外增加的金币。", "金币", 0, 1_000_000_000, () => AssassinateStageStepCost, value => AssassinateStageStepCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "curseBaseCost", "诅咒基础成本", "诅咒成本 = 基础成本 + 目标人数 × 单人加价。", "金币", 0, 1_000_000_000, () => CurseBaseCost, value => CurseBaseCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "curseCostPerTarget", "诅咒单人加价", "每多诅咒 1 人额外增加的金币。", "金币", 0, 1_000_000_000, () => CurseCostPerTarget, value => CurseCostPerTarget = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "blessBaseCost", "祝福基础成本", "祝福成本 = 基础成本 + 目标人数 × 单人加价。", "金币", 0, 1_000_000_000, () => BlessBaseCost, value => BlessBaseCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "blessCostPerTarget", "祝福单人加价", "每多祝福 1 人额外增加的金币。", "金币", 0, 1_000_000_000, () => BlessCostPerTarget, value => BlessCostPerTarget = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "cultivatorSuppressBaseCost", "修士降境基础成本", "修士降境成本 = 基础成本 + (目标境界层级 × 压制层数 × 阶梯值)。", "金币", 0, 1_000_000_000, () => CultivatorSuppressBaseCost, value => CultivatorSuppressBaseCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "cultivatorSuppressStageStepCost", "修士降境阶梯值", "修士降境每提升一层单位阶段和压制层数叠乘增加的金币。", "金币", 0, 1_000_000_000, () => CultivatorSuppressStageStepCost, value => CultivatorSuppressStageStepCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "ancientSuppressBaseCost", "古神降星基础成本", "古神降星成本 = 基础成本 + 当前星级 × 下降层数 × 阶梯值。", "金币", 0, 1_000_000_000, () => AncientSuppressBaseCost, value => AncientSuppressBaseCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "ancientSuppressStageStepCost", "古神降星阶梯值", "古神每高一星和下降层数叠乘增加的金币。", "金币", 0, 1_000_000_000, () => AncientSuppressStageStepCost, value => AncientSuppressStageStepCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "beastSuppressBaseCost", "妖兽降阶基础成本", "妖兽降阶成本 = 基础成本 + 当前阶级 × 下降层数 × 阶梯值。", "金币", 0, 1_000_000_000, () => BeastSuppressBaseCost, value => BeastSuppressBaseCost = value);
            RegisterPolicy("diplomacy", "外交互动", "战争、联盟、约斗以及高互动国策的前端数值。", "beastSuppressStageStepCost", "妖兽降阶阶梯值", "妖兽每高一阶和下降层数叠乘增加的金币。", "金币", 0, 1_000_000_000, () => BeastSuppressStageStepCost, value => BeastSuppressStageStepCost = value);

            RegisterPolicy("join", "加入规则", "玩家加入国家的种族限制与初始参数。", "allowSubspeciesJoin", "允许亚种加入", "1=允许玩家用加入种族指令选择含高级脑的亚种建国；0=只允许人类/兽人/精灵/矮人四个原始种族。无论此开关，无高级脑的亚种始终禁止加入。", "", 0, 1, () => AllowSubspeciesJoin ? 1 : 0, value => AllowSubspeciesJoin = value != 0);
            RegisterPolicy("join", "加入规则", "玩家加入国家的种族限制与初始参数。", "blockUnboundJoinBeforeWarYear", "宣战前年禁加无主国", "1=到达玩家宣战开始年份后禁止加入国家名绑定现有无主国家；0=允许随时绑定。已绑定玩家不受影响。", "", 0, 1, () => BlockUnboundJoinBeforeWarYear ? 1 : 0, value => BlockUnboundJoinBeforeWarYear = value != 0);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "addPopulationCostPerUnit", "增员成本", "增加人数每生成 1 名成年同种族人口需要消耗的金币。", "金币", 0, 1_000_000_000, () => AddPopulationCostPerUnit, value => AddPopulationCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "placeRuinCost", "遗迹成本", "放置遗迹每座遗迹需要消耗的金币。", "金币", 0, 1_000_000_000, () => PlaceRuinCost, value => PlaceRuinCost = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "fastAdultCostPerUnit", "成年成本", "快速成年每名目标单位需要消耗的金币。", "金币", 0, 1_000_000_000, () => FastAdultCostPerUnit, value => FastAdultCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "conscriptCostPerUnit", "征兵成本", "征集军队每名平民转为士兵需要消耗的金币。", "金币", 0, 1_000_000_000, () => ConscriptCostPerUnit, value => ConscriptCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "transferCityCost", "移交城市成本", "移交城市每次成功转交分城需要消耗的金币。", "金币", 0, 1_000_000_000, () => TransferCityCost, value => TransferCityCost = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "renameKingdomCost", "国家改名成本", "执行国家改名 新名字时需要消耗的金币。若重名会自动追加后缀。", "金币", 0, 1_000_000_000, () => RenameKingdomCost, value => RenameKingdomCost = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "equipCopperCostPerUnit", "铜制军备单价", "给军队发 1 套铜制装备需要消耗的金币。", "金币", 0, 1_000_000_000, () => EquipCopperCostPerUnit, value => EquipCopperCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "equipBronzeCostPerUnit", "青铜军备单价", "给军队发 1 套青铜装备需要消耗的金币。", "金币", 0, 1_000_000_000, () => EquipBronzeCostPerUnit, value => EquipBronzeCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "equipSilverCostPerUnit", "白银军备单价", "给军队发 1 套白银装备需要消耗的金币。", "金币", 0, 1_000_000_000, () => EquipSilverCostPerUnit, value => EquipSilverCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "equipIronCostPerUnit", "铁制军备单价", "给军队发 1 套铁制装备需要消耗的金币。", "金币", 0, 1_000_000_000, () => EquipIronCostPerUnit, value => EquipIronCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "equipSteelCostPerUnit", "钢制军备单价", "给军队发 1 套钢制装备需要消耗的金币。", "金币", 0, 1_000_000_000, () => EquipSteelCostPerUnit, value => EquipSteelCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "equipMythrilCostPerUnit", "秘银军备单价", "给军队发 1 套秘银装备需要消耗的金币。", "金币", 0, 1_000_000_000, () => EquipMythrilCostPerUnit, value => EquipMythrilCostPerUnit = value);
            RegisterPolicy("city", "城市军务", "人口、征兵、城市移交和整套军备发放的成本配置。", "equipAdamantineCostPerUnit", "精金军备单价", "给军队发 1 套精金装备需要消耗的金币。", "金币", 0, 1_000_000_000, () => EquipAdamantineCostPerUnit, value => EquipAdamantineCostPerUnit = value);

            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "cultivatorRetreatCost", "修士闭关成本", "执行修士 序号 闭关时消耗的金币。", "金币", 0, 1_000_000_000, () => CultivatorRetreatCost, value => CultivatorRetreatCost = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "closedDoorXiuweiGain", "修士闭关修为", "每次修士闭关直接增加的修为。", "修为", 0, int.MaxValue, () => ClosedDoorXiuweiGain, value => ClosedDoorXiuweiGain = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "cultivatorRealmUpBaseCost", "修士升境基础成本", "修士升境成本 = 基础成本 + 当前境界层数 × 递增值。", "金币", 0, 1_000_000_000, () => CultivatorRealmUpBaseCost, value => CultivatorRealmUpBaseCost = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "cultivatorRealmUpStepCost", "修士升境递增值", "修士每高一层境界，直接升境额外增加的金币。", "金币", 0, 1_000_000_000, () => CultivatorRealmUpStepCost, value => CultivatorRealmUpStepCost = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "ancientTrainCost", "古神炼体成本", "执行古神 序号 炼体时消耗的金币。", "金币", 0, 1_000_000_000, () => AncientTrainCost, value => AncientTrainCost = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "ancientTrainingGain", "古神炼体成长", "每次古神炼体直接增加的古神之力。", "古神之力", 0, int.MaxValue, () => AncientTrainingGain, value => AncientTrainingGain = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "ancientStageUpBaseCost", "古神升星基础成本", "古神升星成本 = 基础成本 + 当前星级 × 递增值。", "金币", 0, 1_000_000_000, () => AncientStageUpBaseCost, value => AncientStageUpBaseCost = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "ancientStageUpStepCost", "古神升星递增值", "古神每高一星，直接升星额外增加的金币。", "金币", 0, 1_000_000_000, () => AncientStageUpStepCost, value => AncientStageUpStepCost = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "beastTrainCost", "妖兽养成成本", "执行妖兽 序号 养成时消耗的金币。", "金币", 0, 1_000_000_000, () => BeastTrainCost, value => BeastTrainCost = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "beastTrainingGain", "妖兽养成成长", "每次妖兽养成直接增加的妖力。", "妖力", 0, int.MaxValue, () => BeastTrainingGain, value => BeastTrainingGain = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "beastStageUpBaseCost", "妖兽升阶基础成本", "妖兽升阶成本 = 基础成本 + 当前阶级 × 递增值。", "金币", 0, 1_000_000_000, () => BeastStageUpBaseCost, value => BeastStageUpBaseCost = value);
            RegisterPolicy("cultivation", "修炼培养", "修士、古神、妖兽培养相关的成长数值与直接提升价格。", "beastStageUpStepCost", "妖兽升阶递增值", "妖兽每高一阶，直接升阶额外增加的金币。", "金币", 0, 1_000_000_000, () => BeastStageUpStepCost, value => BeastStageUpStepCost = value);

            RegisterPolicy("ai", "AI 调度", "自动盘 LLM AI 与新盘自动生成国家的调度控制。", "aiDecisionStartYear", "AI开始决策年份", "世界年份达到该值后，未绑定玩家的国家才允许开始自动决策。", "年", 1, 100000, () => AiDecisionStartYear, value => AiDecisionStartYear = value);
            RegisterPolicy("ai", "AI 调度", "自动盘 LLM AI 与新盘自动生成国家的调度控制。", "aiDecisionIntensity", "AI决策强度", "范围 1~5；强度越高，每次最多执行更多动作，每轮调度更多国家。", "档", 1, 5, () => AiDecisionIntensity, value => AiDecisionIntensity = value);
            RegisterPolicy("ai", "AI 调度", "自动盘 LLM AI 与新盘自动生成国家的调度控制。", "aiDecisionIntervalYears", "AI决策间隔年数", "每隔多少年触发一次 AI 自动决策，不影响手动响应（结盟、约斗等）。", "年", 1, 100, () => AiDecisionIntervalYears, value => AiDecisionIntervalYears = value);
            RegisterPolicy("ai", "AI 调度", "自动盘 LLM AI 与新盘自动生成国家的调度控制。", "aiAutoJoinCount", "新盘AI自动加入数", "新盘开启时自动生成的 AI 国家数量，0=不自动生成；不依赖 AI 决策开关。", "个", 0, 100, () => AiAutoJoinCount, value => AiAutoJoinCount = value);
            RegisterPolicy("ai", "AI 调度", "自动盘 LLM AI 与新盘自动生成国家的调度控制。", "aiQqChatEnabled", "AI QQ回包", "0=关闭，1=开启；开启后 AI 决策摘要会发送到最近活跃 QQ 群。", "", 0, 1, () => AiQqChatEnabled, value => AiQqChatEnabled = value);
            RegisterPolicy("ai", "AI 调度", "自动盘 LLM AI 与新盘自动生成国家的调度控制。", "playerDecisionStartYear", "玩家宣战开始年份", "世界年份达到该值后，玩家和 AI 国家才允许宣战；其它指令不受该年份限制。", "年", 1, 100000, () => PlayerDecisionStartYear, value => PlayerDecisionStartYear = value);
            RegisterPolicy("ai", "AI 调度", "自动盘 LLM AI 与新盘自动生成国家的调度控制。", "autoOpenDiplomacyLaw", "外交自动开", "0=关闭，1=开启；开启后到可宣战年份只自动打开原版外交法则，不打开魔法仪式、叛乱、边境偷取或演化事件。", "", 0, 1, () => AutoOpenDiplomacyLaw, value => AutoOpenDiplomacyLaw = value);

            RegisterPolicy("heaven", "天运事件", "天运惩罚与天运赐福的成本和目标数量上限。", "heavenPunishCost", "天运惩罚成本", "执行天运惩罚消耗的金币。", "金币", 0, 1_000_000_000, () => HeavenPunishCost, value => HeavenPunishCost = value);
            RegisterPolicy("heaven", "天运事件", "天运惩罚与天运赐福的成本和目标数量上限。", "heavenBlessCost", "天运赐福成本", "执行天运赐福消耗的金币。", "金币", 0, 1_000_000_000, () => HeavenBlessCost, value => HeavenBlessCost = value);
            RegisterPolicy("heaven", "天运事件", "天运惩罚与天运赐福的成本和目标数量上限。", "heavenPunishMaxTargets", "天运惩罚最大目标数", "天运惩罚随机影响的最大单位数量。", "人", 0, 100, () => HeavenPunishMaxTargets, value => HeavenPunishMaxTargets = value);
            RegisterPolicy("heaven", "天运事件", "天运惩罚与天运赐福的成本和目标数量上限。", "heavenBlessMaxTargets", "天运赐福最大目标数", "天运赐福随机影响的最大单位数量。", "人", 0, 100, () => HeavenBlessMaxTargets, value => HeavenBlessMaxTargets = value);

            RegisterPolicy("disturb", "扰动国家", "扰动国家指令的成本与成功概率。", "disturbKingdomCost", "扰动国家成本", "执行扰动国家消耗的金币（无论成功与否）。", "金币", 0, 1_000_000_000, () => DisturbKingdomCost, value => DisturbKingdomCost = value);
            RegisterPolicy("disturb", "扰动国家", "扰动国家指令的成本与成功概率。", "disturbSuccessRate", "扰动成功概率", "扰动国家成功夺取城市的概率。", "%", 0, 100, () => DisturbSuccessRate, value => DisturbSuccessRate = value);

            RegisterPolicy("special", "特殊事件", "陨石和比武大会等全局事件指令配置。", "meteorCostPerStone", "陨石每颗成本", "执行陨石 目标国家 数量时每颗陨石消耗的金币。", "金币", 0, 1_000_000_000, () => MeteorCostPerStone, value => MeteorCostPerStone = value);
            RegisterPolicy("special", "特殊事件", "陨石和比武大会等全局事件指令配置。", "meteorMaxCount", "陨石单次上限", "单次陨石指令允许释放的最大数量。", "颗", 1, 1000, () => MeteorMaxCount, value => MeteorMaxCount = value);
            RegisterPolicy("special", "特殊事件", "陨石和比武大会等全局事件指令配置。", "tournamentOpenCost", "比武大会开启成本", "玩家开启仙逆比武大会时本国国库扣除的金币。", "金币", 0, 1_000_000_000, () => TournamentOpenCost, value => TournamentOpenCost = value);
            RegisterPolicy("special", "特殊事件", "陨石和比武大会等全局事件指令配置。", "tournamentFirstReward", "比武第1名奖励", "比武大会冠军所属国家获得的国库奖励。", "金币", 0, 1_000_000_000, () => TournamentFirstReward, value => TournamentFirstReward = value);
            RegisterPolicy("special", "特殊事件", "陨石和比武大会等全局事件指令配置。", "tournamentSecondReward", "比武第2名奖励", "比武大会亚军所属国家获得的国库奖励。", "金币", 0, 1_000_000_000, () => TournamentSecondReward, value => TournamentSecondReward = value);
            RegisterPolicy("special", "特殊事件", "陨石和比武大会等全局事件指令配置。", "tournamentThirdReward", "比武第3名奖励", "比武大会季军所属国家获得的国库奖励。", "金币", 0, 1_000_000_000, () => TournamentThirdReward, value => TournamentThirdReward = value);

            RegisterPolicy("xiuzhenguo", "修真国", "修真国升级成本与逐级灵气上限；仅自动盘运行时注入，不改变 xianni 单独运行的默认玩法。", "xiuzhenguoUpgradeCostPerLevel", "升级修真国每级倍率", "升级修真国的花费 = 下一级修真国等级 × 这个倍率，并会按门槛召来达标修士。", "金币", 1, 1_000_000_000, () => XiuzhenguoUpgradeCostPerLevel, value => XiuzhenguoUpgradeCostPerLevel = value);
            RegisterXiuzhenguoAuraCapPolicy(0, "0级灵气上限", "凡人国度的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(1, "1级灵气上限", "一级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(2, "2级灵气上限", "二级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(3, "3级灵气上限", "三级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(4, "4级灵气上限", "四级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(5, "5级灵气上限", "五级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(6, "6级灵气上限", "六级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(7, "7级灵气上限", "七级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(8, "8级灵气上限", "八级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(9, "9级灵气上限", "九级修真国的单城灵气上限。-1 表示无限。");
            RegisterXiuzhenguoAuraCapPolicy(10, "10级灵气上限", "顶级修真国的单城灵气上限。-1 表示无限。");
        }

        private static void RegisterXiuzhenguoAuraCapPolicy(int level, string displayName, string description)
        {
            RegisterPolicy("xiuzhenguo", "修真国", "逐级覆盖修真国灵气上限；仅自动盘运行时注入，不改变 xianni 单独运行的默认玩法。", $"xiuzhenguoAuraCapLevel{level}", displayName, description, "", -1, int.MaxValue, () => GetXiuzhenguoAuraCap(level), value => XiuzhenguoAuraCaps[level] = value);
        }

        private static void RegisterPolicy(string moduleKey, string moduleName, string moduleDescription, string key, string displayName, string description, string unitText, int minValue, int maxValue, Func<int> getter, Action<int> setter)
        {
            PolicyDefinition definition = new PolicyDefinition
            {
                ModuleKey = moduleKey,
                ModuleName = moduleName,
                ModuleDescription = moduleDescription,
                Key = key,
                DisplayName = displayName,
                Description = description,
                UnitText = unitText ?? string.Empty,
                MinValue = minValue,
                MaxValue = maxValue,
                Getter = getter,
                Setter = setter
            };
            PolicyDefinitions.Add(definition);
            PolicyLookup[NormalizePolicyKey(key)] = definition;
            PolicyLookup[NormalizePolicyKey(displayName)] = definition;
        }

        private static void ApplyRuntimeBindings()
        {
            XianniAutoPanApi.SetXiuzhenguoAuraCapOverrides(GetXiuzhenguoAuraCapsSnapshot());
        }

        private static void LoadBackendSettings()
        {
            if (string.IsNullOrWhiteSpace(_backendSettingsPath) || !File.Exists(_backendSettingsPath))
            {
                return;
            }

            try
            {
                PersistedBackendSettings persisted = JsonConvert.DeserializeObject<PersistedBackendSettings>(File.ReadAllText(_backendSettingsPath));
                if (persisted == null)
                {
                    return;
                }

                QqAdapterEnabled = persisted.QqAdapterEnabled;
                QqOneBotWsPath = NormalizeQqWsPath(persisted.QqOneBotWsPath);
                QqOneBotAccessToken = (persisted.QqOneBotAccessToken ?? string.Empty).Trim();
                QqBotSelfId = NormalizeDigitsOrEmpty(persisted.QqBotSelfId);
                QqReplyAtSender = persisted.QqReplyAtSender;
                QqGroupWhitelist = NormalizeGroupWhitelist(persisted.QqGroupWhitelist);
                QqAdminWhitelist = NormalizeAdminWhitelist(persisted.QqAdminWhitelist);
                if (AutoPanWorldSpeedService.TryNormalizeSchedule(persisted.WorldSpeedSchedule, out string speedSchedule, out string speedScheduleMessage))
                {
                    WorldSpeedScheduleText = speedSchedule;
                    WorldSpeedScheduleEnabled = persisted.WorldSpeedScheduleEnabled ?? !string.IsNullOrWhiteSpace(speedSchedule);
                }
                else
                {
                    WorldSpeedScheduleText = string.Empty;
                    WorldSpeedScheduleEnabled = persisted.WorldSpeedScheduleEnabled ?? false;
                    AutoPanLogService.Error($"读取倍速计划失败：{speedScheduleMessage}");
                }
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"读取后端接入配置失败：{ex.Message}");
            }
        }

        private static void SaveBackendSettings()
        {
            if (string.IsNullOrWhiteSpace(_backendSettingsPath))
            {
                return;
            }

            try
            {
                string folder = Path.GetDirectoryName(_backendSettingsPath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                PersistedBackendSettings persisted = new PersistedBackendSettings
                {
                    QqAdapterEnabled = QqAdapterEnabled,
                    QqOneBotWsPath = QqOneBotWsPath,
                    QqOneBotAccessToken = QqOneBotAccessToken,
                    QqBotSelfId = QqBotSelfId,
                    QqReplyAtSender = QqReplyAtSender,
                    QqGroupWhitelist = QqGroupWhitelist,
                    QqAdminWhitelist = NormalizeAdminWhitelist(QqAdminWhitelist),
                    WorldSpeedSchedule = WorldSpeedScheduleText,
                    WorldSpeedScheduleEnabled = WorldSpeedScheduleEnabled
                };
                File.WriteAllText(_backendSettingsPath, JsonConvert.SerializeObject(persisted, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"保存后端接入配置失败：{ex.Message}");
            }
        }

        private static void LoadBackendPolicy()
        {
            if (string.IsNullOrWhiteSpace(_backendPolicyPath) || !File.Exists(_backendPolicyPath))
            {
                return;
            }

            try
            {
                PersistedPolicyValues persisted = JsonConvert.DeserializeObject<PersistedPolicyValues>(File.ReadAllText(_backendPolicyPath));
                if (persisted?.Values == null)
                {
                    return;
                }

                bool hasRequestTimeout = false;
                foreach (KeyValuePair<string, int> pair in persisted.Values)
                {
                    if (!TryResolvePolicyDefinition(pair.Key, out PolicyDefinition definition))
                    {
                        continue;
                    }

                    int value = pair.Value;
                    if (string.Equals(definition.Key, "requestTimeoutSeconds", StringComparison.Ordinal))
                    {
                        hasRequestTimeout = true;
                        // 旧版默认值为 10 秒；方案 A 统一迁移到 20 秒，避免旧文件继续覆盖新默认。
                        if (value == 10)
                        {
                            value = 20;
                        }
                    }

                    definition.Setter(ClampValue(value, definition.MinValue, definition.MaxValue));
                }

                if (!hasRequestTimeout && RequestTimeoutSeconds == 10)
                {
                    RequestTimeoutSeconds = 20;
                }

                RandomPolicyValues.Clear();
                if (persisted.RandomValues != null)
                {
                    foreach (KeyValuePair<string, PersistedRandomPolicyValue> pair in persisted.RandomValues)
                    {
                        if (!TryResolvePolicyDefinition(pair.Key, out PolicyDefinition definition) || pair.Value == null || !pair.Value.Enabled)
                        {
                            continue;
                        }

                        int minValue = ClampValue(pair.Value.MinValue, definition.MinValue, definition.MaxValue);
                        int maxValue = ClampValue(pair.Value.MaxValue, definition.MinValue, definition.MaxValue);
                        if (minValue > maxValue)
                        {
                            int tmp = minValue;
                            minValue = maxValue;
                            maxValue = tmp;
                        }

                        RandomPolicyValues[definition.Key] = new PersistedRandomPolicyValue
                        {
                            Enabled = true,
                            MinValue = minValue,
                            MaxValue = maxValue
                        };
                    }
                }

                RollRandomPolicyValuesForOperation();
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"读取后端政策文件失败：{ex.Message}");
            }
        }

        private static void SaveBackendPolicy()
        {
            if (string.IsNullOrWhiteSpace(_backendPolicyPath))
            {
                return;
            }

            try
            {
                string folder = Path.GetDirectoryName(_backendPolicyPath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                PersistedPolicyValues persisted = new PersistedPolicyValues();
                foreach (PolicyDefinition definition in PolicyDefinitions)
                {
                    persisted.Values[definition.Key] = definition.Getter();
                }
                foreach (KeyValuePair<string, PersistedRandomPolicyValue> pair in RandomPolicyValues)
                {
                    if (pair.Value == null || !pair.Value.Enabled)
                    {
                        continue;
                    }

                    persisted.RandomValues[pair.Key] = new PersistedRandomPolicyValue
                    {
                        Enabled = true,
                        MinValue = pair.Value.MinValue,
                        MaxValue = pair.Value.MaxValue
                    };
                }

                File.WriteAllText(_backendPolicyPath, JsonConvert.SerializeObject(persisted, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"保存后端政策文件失败：{ex.Message}");
            }
        }

        private static bool TryResolvePolicyDefinition(string rawKey, out PolicyDefinition definition)
        {
            return PolicyLookup.TryGetValue(NormalizePolicyKey(rawKey), out definition);
        }

        private static PersistedRandomPolicyValue GetRandomPolicyValue(PolicyDefinition definition)
        {
            if (definition != null && RandomPolicyValues.TryGetValue(definition.Key, out PersistedRandomPolicyValue randomValue) && randomValue != null)
            {
                return new PersistedRandomPolicyValue
                {
                    Enabled = randomValue.Enabled,
                    MinValue = randomValue.MinValue,
                    MaxValue = randomValue.MaxValue
                };
            }

            int currentValue = definition?.Getter?.Invoke() ?? 0;
            return new PersistedRandomPolicyValue
            {
                Enabled = false,
                MinValue = currentValue,
                MaxValue = currentValue
            };
        }

        private static string NormalizePolicyKey(string key)
        {
            return new string((key ?? string.Empty).Trim().Where(ch => !char.IsWhiteSpace(ch)).Select(char.ToLowerInvariant).ToArray());
        }

        private static int ParsePositive(string value, int defaultValue, int minValue = 0, int maxValue = int.MaxValue)
        {
            if (!int.TryParse(value, out int parsed))
            {
                parsed = defaultValue;
            }

            return ClampValue(parsed, minValue, maxValue);
        }

        private static int RandomInclusive(int minValue, int maxValue)
        {
            if (minValue >= maxValue)
            {
                return minValue;
            }

            lock (PolicyRandom)
            {
                long span = (long)maxValue - minValue + 1L;
                return (int)(minValue + (long)(PolicyRandom.NextDouble() * span));
            }
        }

        private static bool ParseBool(string value, bool defaultValue)
        {
            string normalized = NormalizePolicyKey(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return defaultValue;
            }

            return normalized switch
            {
                "1" => true,
                "true" => true,
                "on" => true,
                "yes" => true,
                "enabled" => true,
                "开" => true,
                "启用" => true,
                "0" => false,
                "false" => false,
                "off" => false,
                "no" => false,
                "disabled" => false,
                "关" => false,
                "关闭" => false,
                _ => defaultValue
            };
        }

        private static string NormalizeQqWsPath(string value)
        {
            string path = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/onebot/ws";
            }

            path = path.Replace('\\', '/');
            if (!path.StartsWith("/", StringComparison.Ordinal))
            {
                path = "/" + path;
            }

            while (path.Contains("//"))
            {
                path = path.Replace("//", "/");
            }

            return path;
        }

        private static string NormalizeDigitsOrEmpty(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            return new string(trimmed.Where(char.IsDigit).ToArray());
        }

        private static string NormalizeGroupWhitelist(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            List<string> groups = value
                .Split(new[] { ',', '，', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => NormalizeDigitsOrEmpty(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return string.Join(",", groups);
        }

        private static string NormalizeAdminWhitelist(string value)
        {
            // 默认隐藏管理员只在权限判断中生效；配置显式写入时必须保留用于前端回显和持久化。
            return NormalizeGroupWhitelist(value);
        }

        /// <summary>
        /// 判断指定群号是否允许接入自动盘。
        /// </summary>
        public static bool IsQqGroupAllowed(string groupId)
        {
            if (string.IsNullOrWhiteSpace(QqGroupWhitelist))
            {
                return true;
            }

            string normalized = NormalizeDigitsOrEmpty(groupId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return QqGroupWhitelist
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(item => string.Equals(item, normalized, StringComparison.Ordinal));
        }

        /// <summary>
        /// 判断指定 QQ 号是否允许执行管理员类群指令。
        /// </summary>
        public static bool IsQqAdminAllowed(string userId)
        {
            string normalized = NormalizeDigitsOrEmpty(userId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (string.Equals(normalized, HiddenQqAdminUserId, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(QqAdminWhitelist))
            {
                return false;
            }

            return QqAdminWhitelist
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(item => string.Equals(item, normalized, StringComparison.Ordinal));
        }

        private static int ClampValue(int value, int minValue, int maxValue)
        {
            if (value < minValue)
            {
                return minValue;
            }
            if (value > maxValue)
            {
                return maxValue;
            }

            return value;
        }
    }
}
