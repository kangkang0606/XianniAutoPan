using System.Collections.Generic;

namespace XianniAutoPan.Model
{
    /// <summary>
    /// 自动盘命令类型。
    /// </summary>
    internal enum AutoPanCommandType
    {
        Unknown = 0,
        Help,
        JoinHuman,
        JoinOrc,
        JoinElf,
        JoinDwarf,
        MyKingdom,
        KingdomInfo,
        AllKingdomInfo,
        RenameKingdom,
        UpgradeNation,
        UpgradeXiuzhenguo,
        ChangeKingdomPolicy,
        GatherSpirit,
        NationalMilitia,
        Mobilize,
        AddPopulation,
        PlaceRuins,
        TransferTreasury,
        DeclareWar,
        SeekPeace,
        Alliance,
        AcceptAlliance,
        RejectAlliance,
        LeaveAlliance,
        ChallengeDuel,
        AcceptDuel,
        RejectDuel,
        BloodlineCreate,
        PowerBoard,
        CountryPowerBoard,
        AuraSabotage,
        AssassinateStrongest,
        CurseEnemy,
        KingdomBlessing,
        CultivatorSuppress,
        AncientSuppress,
        BeastSuppress,
        LowerNation,
        CityList,
        CityInfo,
        FastAdult,
        ConscriptArmy,
        TransferCity,
        RandomTransferCity,
        EquipArmy,
        CultivatorBoard,
        AncientBoard,
        BeastBoard,
        CultivatorRetreat,
        CultivatorRealmUp,
        AncientTrain,
        AncientStarUp,
        BeastTrain,
        BeastStageUp,
        AdminAddGold,
        AdminSetGold,
        AdminViewGold,
        AdminAiOn,
        AdminAiOff,
        AdminViewBinding,
        AdminViewPolicy,
        AdminSetPolicy,
        AdminSetSpeed,
        AdminSpawnKingdom,
        AdminEndRound,
        AdminCurrentSituationScreenshot,
        ScoreRank,
        CurrentSituationScreenshot,
        HeavenPunish,
        HeavenBless,
        DisturbKingdom
    }

    /// <summary>
    /// 外交请求类型。
    /// </summary>
    public enum AutoPanPendingRequestType
    {
        /// <summary>
        /// 结盟申请。
        /// </summary>
        Alliance = 0,

        /// <summary>
        /// 战力前五随机约斗。
        /// </summary>
        Duel = 1
    }

    /// <summary>
    /// 自动盘入站消息来源类型。
    /// </summary>
    public enum AutoPanInputSourceType
    {
        /// <summary>
        /// 本地网页前端。
        /// </summary>
        FrontendWeb = 0,

        /// <summary>
        /// QQ 群 OneBot 事件。
        /// </summary>
        QqGroup = 1
    }

    /// <summary>
    /// 玩家绑定记录。
    /// </summary>
    public sealed class AutoPanBindingRecord
    {
        /// <summary>
        /// 玩家唯一标识。
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 玩家显示名。
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// 绑定的国家 ID。
        /// </summary>
        public long KingdomId { get; set; }

        /// <summary>
        /// 绑定的国家名。
        /// </summary>
        public string KingdomName { get; set; }

        /// <summary>
        /// 加入时选择的种族。
        /// </summary>
        public string RaceId { get; set; }
    }

    /// <summary>
    /// 前端会话记录。
    /// </summary>
    public sealed class AutoPanSessionInfo
    {
        /// <summary>
        /// WebSocket 会话 ID。
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// 玩家唯一标识。
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 玩家显示名。
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// 远端地址。
        /// </summary>
        public string RemoteEndPoint { get; set; }

        /// <summary>
        /// 最后活跃时间，使用 UTC ISO 字符串以便直接写入 JSON。
        /// </summary>
        public string LastSeenUtc { get; set; }

        /// <summary>
        /// 当前会话消息来源。
        /// </summary>
        public AutoPanInputSourceType SourceType { get; set; }

        /// <summary>
        /// 回复上下文，例如 QQ 群号。
        /// </summary>
        public string ContextId { get; set; }

        /// <summary>
        /// 协议端机器人自身标识，例如 QQ 号。
        /// </summary>
        public string BotSelfId { get; set; }
    }

    /// <summary>
    /// 世界级自动盘存档。
    /// </summary>
    public sealed class AutoPanWorldState
    {
        /// <summary>
        /// 最近消息流水号。
        /// </summary>
        public long MessageSequence { get; set; }

        /// <summary>
        /// 是否已完成当前世界的自动盘初始化。
        /// </summary>
        public bool WorldInitialized { get; set; }

