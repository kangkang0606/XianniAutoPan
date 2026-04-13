using System.Text.RegularExpressions;
using XianniAutoPan.Model;

namespace XianniAutoPan.Commands
{
    /// <summary>
    /// 自动盘文本指令解析器。
    /// </summary>
    internal static class AutoPanCommandParser
    {
        private static readonly Regex DeclareWarRegex = new Regex(@"^宣战\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex SeekPeaceRegex = new Regex(@"^求和\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex AllianceRegex = new Regex(@"^(结盟|联盟)\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex BreakAllianceRegex = new Regex(@"^(解盟|解除结盟|取消结盟)\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex AuraSabotageRegex = new Regex(@"^削灵\s+(.+?)\s+(\d+)$", RegexOptions.Compiled);
        private static readonly Regex AssassinateRegex = new Regex(@"^斩首\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex CurseEnemyRegex = new Regex(@"^诅咒\s+(.+?)\s+(\d+)$", RegexOptions.Compiled);
        private static readonly Regex KingdomBlessingRegex = new Regex(@"^国家祝福\s+(全员|\d+)$", RegexOptions.Compiled);
        private static readonly Regex CultivatorSuppressRegex = new Regex(@"^修士降境\s+(.+?)\s+(\d+)\s+(\d+)$", RegexOptions.Compiled);
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
                case "血脉创立":
                    command.CommandType = AutoPanCommandType.BloodlineCreate;
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
                case "修士榜":
                    command.CommandType = AutoPanCommandType.CultivatorBoard;
                    return command;
                case "古神榜":
                    command.CommandType = AutoPanCommandType.AncientBoard;
                    return command;
                case "妖兽榜":
                    command.CommandType = AutoPanCommandType.BeastBoard;
                    return command;
                case "#全局AI 开":
                    command.CommandType = AutoPanCommandType.AdminAiOn;
                    return command;
                case "#全局AI 关":
                    command.CommandType = AutoPanCommandType.AdminAiOff;
                    return command;
            }

            Match match = AuraSabotageRegex.Match(text);
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

            match = BreakAllianceRegex.Match(text);
            if (match.Success)
            {
                command.CommandType = AutoPanCommandType.BreakAlliance;
                command.TargetName = match.Groups[2].Value.Trim();
                return command;
            }

            match = CultivatorActionRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int cultivatorIndex))
            {
                command.CommandType = AutoPanCommandType.CultivatorRetreat;
                command.SlotIndex = cultivatorIndex;
                return command;
            }

            match = CultivatorRealmUpRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out cultivatorIndex))
            {
                command.CommandType = AutoPanCommandType.CultivatorRealmUp;
                command.SlotIndex = cultivatorIndex;
                return command;
            }

            match = AncientActionRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int ancientIndex))
            {
                command.CommandType = AutoPanCommandType.AncientTrain;
                command.SlotIndex = ancientIndex;
                return command;
            }

            match = AncientStarUpRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out ancientIndex))
            {
                command.CommandType = AutoPanCommandType.AncientStarUp;
                command.SlotIndex = ancientIndex;
                return command;
            }

            match = BeastActionRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int beastIndex))
            {
                command.CommandType = AutoPanCommandType.BeastTrain;
                command.SlotIndex = beastIndex;
                return command;
            }

            match = BeastStageUpRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out beastIndex))
            {
                command.CommandType = AutoPanCommandType.BeastStageUp;
                command.SlotIndex = beastIndex;
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

            return command;
        }
    }
}
