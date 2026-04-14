using System.Text.RegularExpressions;
using XianniAutoPan.Model;

namespace XianniAutoPan.Commands
{
    /// <summary>
    /// 自动盘文本指令解析器。
    /// </summary>
    internal static class AutoPanCommandParser
    {
        private static readonly Regex HelpRegex = new Regex(@"^(帮助|指令|帮助指令)$", RegexOptions.Compiled);
        private static readonly Regex RenameKingdomRegex = new Regex(@"^国家改名\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex DeclareWarRegex = new Regex(@"^宣战\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex SeekPeaceRegex = new Regex(@"^求和\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex AllianceRegex = new Regex(@"^(结盟|联盟)\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex AcceptAllianceRegex = new Regex(@"^同意结盟(?:\s+(.+))?$", RegexOptions.Compiled);
        private static readonly Regex RejectAllianceRegex = new Regex(@"^拒绝结盟(?:\s+(.+))?$", RegexOptions.Compiled);
        private static readonly Regex LeaveAllianceRegex = new Regex(@"^(退盟|解盟|退出联盟)$", RegexOptions.Compiled);
        private static readonly Regex ChallengeDuelRegex = new Regex(@"^约斗\s+(.+?)(?:\s+([1-9]\d*))?$", RegexOptions.Compiled);
        private static readonly Regex AcceptDuelRegex = new Regex(@"^同意约斗(?:\s+(.+))?$", RegexOptions.Compiled);
        private static readonly Regex RejectDuelRegex = new Regex(@"^拒绝约斗(?:\s+(.+))?$", RegexOptions.Compiled);
        private static readonly Regex BloodlineCreateRegex = new Regex(@"^血脉创立(?:\s+(\d+))?$", RegexOptions.Compiled);
        private static readonly Regex LowerNationRegex = new Regex(@"^降低国运\s+(.+?)(?:\s+(\d+))?$", RegexOptions.Compiled);
        private static readonly Regex AuraSabotageRegex = new Regex(@"^削灵\s+(.+?)\s+(\d+)$", RegexOptions.Compiled);
        private static readonly Regex AssassinateRegex = new Regex(@"^斩首\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex CurseEnemyRegex = new Regex(@"^诅咒\s+(.+?)\s+(\d+)$", RegexOptions.Compiled);
        private static readonly Regex KingdomBlessingRegex = new Regex(@"^国家祝福\s+(全员|\d+)$", RegexOptions.Compiled);
        private static readonly Regex CultivatorSuppressRegex = new Regex(@"^修士降境\s+(.+?)\s+(\d+)\s+(\d+)$", RegexOptions.Compiled);
        private static readonly Regex AddPopulationRegex = new Regex(@"^(增加人数|增加人口)\s+([1-9]\d*)$", RegexOptions.Compiled);
        private static readonly Regex PlaceRuinsRegex = new Regex(@"^放置遗迹(?:\s+([1-9]\d*))?$", RegexOptions.Compiled);
        private static readonly Regex TransferTreasuryRegex = new Regex(@"^转账\s+(.+?)\s+([1-9]\d*)$", RegexOptions.Compiled);
        private static readonly Regex CityInfoRegex = new Regex(@"^城市信息\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex FastAdultRegex = new Regex(@"^快速成年\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex ConscriptArmyRegex = new Regex(@"^征集军队\s+(.+?)\s+(全部|\d+)$", RegexOptions.Compiled);
        private static readonly Regex TransferCityRegex = new Regex(@"^移交城市\s+(.+?)\s+给\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex EquipArmyRegex = new Regex(@"^军备\s+(.+?)\s+(铜|青铜|白银|铁|钢|秘银|精金)\s+(全军|\d+)$", RegexOptions.Compiled);
        private static readonly Regex CultivatorActionRegex = new Regex(@"^修士\s+(\d+)\s+闭关$", RegexOptions.Compiled);
        private static readonly Regex CultivatorRealmUpRegex = new Regex(@"^修士\s+(\d+)\s+(升境|提升境界)$", RegexOptions.Compiled);
        private static readonly Regex AncientActionRegex = new Regex(@"^古神\s+(\d+)\s+炼体$", RegexOptions.Compiled);
        private static readonly Regex AncientStarUpRegex = new Regex(@"^古神\s+(\d+)\s+(升星|提升一星|提升星级)$", RegexOptions.Compiled);
        private static readonly Regex BeastActionRegex = new Regex(@"^妖兽\s+(\d+)\s+养成$", RegexOptions.Compiled);
        private static readonly Regex BeastStageUpRegex = new Regex(@"^妖兽\s+(\d+)\s+(升阶|提升一阶|提升阶级)$", RegexOptions.Compiled);
        private static readonly Regex AdminAddGoldRegex = new Regex(@"^#增加国家金币\s+(.+?)\s+(-?\d+)$", RegexOptions.Compiled);
        private static readonly Regex AdminSetGoldRegex = new Regex(@"^#设置国家金币\s+(.+?)\s+(-?\d+)$", RegexOptions.Compiled);
        private static readonly Regex AdminViewGoldRegex = new Regex(@"^#查看国家金币\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex AdminViewBindingRegex = new Regex(@"^#查看绑定\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex AdminSetAiDecisionStartYearRegex = new Regex(@"^#设置AI开始决策年份\s+(-?\d+)$", RegexOptions.Compiled);
        private static readonly Regex AdminSetPlayerDecisionStartYearRegex = new Regex(@"^#设置玩家开始决策年份\s+(-?\d+)$", RegexOptions.Compiled);
        private static readonly Regex AdminSetPolicyRegex = new Regex(@"^#设置政策\s+(.+?)\s+(-?\d+)$", RegexOptions.Compiled);
        private static readonly Regex AdminSetSpeedRegex = new Regex(@"^#(\d+(?:\.\d+)?)x$", RegexOptions.Compiled);
        private static readonly Regex AdminSpawnKingdomRegex = new Regex(@"^#生成\s+(人类|兽人|精灵|矮人)$", RegexOptions.Compiled);
        private static readonly Regex HeavenPunishRegex = new Regex(@"^天运惩罚\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex HeavenBlessRegex = new Regex(@"^天运赐福\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex DisturbKingdomRegex = new Regex(@"^扰动国家\s+(.+)$", RegexOptions.Compiled);

        /// <summary>
        /// 解析前端文本为内部命令。
        /// </summary>
        public static AutoPanParsedCommand Parse(string rawText)
        {
            string text = (rawText ?? string.Empty).Trim().Replace('　', ' ');
            AutoPanParsedCommand command = new AutoPanParsedCommand
            {
                CommandType = AutoPanCommandType.Unknown,
                RawText = text
            };
            if (string.IsNullOrWhiteSpace(text))
            {
                return command;
            }

            Match match = HelpRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.Help;
                return command;
            }

            switch (text)
            {
                case "加入人类":
                    command.CommandType = AutoPanCommandType.JoinHuman;
                    return command;
                case "加入兽人":
                    command.CommandType = AutoPanCommandType.JoinOrc;
                    return command;
                case "加入精灵":
                    command.CommandType = AutoPanCommandType.JoinElf;
                    return command;
                case "加入矮人":
                    command.CommandType = AutoPanCommandType.JoinDwarf;
                    return command;
                case "我的国家":
                    command.CommandType = AutoPanCommandType.MyKingdom;
                    return command;
                case "国家信息":
                    command.CommandType = AutoPanCommandType.KingdomInfo;
                    return command;
                case "查看所有国家信息":
                case "所有国家信息":
                    command.CommandType = AutoPanCommandType.AllKingdomInfo;
                    return command;
                case "城市信息":
                    command.CommandType = AutoPanCommandType.CityInfo;
                    command.TargetName = string.Empty;
                    return command;
                case "城市列表":
                    command.CommandType = AutoPanCommandType.CityList;
                    return command;
                case "升级国运":
                    command.CommandType = AutoPanCommandType.UpgradeNation;
                    return command;
                case "国策 聚灵":
                    command.CommandType = AutoPanCommandType.GatherSpirit;
                    return command;
                case "放置遗迹":
                    command.CommandType = AutoPanCommandType.PlaceRuins;
                    command.NumericValue = 1;
                    return command;
                case "修士榜":
                    command.CommandType = AutoPanCommandType.CultivatorBoard;
                    return command;
                case "古神榜":
                    command.CommandType = AutoPanCommandType.AncientBoard;
                    return command;
                case "妖兽榜":
                    command.CommandType = AutoPanCommandType.BeastBoard;
                    return command;
                case "天榜":
                    command.CommandType = AutoPanCommandType.PowerBoard;
                    return command;
                case "同意结盟":
                    command.CommandType = AutoPanCommandType.AcceptAlliance;
                    return command;
                case "拒绝结盟":
                    command.CommandType = AutoPanCommandType.RejectAlliance;
                    return command;
                case "同意约斗":
                    command.CommandType = AutoPanCommandType.AcceptDuel;
                    return command;
                case "拒绝约斗":
                    command.CommandType = AutoPanCommandType.RejectDuel;
                    return command;
                case "退盟":
                case "解盟":
                case "退出联盟":
                    command.CommandType = AutoPanCommandType.LeaveAlliance;
                    return command;
                case "#全局AI 开":
                    command.CommandType = AutoPanCommandType.AdminAiOn;
                    return command;
                case "#全局AI 关":
                    command.CommandType = AutoPanCommandType.AdminAiOff;
                    return command;
                case "#查看政策":
                    command.CommandType = AutoPanCommandType.AdminViewPolicy;
                    return command;
            }

            match = RenameKingdomRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.RenameKingdom;
                command.TextArg = match.Groups[1].Value.Trim();
                return command;
            }

            match = BloodlineCreateRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.BloodlineCreate;
                command.ObjectIdArg = string.IsNullOrWhiteSpace(match.Groups[1].Value) ? 0L : long.Parse(match.Groups[1].Value);
                return command;
            }

            match = AddPopulationRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int addPopulationCount))
            {
                command.CommandType = AutoPanCommandType.AddPopulation;
                command.NumericValue = addPopulationCount;
                return command;
            }

            match = PlaceRuinsRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.PlaceRuins;
                command.NumericValue = string.IsNullOrWhiteSpace(match.Groups[1].Value) ? 1 : int.Parse(match.Groups[1].Value);
                return command;
            }

            match = TransferTreasuryRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int transferAmount))
            {
                command.CommandType = AutoPanCommandType.TransferTreasury;
                command.TargetName = match.Groups[1].Value.Trim();
                command.NumericValue = transferAmount;
                return command;
            }