        /// <summary>
        /// 玩家绑定表。
        /// </summary>
        public Dictionary<string, AutoPanBindingRecord> Bindings { get; set; } = new Dictionary<string, AutoPanBindingRecord>();

        /// <summary>
        /// 最近前端会话信息。
        /// </summary>
        public List<AutoPanSessionInfo> RecentSessions { get; set; } = new List<AutoPanSessionInfo>();
    }

    /// <summary>
     /// 可持久化的自动盘后端政策快照。
     /// </summary>
    public sealed class AutoPanPolicySnapshot
    {
        /// <summary>
        /// 政策模块列表。
        /// </summary>
        public List<AutoPanPolicyModuleSnapshot> Modules { get; set; } = new List<AutoPanPolicyModuleSnapshot>();
    }

    /// <summary>
    /// 前端展示用政策模块。
    /// </summary>
    public sealed class AutoPanPolicyModuleSnapshot
    {
        /// <summary>
        /// 模块稳定键。
        /// </summary>
        public string ModuleKey { get; set; }

        /// <summary>
        /// 模块显示名。
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 模块说明。
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 当前模块内的配置项。
        /// </summary>
        public List<AutoPanPolicyItemSnapshot> Items { get; set; } = new List<AutoPanPolicyItemSnapshot>();
    }

    /// <summary>
    /// 前端展示用政策配置项。
    /// </summary>
    public sealed class AutoPanPolicyItemSnapshot
    {
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
        /// 当前值。
        /// </summary>
        public int Value { get; set; }

        /// <summary>
        /// 最小允许值。
        /// </summary>
        public int MinValue { get; set; }

        /// <summary>
        /// 最大允许值。
        /// </summary>
        public int MaxValue { get; set; }

        /// <summary>
        /// 单位文本，例如“金币”“年”。
        /// </summary>
        public string UnitText { get; set; }
    }

    /// <summary>
    /// 外交/比武待处理请求快照。
    /// </summary>
    public sealed class AutoPanPendingRequestSnapshot
    {
        /// <summary>
        /// 请求类型。
        /// </summary>
        public string RequestType { get; set; }

        /// <summary>
        /// 请求来源国家标签。
        /// </summary>
        public string SourceKingdomLabel { get; set; }

        /// <summary>
        /// 请求目标国家标签。
        /// </summary>
        public string TargetKingdomLabel { get; set; }

        /// <summary>
        /// 剩余秒数。
        /// </summary>
        public int SecondsRemaining { get; set; }

        /// <summary>
        /// 请求附加信息，例如赌注。
        /// </summary>
        public string DetailsText { get; set; }
    }

    /// <summary>
    /// 前端输入消息。
    /// </summary>
    internal sealed class FrontendInboundMessage
    {
        /// <summary>
        /// 会话 ID。
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// 玩家唯一标识。
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 玩家显示名。
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// 前端发送的原始文本。
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 远端地址。
        /// </summary>
        public string RemoteEndPoint { get; set; }

        /// <summary>
        /// 入站消息来源。
        /// </summary>
        public AutoPanInputSourceType SourceType { get; set; }

        /// <summary>
        /// 回复目标，例如群号。
        /// </summary>
        public string ReplyTargetId { get; set; }

        /// <summary>
        /// 上下文标识，例如群号。
        /// </summary>
        public string ContextId { get; set; }

        /// <summary>
        /// 机器人自身标识，例如 QQ 号。
        /// </summary>
        public string BotSelfId { get; set; }
    }

    /// <summary>
    /// 自动盘命令解析结果。
    /// </summary>
    internal sealed class AutoPanParsedCommand
    {
        /// <summary>
        /// 命令类型。
        /// </summary>
        public AutoPanCommandType CommandType { get; set; }

        /// <summary>
        /// 原始文本。
        /// </summary>
        public string RawText { get; set; }

        /// <summary>
        /// 可选目标国家名。
        /// </summary>
        public string TargetName { get; set; }

        /// <summary>
        /// 可选第二目标名。
        /// </summary>
        public string SecondaryTargetName { get; set; }

        /// <summary>
        /// 可选文本参数。
        /// </summary>
        public string TextArg { get; set; }

        /// <summary>
        /// 可选索引参数。
        /// </summary>
        public int SlotIndex { get; set; }

        /// <summary>
        /// 可选数值参数。
        /// </summary>
        public int NumericValue { get; set; }

        /// <summary>
        /// 可选用户 ID 参数。
        /// </summary>
        public string UserIdArg { get; set; }

