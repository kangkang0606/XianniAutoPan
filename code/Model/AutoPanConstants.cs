namespace XianniAutoPan.Model
{
    /// <summary>
    /// 自动盘常量定义。
    /// </summary>
    internal static class AutoPanConstants
    {
        /// <summary>
        /// 统一日志前缀。
        /// </summary>
        public const string LogPrefix = "[XIANNI][AUTOPAN]";

        /// <summary>
        /// 世界状态存档键。
        /// </summary>
        public const string WorldStateKey = "xianni.autopan.world_state";

        /// <summary>
        /// 国家拥有者用户 ID 键。
        /// </summary>
        public const string KeyOwnerUserId = "xianni.autopan.owner_user_id";

        /// <summary>
        /// 国家拥有者显示名键。
        /// </summary>
        public const string KeyOwnerName = "xianni.autopan.owner_name";

        /// <summary>
        /// 国家国库键。
        /// </summary>
        public const string KeyTreasury = "xianni.autopan.treasury";

        /// <summary>
        /// 国家等级键。
        /// </summary>
        public const string KeyLevel = "xianni.autopan.level";

        /// <summary>
        /// 聚灵截止年份键。
        /// </summary>
        public const string KeyGatherSpiritUntilYear = "xianni.autopan.gather_spirit_until_year";

        /// <summary>
        /// 玩家加入时的初始国库。
        /// </summary>
        public const int InitialTreasury = 200;

        /// <summary>
        /// 玩家加入时的初始等级。
        /// </summary>
        public const int InitialLevel = 1;

        /// <summary>
        /// 每次加入生成的固定单位数量。
        /// </summary>
        public const int JoinUnitCount = 5;

        /// <summary>
        /// 自动盘固定成年年龄。
        /// </summary>
        public const int FixedAdultAge = 18;

        /// <summary>
        /// 聚灵持续年数。
        /// </summary>
        public const int GatherSpiritDurationYears = 5;

        /// <summary>
        /// 聚灵对每座城市提供的等效灵气。
        /// </summary>
        public const int GatherSpiritAuraBonusPerCity = 500;

        /// <summary>
        /// 修士闭关获得的修为。
        /// </summary>
        public const long ClosedDoorXiuweiGain = 10000L;

        /// <summary>
        /// 古神炼体获得的古神之力。
        /// </summary>
        public const int AncientTrainingGain = 15000;

        /// <summary>
        /// 妖兽养成获得的妖力。
        /// </summary>
        public const int BeastTrainingGain = 15000;

        /// <summary>
        /// 每帧处理的前端消息上限。
        /// </summary>
        public const int MaxMessagesPerFrame = 20;

        /// <summary>
        /// 聚灵国策价格。
        /// </summary>
        public const int GatherSpiritCost = 120;

        /// <summary>
        /// 宣战价格。
        /// </summary>
        public const int DeclareWarCost = 150;

        /// <summary>
        /// 求和价格。
        /// </summary>
        public const int SeekPeaceCost = 100;

        /// <summary>
        /// 结盟价格。
        /// </summary>
        public const int AllianceCost = 130;

        /// <summary>
        /// 解盟价格。
        /// </summary>
        public const int BreakAllianceCost = 90;

        /// <summary>
        /// 修士闭关价格。
        /// </summary>
        public const int CultivatorRetreatCost = 80;

        /// <summary>
        /// 古神炼体价格。
        /// </summary>
        public const int AncientTrainCost = 100;

        /// <summary>
        /// 妖兽养成价格。
        /// </summary>
        public const int BeastTrainCost = 90;

        /// <summary>
        /// 修士直接升境价格。
        /// </summary>
        public const int CultivatorRealmUpCost = 220;

        /// <summary>
        /// 古神直接升星价格。
        /// </summary>
        public const int AncientStarUpCost = 180;

        /// <summary>
        /// 妖兽直接升阶价格。
        /// </summary>
        public const int BeastStageUpCost = 160;

        /// <summary>
        /// 快速成年每名单位的价格。
        /// </summary>
        public const int FastAdultCostPerUnit = 20;

        /// <summary>
        /// 征集军队每名单位的价格。
        /// </summary>
        public const int ConscriptCostPerUnit = 35;

        /// <summary>
        /// 移交城市固定价格。
        /// </summary>
        public const int TransferCityCost = 180;

        /// <summary>
        /// 铜制军备每名士兵价格。
        /// </summary>
        public const int EquipCopperCostPerUnit = 30;

        /// <summary>
        /// 青铜军备每名士兵价格。
        /// </summary>
        public const int EquipBronzeCostPerUnit = 45;

        /// <summary>
        /// 白银军备每名士兵价格。
        /// </summary>
        public const int EquipSilverCostPerUnit = 60;

        /// <summary>
        /// 铁制军备每名士兵价格。
        /// </summary>
        public const int EquipIronCostPerUnit = 80;

        /// <summary>
        /// 钢制军备每名士兵价格。
        /// </summary>
        public const int EquipSteelCostPerUnit = 110;

        /// <summary>
        /// 秘银军备每名士兵价格。
        /// </summary>
        public const int EquipMythrilCostPerUnit = 160;

        /// <summary>
        /// 精金军备每名士兵价格。
        /// </summary>
        public const int EquipAdamantineCostPerUnit = 240;

        /// <summary>
        /// 年度日志缓存数量。
        /// </summary>
        public const int LogCapacity = 80;

        /// <summary>
        /// 前端会话缓存数量。
        /// </summary>
        public const int SessionCapacity = 30;
    }
}
