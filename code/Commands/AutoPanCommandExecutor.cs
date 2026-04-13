using System;
using System.Collections.Generic;
using NeoModLoader.api;
using XianniAutoPan.Model;
using XianniAutoPan.Services;
using xn.api;

namespace XianniAutoPan.Commands
{
    /// <summary>
    /// 自动盘命令执行器。
    /// </summary>
    internal static class AutoPanCommandExecutor
    {
        /// <summary>
        /// 执行玩家前端命令。
        /// </summary>
        public static AutoPanCommandResult ExecutePlayerMessage(FrontendInboundMessage message)
        {
            string userId = string.IsNullOrWhiteSpace(message?.UserId) ? message?.SessionId ?? "local-user" : message.UserId.Trim();
            string playerName = string.IsNullOrWhiteSpace(message?.PlayerName) ? userId : message.PlayerName.Trim();
            long sequence = AutoPanStateRepository.NextMessageSequence();
            AutoPanCommandResult result = new AutoPanCommandResult
            {
                Success = false,
                Sequence = sequence,
                UserId = userId,
                Text = "未处理。"
            };

            if (World.world == null)
            {
                result.Text = "当前世界未加载，无法执行自动盘指令。";
                return result;
            }

            AutoPanStateRepository.RecordSession(message.SessionId, userId, playerName, message.RemoteEndPoint);
            AutoPanStateRepository.EnsureBindingValidForUser(userId);
            AutoPanParsedCommand command = AutoPanCommandParser.Parse(message.Text);
            if (command.CommandType == AutoPanCommandType.Unknown)
            {
                if (!command.RawText.StartsWith("#", StringComparison.Ordinal) && TryGetPlayerKingdom(userId, out Kingdom chatKingdom, out _))
                {
                    AutoPanKingdomSpeechService.ShowSpeech(chatKingdom, playerName, command.RawText, isCommand: false);
                    AutoPanLogService.Info($"{playerName} 发送国家聊天：{chatKingdom.name} -> {command.RawText}");
                    result.Success = true;
                    result.Text = $"已在 {chatKingdom.name} 上方显示聊天内容。";
                    return result;
                }

                result.Text = "无法识别这条指令，请查看指令书。";
                return result;
            }

            switch (command.CommandType)
            {
                case AutoPanCommandType.JoinHuman:
                case AutoPanCommandType.JoinOrc:
                case AutoPanCommandType.JoinElf:
                case AutoPanCommandType.JoinDwarf:
                    result.Success = AutoPanKingdomService.TryCreatePlayerKingdom(userId, playerName, command.CommandType, out Kingdom joinedKingdom, out string joinText);
                    result.Text = joinText;
                    if (joinedKingdom != null && result.Success)
                    {
                        AutoPanKingdomSpeechService.ShowSpeech(joinedKingdom, playerName, command.RawText, isCommand: true);
                        AutoPanLogService.Info($"{playerName} 执行 {command.RawText} -> {joinText}");
                    }
                    return result;

                case AutoPanCommandType.AdminAddGold:
                    return ExecuteAdminAddGold(command, playerName, result);
                case AutoPanCommandType.AdminSetGold:
                    return ExecuteAdminSetGold(command, playerName, result);
                case AutoPanCommandType.AdminViewGold:
                    return ExecuteAdminViewGold(command, result);
                case AutoPanCommandType.AdminAiOn:
                    return ExecuteAdminToggleAi(true, playerName, result);
                case AutoPanCommandType.AdminAiOff:
                    return ExecuteAdminToggleAi(false, playerName, result);
                case AutoPanCommandType.AdminViewBinding:
                    return ExecuteAdminViewBinding(command, result);
            }

            if (!TryGetPlayerKingdom(userId, out Kingdom playerKingdom, out string kingdomError))
            {
                result.Text = kingdomError;
                return result;
            }

            result = ExecuteOwnedCommand(command, playerKingdom, playerName, isAi: false, result);
            if (!command.RawText.StartsWith("#", StringComparison.Ordinal))
            {
                AutoPanKingdomSpeechService.ShowSpeech(playerKingdom, playerName, command.RawText, isCommand: true);
            }
            return result;
        }