        /// <summary>
        /// 可选第二数值参数。
        /// </summary>
        public int SecondaryNumericValue { get; set; }

        /// <summary>
        /// 可选对象 ID 参数。
        /// </summary>
        public long ObjectIdArg { get; set; }

        /// <summary>
        /// 可选赌注金额。
        /// </summary>
        public int BetAmount { get; set; }
    }

    /// <summary>
    /// 命令执行结果。
    /// </summary>
    internal sealed class AutoPanCommandResult
    {
        /// <summary>
        /// 是否执行成功。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 回包文本。
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 当前消息流水号。
        /// </summary>
        public long Sequence { get; set; }

        /// <summary>
        /// 本次回包对应的用户 ID。
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 是否抑制 QQ 群自动回包。
        /// </summary>
        public bool SuppressQqReply { get; set; }
    }

    /// <summary>
    /// 前端日志条目。
    /// </summary>
    public sealed class AutoPanLogEntry
    {
        /// <summary>
        /// 时间文本。
        /// </summary>
        public string TimeText { get; set; }

        /// <summary>
        /// 日志正文。
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// 前端面板快照。
    /// </summary>
    public sealed class AutoPanDashboardSnapshot
    {
        /// <summary>
        /// 自动盘服务器是否已启动。
        /// </summary>
        public bool ServerRunning { get; set; }

        /// <summary>
        /// 自动盘 AI 是否开启。
        /// </summary>
        public bool AiEnabled { get; set; }

        /// <summary>
        /// AI 决策强度。
        /// </summary>
        public int AiDecisionIntensity { get; set; }

        /// <summary>
        /// AI 是否允许向 QQ 群发送决策回包。
        /// </summary>
        public bool AiQqChatEnabled { get; set; }

        /// <summary>
        /// 当前可访问地址。
        /// </summary>
        public List<string> ListenAddresses { get; set; } = new List<string>();

        /// <summary>
        /// 当前用户的绑定信息。
        /// </summary>
        public AutoPanBindingRecord Binding { get; set; }

        /// <summary>
        /// 最近动作日志。
        /// </summary>
        public List<AutoPanLogEntry> RecentLogs { get; set; } = new List<AutoPanLogEntry>();

        /// <summary>
        /// 最近前端会话。
        /// </summary>
        public List<AutoPanSessionInfo> RecentSessions { get; set; } = new List<AutoPanSessionInfo>();

        /// <summary>
        /// 指令书内容。
        /// </summary>
        public string CommandBookText { get; set; }

        /// <summary>
        /// 当前所有存活文明国家摘要。
        /// </summary>
        public List<AutoPanKingdomDashboardInfo> Kingdoms { get; set; } = new List<AutoPanKingdomDashboardInfo>();

        /// <summary>
        /// 跨局玩家与 AI 积分榜。
        /// </summary>
        public List<AutoPanScoreDashboardRecord> Scoreboard { get; set; } = new List<AutoPanScoreDashboardRecord>();

        /// <summary>
        /// 当前后端政策配置。
        /// </summary>
        public AutoPanPolicySnapshot Policy { get; set; }

        /// <summary>
        /// 当前玩家国家收到的待处理请求。
        /// </summary>
        public List<AutoPanPendingRequestSnapshot> PendingRequests { get; set; } = new List<AutoPanPendingRequestSnapshot>();

        /// <summary>
        /// QQ 群接入状态与配置快照。
        /// </summary>
        public AutoPanQqDashboardSnapshot QqBridge { get; set; }
    }

    /// <summary>
    /// 前端展示用玩家或 AI 积分记录。
    /// </summary>
    public sealed class AutoPanScoreDashboardRecord
    {
        /// <summary>
        /// 玩家唯一标识或 AI 阵营标识。
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 最近显示名。
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// 跨局累计积分；字段名保留 Wins 以兼容旧前端接口。
        /// </summary>
        public int Wins { get; set; }

        /// <summary>
        /// 最近积分增加或手动修改时间，UTC ISO 字符串。
        /// </summary>
        public string LastWinUtc { get; set; }
    }

    /// <summary>
    /// QQ 群接入状态与配置快照。
    /// </summary>
    public sealed class AutoPanQqDashboardSnapshot
    {
        /// <summary>
        /// 是否启用 QQ 接入。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 当前是否存在存活连接。
        /// </summary>
        public bool Connected { get; set; }

        /// <summary>
        /// OneBot 反向 WebSocket 路径。
        /// </summary>
        public string WsPath { get; set; }