            match = LowerNationRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.LowerNation;
                command.TargetName = match.Groups[1].Value.Trim();
                command.NumericValue = string.IsNullOrWhiteSpace(match.Groups[2].Value) ? 1 : int.Parse(match.Groups[2].Value);
                return command;
            }

            match = AuraSabotageRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int auraAmount))
            {
                command.CommandType = AutoPanCommandType.AuraSabotage;
                command.TargetName = match.Groups[1].Value.Trim();
                command.NumericValue = auraAmount;
                return command;
            }

            match = AssassinateRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.AssassinateStrongest;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = CurseEnemyRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int curseCount))
            {
                command.CommandType = AutoPanCommandType.CurseEnemy;
                command.TargetName = match.Groups[1].Value.Trim();
                command.NumericValue = curseCount;
                return command;
            }

            match = KingdomBlessingRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.KingdomBlessing;
                command.TextArg = match.Groups[1].Value.Trim();
                command.NumericValue = command.TextArg == "全员" ? -1 : int.Parse(command.TextArg);
                return command;
            }

            match = CultivatorSuppressRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int suppressCount) && int.TryParse(match.Groups[3].Value, out int suppressLevels))
            {
                command.CommandType = AutoPanCommandType.CultivatorSuppress;
                command.TargetName = match.Groups[1].Value.Trim();
                command.NumericValue = suppressCount;
                command.SecondaryNumericValue = suppressLevels;
                return command;
            }

            match = CityInfoRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.CityInfo;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = FastAdultRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.FastAdult;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = ConscriptArmyRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.ConscriptArmy;
                command.TargetName = match.Groups[1].Value.Trim();
                command.TextArg = match.Groups[2].Value.Trim();
                command.NumericValue = command.TextArg == "全部" ? -1 : int.Parse(command.TextArg);
                return command;
            }

            match = TransferCityRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.TransferCity;
                command.TargetName = match.Groups[1].Value.Trim();
                command.SecondaryTargetName = match.Groups[2].Value.Trim();
                return command;
            }

            match = EquipArmyRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.EquipArmy;
                command.TargetName = match.Groups[1].Value.Trim();
                command.SecondaryTargetName = match.Groups[2].Value.Trim();
                command.TextArg = match.Groups[3].Value.Trim();
                command.NumericValue = command.TextArg == "全军" ? -1 : int.Parse(command.TextArg);
                return command;
            }

            match = DeclareWarRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.DeclareWar;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = SeekPeaceRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.SeekPeace;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = AllianceRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.Alliance;
                command.TargetName = match.Groups[2].Value.Trim();
                return command;
            }

            match = AcceptAllianceRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.AcceptAlliance;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = RejectAllianceRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.RejectAlliance;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = LeaveAllianceRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.LeaveAlliance;
                return command;
            }

            match = ChallengeDuelRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.ChallengeDuel;
                command.TargetName = match.Groups[1].Value.Trim();
                command.BetAmount = string.IsNullOrWhiteSpace(match.Groups[2].Value) ? 0 : int.Parse(match.Groups[2].Value);
                return command;
            }

            match = AcceptDuelRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.AcceptDuel;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = RejectDuelRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.RejectDuel;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = CultivatorActionRegex.Match(text);
            if (match.Success && long.TryParse(match.Groups[1].Value, out long cultivatorId))
            {
                command.CommandType = AutoPanCommandType.CultivatorRetreat;
                command.ObjectIdArg = cultivatorId;
                return command;
            }

            match = CultivatorRealmUpRegex.Match(text);
            if (match.Success && long.TryParse(match.Groups[1].Value, out cultivatorId))
            {
                command.CommandType = AutoPanCommandType.CultivatorRealmUp;
                command.ObjectIdArg = cultivatorId;
                return command;
            }

            match = AncientActionRegex.Match(text);
            if (match.Success && long.TryParse(match.Groups[1].Value, out long ancientId))
            {
                command.CommandType = AutoPanCommandType.AncientTrain;
                command.ObjectIdArg = ancientId;
                return command;
            }

            match = AncientStarUpRegex.Match(text);
            if (match.Success && long.TryParse(match.Groups[1].Value, out ancientId))
            {
                command.CommandType = AutoPanCommandType.AncientStarUp;
                command.ObjectIdArg = ancientId;
                return command;
            }

            match = BeastActionRegex.Match(text);
            if (match.Success && long.TryParse(match.Groups[1].Value, out long beastId))
            {
                command.CommandType = AutoPanCommandType.BeastTrain;
                command.ObjectIdArg = beastId;
                return command;
            }

            match = BeastStageUpRegex.Match(text);
            if (match.Success && long.TryParse(match.Groups[1].Value, out beastId))
            {
                command.CommandType = AutoPanCommandType.BeastStageUp;
                command.ObjectIdArg = beastId;
                return command;
            }

            match = AdminAddGoldRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int addGold))
            {
                command.CommandType = AutoPanCommandType.AdminAddGold;
                command.TargetName = match.Groups[1].Value.Trim();
                command.NumericValue = addGold;
                return command;
            }

            match = AdminSetGoldRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int setGold))
            {
                command.CommandType = AutoPanCommandType.AdminSetGold;
                command.TargetName = match.Groups[1].Value.Trim();
                command.NumericValue = setGold;
                return command;
            }

            match = AdminViewGoldRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.AdminViewGold;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = AdminViewBindingRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.AdminViewBinding;
                command.UserIdArg = match.Groups[1].Value.Trim();
                return command;
            }

            match = AdminSetAiDecisionStartYearRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int aiStartYear))
            {
                command.CommandType = AutoPanCommandType.AdminSetPolicy;
                command.TargetName = "aiDecisionStartYear";
                command.NumericValue = aiStartYear;
                return command;
            }

            match = AdminSetPlayerDecisionStartYearRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int playerStartYear))
            {
                command.CommandType = AutoPanCommandType.AdminSetPolicy;
                command.TargetName = "playerDecisionStartYear";
                command.NumericValue = playerStartYear;
                return command;
            }

            match = AdminSetPolicyRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int policyValue))
            {
                command.CommandType = AutoPanCommandType.AdminSetPolicy;
                command.TargetName = match.Groups[1].Value.Trim();
                command.NumericValue = policyValue;
                return command;
            }

            match = AdminSetSpeedRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.AdminSetSpeed;
                command.TextArg = match.Groups[1].Value;
                return command;
            }

            match = AdminSpawnKingdomRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.AdminSpawnKingdom;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = HeavenPunishRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.HeavenPunish;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = HeavenBlessRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.HeavenBless;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            match = DisturbKingdomRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.DisturbKingdom;
                command.TargetName = match.Groups[1].Value.Trim();
                return command;
            }

            return command;
        }
    }
}