        /// <summary>
        /// 执行 AI 为国家选择的动作。
        /// </summary>
        public static bool TryExecuteAiCommand(Kingdom kingdom, string commandText, out string message)
        {
            message = string.Empty;
            if (kingdom == null || !kingdom.isAlive())
            {
                message = "AI 国家已失效。";
                return false;
            }

            AutoPanParsedCommand command = AutoPanCommandParser.Parse(commandText);
            AutoPanCommandResult result = new AutoPanCommandResult
            {
                UserId = $"ai:{kingdom.getID()}",
                Sequence = 0
            };
            result = ExecuteOwnedCommand(command, kingdom, "国家AI", isAi: true, result);
            message = result.Text;
            return result.Success;
        }

        private static AutoPanCommandResult ExecuteOwnedCommand(AutoPanParsedCommand command, Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            switch (command.CommandType)
            {
                case AutoPanCommandType.MyKingdom:
                case AutoPanCommandType.KingdomInfo:
                    result.Success = true;
                    result.Text = AutoPanKingdomService.BuildKingdomInfoText(kingdom);
                    return result;
                case AutoPanCommandType.CityList:
                    result.Success = true;
                    result.Text = AutoPanCityService.BuildCityListText(kingdom);
                    return result;
                case AutoPanCommandType.CityInfo:
                    return ExecuteCityInfo(kingdom, command, result);
                case AutoPanCommandType.UpgradeNation:
                    return ExecuteUpgradeNation(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.GatherSpirit:
                    return ExecuteGatherSpirit(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.AddPopulation:
                    return ExecuteAddPopulation(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.PlaceRuins:
                    return ExecutePlaceRuins(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.TransferTreasury:
                    return ExecuteTransferTreasury(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.DeclareWar:
                    return ExecuteDeclareWar(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.SeekPeace:
                    return ExecuteSeekPeace(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.Alliance:
                    return ExecuteAlliance(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.BreakAlliance:
                    return ExecuteBreakAlliance(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.BloodlineCreate:
                    return ExecuteBloodlineCreate(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.AuraSabotage:
                    return ExecuteAuraSabotage(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.AssassinateStrongest:
                    return ExecuteAssassinateStrongest(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.CurseEnemy:
                    return ExecuteCurseEnemy(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.KingdomBlessing:
                    return ExecuteKingdomBlessing(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.CultivatorSuppress:
                    return ExecuteCultivatorSuppress(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.CultivatorBoard:
                case AutoPanCommandType.AncientBoard:
                case AutoPanCommandType.BeastBoard:
                    result.Success = true;
                    result.Text = AutoPanKingdomService.BuildBoardText(kingdom, command.CommandType);
                    return result;
                case AutoPanCommandType.FastAdult:
                    return ExecuteFastAdult(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.ConscriptArmy:
                    return ExecuteConscriptArmy(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.TransferCity:
                    return ExecuteTransferCity(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.EquipArmy:
                    return ExecuteEquipArmy(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.CultivatorRetreat:
                    return ExecuteCultivatorRetreat(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.CultivatorRealmUp:
                    return ExecuteCultivatorRealmUp(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.AncientTrain:
                    return ExecuteAncientTraining(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.AncientStarUp:
                    return ExecuteAncientStarUp(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.BeastTrain:
                    return ExecuteBeastTraining(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.BeastStageUp:
                    return ExecuteBeastStageUp(kingdom, command, operatorName, isAi, result);
                default:
                    result.Success = false;
                    result.Text = isAi ? "AI 指令不在允许范围内。" : "当前国家无法执行该指令。";
                    return result;
            }
        }

        private static AutoPanCommandResult ExecuteCityInfo(Kingdom kingdom, AutoPanParsedCommand command, AutoPanCommandResult result)
        {
            if (string.IsNullOrWhiteSpace(command.TargetName))
            {
                result.Success = true;
                result.Text = AutoPanCityService.BuildAllCityInfoText(kingdom);
                return result;
            }

            if (!AutoPanCityService.TryResolveOwnedCity(kingdom, command.TargetName, out City city, out string error))
            {
                result.Text = error;
                return result;
            }

            result.Success = true;
            result.Text = AutoPanCityService.BuildCityInfoText(city);
            return result;
        }

        private static AutoPanCommandResult ExecuteAddPopulation(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanKingdomService.TryAddPopulation(kingdom, command.NumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 增加了 {Math.Max(1, command.NumericValue)} 名同种族成年人口");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 增加人数：{kingdom.name} / {command.NumericValue}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecutePlaceRuins(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            int requestedCount = command.NumericValue <= 0 ? 1 : command.NumericValue;
            int totalCost = requestedCount * AutoPanConstants.PlaceRuinCost;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, totalCost, out string spendError))
            {
                result.Text = spendError;
                return result;
            }

            if (!XianniAutoPanApi.TryPlaceRuinsInKingdom(kingdom, requestedCount, out int placedCount) || placedCount <= 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, totalCost);
                result.Text = "当前王国内没有可放置遗迹的合法区域。";
                return result;
            }

            int refund = totalCost - placedCount * AutoPanConstants.PlaceRuinCost;
            if (refund > 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, refund);
            }

            result.Success = true;
            result.Text = $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 已放置 {placedCount} 座遗迹，消耗 {placedCount * AutoPanConstants.PlaceRuinCost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 在本国区域放置了 {placedCount} 座遗迹");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 放置遗迹：{kingdom.name} / 请求{requestedCount} / 成功{placedCount}");
            return result;
        }

        private static AutoPanCommandResult ExecuteTransferTreasury(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }

            result.Success = AutoPanKingdomService.TryTransferTreasury(kingdom, target, command.NumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 向 {target.name} 转账 {command.NumericValue} 金币");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 转账：{kingdom.name} -> {target.name} / {command.NumericValue}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteFastAdult(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanCityService.TryFastAdult(kingdom, command.TargetName, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 执行快速成年：{command.TargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 快速成年：{kingdom.name} / {command.TargetName}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteConscriptArmy(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanCityService.TryConscriptArmy(kingdom, command.TargetName, command.NumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 征集军队：{kingdom.name} / {command.TargetName} / {command.TextArg}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteTransferCity(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanCityService.TryTransferCity(kingdom, command.TargetName, command.SecondaryTargetName, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 移交城市 {command.TargetName} 给 {command.SecondaryTargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 移交城市：{kingdom.name} / {command.TargetName} -> {command.SecondaryTargetName}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteEquipArmy(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanCityService.TryEquipArmy(kingdom, command.TargetName, command.SecondaryTargetName, command.NumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 军备发放：{kingdom.name} / {command.TargetName} / {command.SecondaryTargetName} / {command.TextArg}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteUpgradeNation(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            int level = AutoPanKingdomService.GetLevel(kingdom);
            int cost = 200 * level;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out string error))
            {
                result.Text = error;
                return result;
            }

            AutoPanKingdomService.SetLevel(kingdom, level + 1);
            result.Success = true;
            result.Text = $"{kingdom.name} 升级国运成功，消耗 {cost} 金币，国家等级提升到 {level + 1}。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 国运提升至 {level + 1} 级");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 执行升级国运：{kingdom.name} -> Lv{level + 1}");
            return result;
        }

        private static AutoPanCommandResult ExecuteGatherSpirit(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.GatherSpiritCost, out string error))
            {
                result.Text = error;
                return result;
            }

            AutoPanKingdomService.ActivateGatherSpirit(kingdom);
            int untilYear = AutoPanKingdomService.GetGatherSpiritUntilYear(kingdom);
            result.Success = true;
            result.Text = $"{kingdom.name} 已开启聚灵国策，持续到第 {untilYear} 年。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 开启聚灵国策，灵气汇聚 5 年");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 开启聚灵：{kingdom.name} -> {untilYear}");
            return result;
        }

        private static AutoPanCommandResult ExecuteDeclareWar(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }
            if (target == kingdom)
            {
                result.Text = "不能向自己的国家宣战。";
                return result;
            }
            if (Alliance.isSame(kingdom.getAlliance(), target.getAlliance()))
            {
                result.Text = $"你与 {target.name} 当前处于同一联盟，不能直接宣战。";
                return result;
            }
            if (AutoPanKingdomService.TryFindWarWith(kingdom, target, out _))
            {
                result.Text = $"你与 {target.name} 已处于战争状态。";
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.DeclareWarCost, out string error))
            {
                result.Text = error;
                return result;
            }

            War war = World.world.diplomacy.startWar(kingdom, target, WarTypeLibrary.normal);
            if (war == null)
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConstants.DeclareWarCost);
                result.Text = $"对 {target.name} 宣战失败。";
                return result;
            }

            result.Success = true;
            result.Text = $"{kingdom.name} 已向 {target.name} 宣战，消耗 {AutoPanConstants.DeclareWarCost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 向 {target.name} 宣战");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 宣战：{kingdom.name} -> {target.name}");
            return result;
        }

        private static AutoPanCommandResult ExecuteSeekPeace(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }

            if (!AutoPanKingdomService.TryFindWarWith(kingdom, target, out War war))
            {
                result.Text = $"当前与 {target.name} 没有正在进行的战争。";
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.SeekPeaceCost, out string error))
            {
                result.Text = error;
                return result;
            }

            World.world.wars.endWar(war, WarWinner.Peace);
            result.Success = true;
            result.Text = $"{kingdom.name} 已向 {target.name} 求和，消耗 {AutoPanConstants.SeekPeaceCost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 与 {target.name} 达成和平");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 求和：{kingdom.name} -> {target.name}");
            return result;
        }

        private static AutoPanCommandResult ExecuteAlliance(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }
            if (target == kingdom)
            {
                result.Text = "不能和自己的国家结盟。";
                return result;
            }
            if (Alliance.isSame(kingdom.getAlliance(), target.getAlliance()))
            {
                result.Text = $"你与 {target.name} 已经处于同一联盟。";
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.AllianceCost, out string error))
            {
                result.Text = error;
                return result;
            }

            World.world.alliances.forceAlliance(kingdom, target);
            if (!Alliance.isSame(kingdom.getAlliance(), target.getAlliance()))
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConstants.AllianceCost);
                result.Text = $"与 {target.name} 结盟失败。";
                return result;
            }

            result.Success = true;
            result.Text = $"{kingdom.name} 已与 {target.name} 结盟，消耗 {AutoPanConstants.AllianceCost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 与 {target.name} 缔结联盟");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 结盟：{kingdom.name} <-> {target.name}");
            return result;
        }

        private static AutoPanCommandResult ExecuteBreakAlliance(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }
            if (target == kingdom)
            {
                result.Text = "请指定要解除结盟的其他国家。";
                return result;
            }

            Alliance alliance = kingdom.getAlliance();
            if (alliance == null || !Alliance.isSame(alliance, target.getAlliance()))
            {
                result.Text = $"你与 {target.name} 当前不在同一联盟。";
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.BreakAllianceCost, out string error))
            {
                result.Text = error;
                return result;
            }

            if (alliance.kingdoms_hashset.Count <= 2)
            {
                World.world.alliances.dissolveAlliance(alliance);
            }
            else
            {
                alliance.leave(target);
            }

            result.Success = true;
            result.Text = $"{kingdom.name} 已与 {target.name} 解除结盟，消耗 {AutoPanConstants.BreakAllianceCost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 与 {target.name} 解除联盟");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 解盟：{kingdom.name} x {target.name}");
            return result;
        }