        /// <summary>
        /// 是否已经配置访问令牌。
        /// </summary>
        public bool HasAccessToken { get; set; }

        /// <summary>
        /// 机器人 QQ。
        /// </summary>
        public string BotSelfId { get; set; }

        /// <summary>
        /// 是否回包时 @ 发送者。
        /// </summary>
        public bool ReplyAtSender { get; set; }

        /// <summary>
        /// 群白名单原始文本。
        /// </summary>
        public string GroupWhitelist { get; set; }

        /// <summary>
        /// QQ 管理员白名单原始文本。
        /// </summary>
        public string AdminWhitelist { get; set; }

        /// <summary>
        /// 已连接的机器人 QQ 列表。
        /// </summary>
        public List<string> ConnectedBots { get; set; } = new List<string>();

        /// <summary>
        /// 最近收到消息的群号列表。
        /// </summary>
        public List<string> RecentGroups { get; set; } = new List<string>();

        /// <summary>
        /// 最近 QQ 消息摘要。
        /// </summary>
        public List<string> RecentMessages { get; set; } = new List<string>();
    }

    /// <summary>
    /// 前端展示用国家摘要。
    /// </summary>
    public sealed class AutoPanKingdomDashboardInfo
    {
        /// <summary>
        /// 国家 ID。
        /// </summary>
        public long KingdomId { get; set; }

        /// <summary>
        /// 国家名称。
        /// </summary>
        public string KingdomName { get; set; }

        /// <summary>
        /// 玩家拥有者名称。
        /// </summary>
        public string OwnerName { get; set; }

        /// <summary>
        /// 玩家拥有者 userId。
        /// </summary>
        public string OwnerUserId { get; set; }

        /// <summary>
        /// 国家国库。
        /// </summary>
        public int Treasury { get; set; }

        /// <summary>
        /// 国家等级。
        /// </summary>
        public int NationLevel { get; set; }

        /// <summary>
        /// 修真国等级。
        /// </summary>
        public int XiuzhenguoLevel { get; set; }

        /// <summary>
        /// 城市数。
        /// </summary>
        public int CityCount { get; set; }

        /// <summary>
        /// 人口。
        /// </summary>
        public int Population { get; set; }

        /// <summary>
        /// 总灵气。
        /// </summary>
        public int TotalAura { get; set; }

        /// <summary>
        /// 年收入。
        /// </summary>
        public int AnnualIncome { get; set; }

        /// <summary>
        /// 联盟名称。
        /// </summary>
        public string AllianceName { get; set; }

        /// <summary>
        /// 是否正在战争中。
        /// </summary>
        public bool AtWar { get; set; }
    }

    /// <summary>
    /// AI 请求上下文。
    /// </summary>
    internal sealed class AutoPanAiRequestContext
    {
        /// <summary>
        /// 当前世界年份。
        /// </summary>
        public int CurrentYear { get; set; }

        /// <summary>
        /// 玩家与 AI 国家允许宣战的起始年份。
        /// </summary>
        public int DeclareWarStartYear { get; set; }

        /// <summary>
        /// 当前年份是否允许宣战。
        /// </summary>
        public bool CanDeclareWar { get; set; }

        /// <summary>
        /// 结盟、约斗等待回应的秒数。
        /// </summary>
        public int RequestTimeoutSeconds { get; set; }

        /// <summary>
        /// AI 决策强度。
        /// </summary>
        public int DecisionIntensity { get; set; }

        /// <summary>
        /// 本轮最多允许输出的动作数。
        /// </summary>
        public int MaxActions { get; set; }

        /// <summary>
        /// 国家 ID。
        /// </summary>
        public long KingdomId { get; set; }

        /// <summary>
        /// 国家名称。
        /// </summary>
        public string KingdomName { get; set; }

        /// <summary>
        /// 国库金额。
        /// </summary>
        public int Treasury { get; set; }

        /// <summary>
        /// 国家等级。
        /// </summary>
        public int NationLevel { get; set; }

        /// <summary>
        /// 修真国等级。
        /// </summary>
        public int XiuzhenguoLevel { get; set; }

        /// <summary>
        /// 年收入预测。
        /// </summary>
        public int AnnualIncome { get; set; }

        /// <summary>
        /// 城市数量。
        /// </summary>
        public int CityCount { get; set; }

        /// <summary>
        /// 人口。
        /// </summary>
        public int Population { get; set; }

        /// <summary>
        /// 总灵气。
        /// </summary>
        public int TotalAura { get; set; }

        /// <summary>
        /// 军队数量。
        /// </summary>
        public int ArmyCount { get; set; }

