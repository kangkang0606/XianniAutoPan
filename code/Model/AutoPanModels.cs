using System.Collections.Generic;

namespace XianniAutoPan.Model
{
    /// <summary>
    /// 自动盘命令类型。
    /// </summary>
    internal enum AutoPanCommandType
    {
        Unknown = 0,
        JoinHuman,
        JoinOrc,
        JoinElf,
        JoinDwarf,
        MyKingdom,
        KingdomInfo,
        UpgradeNation,
        GatherSpirit,
        AddPopulation,
        PlaceRuins,
        TransferTreasury,
        DeclareWar,
        SeekPeace,
        Alliance,
        BreakAlliance,
        BloodlineCreate,
        AuraSabotage,
        AssassinateStrongest,
        CurseEnemy,
        KingdomBlessing,
        CultivatorSuppress,
        CityList,
        CityInfo,
        FastAdult,
        ConscriptArmy,
        TransferCity,
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
        AdminViewBinding
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
        /// 玩家绑定表。
        /// </summary>
        public Dictionary<string, AutoPanBindingRecord> Bindings { get; set; } = new Dictionary<string, AutoPanBindingRecord>();

        /// <summary>
        /// 最近前端会话信息。
        /// </summary>
        public List<AutoPanSessionInfo> RecentSessions { get; set; } = new List<AutoPanSessionInfo>();
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
        /// 聚灵是否生效。
        /// </summary>
        public bool GatherSpiritActive { get; set; }

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
        /// 决策使用的动作文本。
        /// </summary>
        public List<string> Commands { get; set; } = new List<string>();

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