        private static AutoPanCommandResult ExecuteBloodlineCreate(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TryCreateFounderBloodline(kingdom, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 完成血脉创立");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 血脉创立：{kingdom.name} -> {message}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteAuraSabotage(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TryReduceEnemyAura(kingdom, command.TargetName, command.NumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 发动削灵：{command.TargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 削灵：{kingdom.name} -> {command.TargetName} / {command.NumericValue}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteAssassinateStrongest(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TryAssassinateStrongest(kingdom, command.TargetName, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 发动斩首：{command.TargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 斩首：{kingdom.name} -> {command.TargetName}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteCurseEnemy(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TryCurseEnemyUnits(kingdom, command.TargetName, command.NumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 发动诅咒：{command.TargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 诅咒：{kingdom.name} -> {command.TargetName} / {command.NumericValue}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteKingdomBlessing(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TryBlessOwnUnits(kingdom, command.NumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 降下国家祝福");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 国家祝福：{kingdom.name} / {command.TextArg}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteCultivatorSuppress(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TrySuppressEnemyCultivators(kingdom, command.TargetName, command.NumericValue, command.SecondaryNumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 发动修士压境：{command.TargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 修士压境：{kingdom.name} -> {command.TargetName} / 人数{command.NumericValue} / 等级{command.SecondaryNumericValue}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteCultivatorRetreat(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActor(kingdom, AutoPanCommandType.CultivatorBoard, command.SlotIndex, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.CultivatorRetreatCost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!XianniAutoPanApi.TryAddXiuwei(actor, AutoPanConstants.ClosedDoorXiuweiGain))
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConstants.CultivatorRetreatCost);
                result.Text = "闭关失败，未能为修士增加修为。";
                return result;
            }
            XianniAutoPanApi.TryTriggerBreakthrough(actor);
            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 闭关成功，获得 {AutoPanConstants.ClosedDoorXiuweiGain} 修为。";
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 修士闭关：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteCultivatorRealmUp(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActor(kingdom, AutoPanCommandType.CultivatorBoard, command.SlotIndex, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            int cost = AutoPanCostService.GetCultivatorRealmUpCost(actor);
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanCultivationPromotionService.TryPromoteCultivatorRealm(actor))
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                result.Text = "升境失败，该修士可能已到最高境界或不满足条件。";
                return result;
            }

            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 已直接提升一个境界，消耗 {cost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 的修士 {actor.getName()} 直接提升了一个境界");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 修士升境：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteAncientTraining(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActor(kingdom, AutoPanCommandType.AncientBoard, command.SlotIndex, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.AncientTrainCost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!XianniAutoPanApi.TryAddAncientPower(actor, AutoPanConstants.AncientTrainingGain))
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConstants.AncientTrainCost);
                result.Text = "炼体失败，未能增加古神之力。";
                return result;
            }
            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 炼体成功，获得 {AutoPanConstants.AncientTrainingGain} 古神之力。";
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 古神炼体：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteAncientStarUp(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActor(kingdom, AutoPanCommandType.AncientBoard, command.SlotIndex, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            int cost = AutoPanCostService.GetAncientStageUpCost(actor);
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanCultivationPromotionService.TryPromoteAncientStage(actor))
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                result.Text = "升星失败，该古神可能已到最高星级或不满足条件。";
                return result;
            }

            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 已直接提升一星，消耗 {cost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 的古神 {actor.getName()} 直接提升了一星");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 古神升星：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteBeastTraining(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActor(kingdom, AutoPanCommandType.BeastBoard, command.SlotIndex, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConstants.BeastTrainCost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!XianniAutoPanApi.TryAddBeastPower(actor, AutoPanConstants.BeastTrainingGain))
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConstants.BeastTrainCost);
                result.Text = "养成失败，未能增加妖力。";
                return result;
            }
            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 养成成功，获得 {AutoPanConstants.BeastTrainingGain} 妖力。";
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 妖兽养成：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteBeastStageUp(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActor(kingdom, AutoPanCommandType.BeastBoard, command.SlotIndex, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            int cost = AutoPanCostService.GetBeastStageUpCost(actor);
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanCultivationPromotionService.TryPromoteBeastStage(actor))
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                result.Text = "升阶失败，该妖兽可能已到最高阶或不满足条件。";
                return result;
            }

            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 已直接提升一阶，消耗 {cost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 的妖兽 {actor.getName()} 直接提升了一阶");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 妖兽升阶：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteAdminAddGold(AutoPanParsedCommand command, string operatorName, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }
            if (command.NumericValue <= 0)
            {
                result.Text = "增加金币的数值必须大于 0。";
                return result;
            }

            int treasury = AutoPanKingdomService.AddTreasury(target, command.NumericValue);
            result.Success = true;
            result.Text = $"{target.name} 国库已增加 {command.NumericValue}，当前为 {treasury}。";
            AutoPanLogService.Info($"{operatorName} 使用管理员指令：{target.name} +{command.NumericValue} 金币");
            return result;
        }

        private static AutoPanCommandResult ExecuteAdminSetGold(AutoPanParsedCommand command, string operatorName, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }
            if (command.NumericValue < 0)
            {
                result.Text = "国家金币不能设置为负数。";
                return result;
            }

            AutoPanKingdomService.SetTreasury(target, command.NumericValue);
            result.Success = true;
            result.Text = $"{target.name} 国库已设置为 {command.NumericValue}。";
            AutoPanLogService.Info($"{operatorName} 使用管理员指令：{target.name} 国库设置为 {command.NumericValue}");
            return result;
        }