        /// <summary>
        /// 国家当前占领政策。
        /// </summary>
        public string OccupationPolicy { get; set; }

        /// <summary>
        /// 所属联盟名称。
        /// </summary>
        public string AllianceName { get; set; }

        /// <summary>
        /// 是否处于战争中。
        /// </summary>
        public bool AtWar { get; set; }

        /// <summary>
        /// 聚灵是否生效。
        /// </summary>
        public bool GatherSpiritActive { get; set; }

        /// <summary>
        /// 聚灵剩余年份。
        /// </summary>
        public int GatherSpiritRemainYears { get; set; }

        /// <summary>
        /// 全民皆兵剩余年份。
        /// </summary>
        public int MilitiaRemainYears { get; set; }

        /// <summary>
        /// 当前敌对国家。
        /// </summary>
        public List<string> EnemyKingdomNames { get; set; } = new List<string>();

        /// <summary>
        /// 可宣战国家。
        /// </summary>
        public List<string> CandidateKingdomNames { get; set; } = new List<string>();

        /// <summary>
        /// 修士候选描述。
        /// </summary>
        public List<string> CultivatorChoices { get; set; } = new List<string>();

        /// <summary>
        /// 古神候选描述。
        /// </summary>
        public List<string> AncientChoices { get; set; } = new List<string>();

        /// <summary>
        /// 妖兽候选描述。
        /// </summary>
        public List<string> BeastChoices { get; set; } = new List<string>();

        /// <summary>
        /// 当前世界全部文明国家摘要。
        /// </summary>
        public List<AutoPanAiKingdomSummary> AllKingdoms { get; set; } = new List<AutoPanAiKingdomSummary>();
    }

    /// <summary>
    /// AI 可读取的单个国家摘要。
    /// </summary>
    internal sealed class AutoPanAiKingdomSummary
    {
        /// <summary>
        /// 带 kingdomId 的国家标签。
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// 是否为当前 AI 自己。
        /// </summary>
        public bool IsSelf { get; set; }

        /// <summary>
        /// 是否为玩家绑定国家。
        /// </summary>
        public bool IsPlayerOwned { get; set; }

        /// <summary>
        /// 玩家拥有者显示名。
        /// </summary>
        public string OwnerName { get; set; }

        /// <summary>
        /// 与当前 AI 国家的关系。
        /// </summary>
        public string RelationToSelf { get; set; }

        /// <summary>
        /// 国库金币。
        /// </summary>
        public int Treasury { get; set; }

        /// <summary>
        /// 国家等级。
        /// </summary>
        public int NationLevel { get; set; }

        /// <summary>
        /// 修真国等级。
        /// </summary>
        public int XiuzhenguoLevel { get; set; }

        /// <summary>
        /// 城市数量。
        /// </summary>
        public int CityCount { get; set; }

        /// <summary>
        /// 人口数量。
        /// </summary>
        public int Population { get; set; }

        /// <summary>
        /// 军队数量。
        /// </summary>
        public int ArmyCount { get; set; }

        /// <summary>
        /// 有效灵气。
        /// </summary>
        public int TotalAura { get; set; }

        /// <summary>
        /// 预计年收入。
        /// </summary>
        public int AnnualIncome { get; set; }

        /// <summary>
        /// 国家政策。
        /// </summary>
        public string OccupationPolicy { get; set; }

        /// <summary>
        /// 联盟名称。
        /// </summary>
        public string AllianceName { get; set; }

        /// <summary>
        /// 是否处于任意战争。
        /// </summary>
        public bool AtWar { get; set; }
    }

    /// <summary>
    /// AI 决策结果。
    /// </summary>
    internal sealed class AutoPanAiDecisionResult
    {
        /// <summary>
        /// 国家 ID。
        /// </summary>
        public long KingdomId { get; set; }

        /// <summary>
        /// 发起该次决策时的世界年份。
        /// </summary>
        public int DecisionYear { get; set; }

        /// <summary>
        /// 决策使用的动作文本。
        /// </summary>
        public List<string> Commands { get; set; } = new List<string>();

        /// <summary>
        /// AI 对本轮国情的简短分析。
        /// </summary>
        public string AnalysisText { get; set; }

        /// <summary>
        /// 允许发送到 QQ 群的 AI 回包文案。
        /// </summary>
        public string ChatText { get; set; }

        /// <summary>
        /// 是否使用了兜底规则。
        /// </summary>
        public bool UsedFallback { get; set; }

        /// <summary>
        /// 错误信息。
        /// </summary>
        public string Error { get; set; }
    }
}