        private static AutoPanCommandResult ExecuteAdminViewGold(AutoPanParsedCommand command, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }

            result.Success = true;
            result.Text = $"{target.name} 当前国库：{AutoPanKingdomService.GetTreasury(target)}，国家等级：{AutoPanKingdomService.GetLevel(target)}。";
            return result;
        }

        private static AutoPanCommandResult ExecuteAdminToggleAi(bool enabled, string operatorName, AutoPanCommandResult result)
        {
            ModConfig config = XianniAutoPanMain.Instance.GetConfig();
            config["autopan_config_basic"]["autopan_enable_llm_ai"].SetValue(enabled);
            config.Save();
            result.Success = true;
            result.Text = enabled ? "全局自动盘 AI 已开启。" : "全局自动盘 AI 已关闭。";
            AutoPanLogService.Info($"{operatorName} 使用管理员指令：全局AI -> {(enabled ? "开" : "关")}");
            return result;
        }

        private static AutoPanCommandResult ExecuteAdminViewBinding(AutoPanParsedCommand command, AutoPanCommandResult result)
        {
            AutoPanBindingRecord binding = AutoPanStateRepository.GetBindingSnapshot(command.UserIdArg);
            if (binding == null)
            {
                result.Text = $"userId={command.UserIdArg} 当前没有绑定国家。";
                return result;
            }

            result.Success = true;
            result.Text = $"userId={binding.UserId}，玩家名={binding.PlayerName}，国家={binding.KingdomName}，kingdomId={binding.KingdomId}。";
            return result;
        }

        private static bool TryGetPlayerKingdom(string userId, out Kingdom kingdom, out string error)
        {
            error = string.Empty;
            kingdom = null;
            if (!AutoPanStateRepository.TryGetLiveBinding(userId, out AutoPanBindingRecord binding, out kingdom))
            {
                error = "你当前没有存活绑定国家，请先发送“加入人类/兽人/精灵/矮人”。";
                return false;
            }

            if (kingdom == null || !kingdom.isAlive())
            {
                error = $"你绑定的国家 {binding?.KingdomName ?? "未知国家"} 已经灭亡。";
                return false;
            }
            return true;
        }
    }
}
