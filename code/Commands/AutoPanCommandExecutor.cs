using System;
using System.Collections.Generic;
using System.Globalization;
using NeoModLoader.api;
using XianniAutoPan.AI;
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
        private static readonly HashSet<AutoPanCommandType> InfoCommands = new HashSet<AutoPanCommandType>
        {
            AutoPanCommandType.Help,
            AutoPanCommandType.MyKingdom,
            AutoPanCommandType.KingdomInfo,
            AutoPanCommandType.AllKingdomInfo,
            AutoPanCommandType.CityList,
            AutoPanCommandType.CityInfo,
            AutoPanCommandType.PowerBoard,
            AutoPanCommandType.CountryPowerBoard,
            AutoPanCommandType.CultivatorBoard,
            AutoPanCommandType.AncientBoard,
            AutoPanCommandType.BeastBoard,
            AutoPanCommandType.ScoreRank,
            AutoPanCommandType.CurrentSituationScreenshot,
            AutoPanCommandType.AdminCurrentSituationScreenshot
        };

        private static readonly HashSet<AutoPanCommandType> AiAllowedCommands = new HashSet<AutoPanCommandType>
        {
            AutoPanCommandType.UpgradeNation,
            AutoPanCommandType.UpgradeXiuzhenguo,
            AutoPanCommandType.ChangeKingdomPolicy,
            AutoPanCommandType.GatherSpirit,
            AutoPanCommandType.NationalMilitia,
            AutoPanCommandType.Mobilize,
            AutoPanCommandType.AddPopulation,
            AutoPanCommandType.DeclareWar,
            AutoPanCommandType.SeekPeace,
            AutoPanCommandType.Alliance,
            AutoPanCommandType.LeaveAlliance,
            AutoPanCommandType.ChallengeDuel,
            AutoPanCommandType.BloodlineCreate,
            AutoPanCommandType.KingdomBlessing,
            AutoPanCommandType.AuraSabotage,
            AutoPanCommandType.AssassinateStrongest,
            AutoPanCommandType.CurseEnemy,
            AutoPanCommandType.HeavenPunish,
            AutoPanCommandType.HeavenBless,
            AutoPanCommandType.DisturbKingdom,
            AutoPanCommandType.LowerNation,
            AutoPanCommandType.CultivatorRetreat,
            AutoPanCommandType.CultivatorRealmUp,
            AutoPanCommandType.AncientTrain,
            AutoPanCommandType.AncientStarUp,
            AutoPanCommandType.BeastTrain,
            AutoPanCommandType.BeastStageUp
        };

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

            AutoPanStateRepository.RecordSession(message.SessionId, userId, playerName, message.RemoteEndPoint, message.SourceType, message.ContextId, message.BotSelfId);
            AutoPanNotificationService.RecordRoute(message, playerName);
            AutoPanStateRepository.RefreshPlayerProfile(userId, playerName);
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
                    result.SuppressQqReply = message.SourceType == AutoPanInputSourceType.QqGroup;
                    return result;
                }

                if (message.SourceType == AutoPanInputSourceType.QqGroup && !command.RawText.StartsWith("#", StringComparison.Ordinal))
                {
                    result.Success = false;
                    result.Text = string.Empty;
                    result.SuppressQqReply = true;
                    return result;
                }

                result.Text = "无法识别这条指令，请查看指令书。";
                return result;
            }

            if (command.RawText.StartsWith("#", StringComparison.Ordinal) && message.SourceType == AutoPanInputSourceType.QqGroup && !AutoPanConfigHooks.IsQqAdminAllowed(userId))
            {
                result.Text = "你不在 QQ 管理员白名单中，无法执行管理员指令。";
                return result;
            }

            if (ShouldRollPolicyForCommand(command.CommandType))
            {
                AutoPanConfigHooks.RollRandomPolicyValuesForOperation();
            }

            switch (command.CommandType)
            {
                case AutoPanCommandType.Help:
                    result.Success = true;
                    result.Text = BuildPlayerHelpText();
                    return result;
                case AutoPanCommandType.ScoreRank:
                    result.Success = true;
                    result.Text = AutoPanScoreService.BuildRankingText();
                    return result;
                case AutoPanCommandType.CurrentSituationScreenshot:
                    result.Success = AutoPanScreenshotService.TrySendCurrentSituation(message, bypassCooldown: false, out string screenshotText);
                    result.Text = screenshotText;
                    result.SuppressQqReply = result.Success;
                    return result;
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

                case AutoPanCommandType.JoinExistingKingdom:
                    result.Success = AutoPanKingdomService.TryBindExistingKingdom(userId, playerName, command.TargetName, out Kingdom existingKingdom, out string existingJoinText);
                    result.Text = existingJoinText;
                    if (!result.Success && existingJoinText.StartsWith("找不到目标国家", StringComparison.Ordinal))
                    {
                        result.Success = AutoPanKingdomService.TryCreatePlayerKingdomByCivilizationUnit(userId, playerName, command.TargetName, out existingKingdom, out existingJoinText);
                        result.Text = existingJoinText;
                    }
                    if (existingKingdom != null && result.Success)
                    {
                        AutoPanKingdomSpeechService.ShowSpeech(existingKingdom, playerName, command.RawText, isCommand: true);
                        AutoPanLogService.Info($"{playerName} 执行 {command.RawText} -> {existingJoinText}");
                    }
                    return result;

                case AutoPanCommandType.JoinCivilizationUnit:
                    result.Success = AutoPanKingdomService.TryCreatePlayerKingdomByCivilizationUnit(userId, playerName, command.TargetName, out Kingdom civilizationKingdom, out string civilizationJoinText);
                    result.Text = civilizationJoinText;
                    if (civilizationKingdom != null && result.Success)
                    {
                        AutoPanKingdomSpeechService.ShowSpeech(civilizationKingdom, playerName, command.RawText, isCommand: true);
                        AutoPanLogService.Info($"{playerName} 执行 {command.RawText} -> {civilizationJoinText}");
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
                case AutoPanCommandType.AdminViewPolicy:
                    return ExecuteAdminViewPolicy(result);
                case AutoPanCommandType.AdminSetPolicy:
                    return ExecuteAdminSetPolicy(command, playerName, result);
                case AutoPanCommandType.AdminSetSpeed:
                    return ExecuteAdminSetSpeed(command, playerName, result);
                case AutoPanCommandType.AdminViewSpeedSchedule:
                    return ExecuteAdminViewSpeedSchedule(result);
                case AutoPanCommandType.AdminSetSpeedSchedule:
                    return ExecuteAdminSetSpeedSchedule(command, playerName, result);
                case AutoPanCommandType.AdminEnableSpeedSchedule:
                    return ExecuteAdminToggleSpeedSchedule(true, playerName, result);
                case AutoPanCommandType.AdminDisableSpeedSchedule:
                    return ExecuteAdminToggleSpeedSchedule(false, playerName, result);
                case AutoPanCommandType.AdminSpawnKingdom:
                    return ExecuteAdminSpawnKingdom(command, playerName, result);
                case AutoPanCommandType.AdminEndRound:
                    result.Success = true;
                    result.Text = AutoPanRoundService.EndRound("管理员手动结盘", playerName);
                    return result;
                case AutoPanCommandType.AdminEndRoundNoScore:
                    result.Success = true;
                    result.Text = AutoPanRoundService.EndRoundNoScore("管理员手动结盘（不计积分）", playerName);
                    return result;
                case AutoPanCommandType.AdminCurrentSituationScreenshot:
                    result.Success = AutoPanScreenshotService.TrySendCurrentSituation(message, bypassCooldown: true, out string adminScreenshotText);
                    result.Text = adminScreenshotText;
                    result.SuppressQqReply = result.Success;
                    return result;
                case AutoPanCommandType.AllKingdomInfo:
                    result.Success = true;
                    result.Text = AutoPanKingdomService.BuildAllKingdomInfoText();
                    return result;
            }

            if (!TryGetPlayerKingdom(userId, out Kingdom playerKingdom, out string kingdomError))
            {
                result.Text = kingdomError;
                return result;
            }

            if (IsDeclareWarBlockedByYear(command, "玩家国家", out string yearLimitText))
            {
                result.Text = yearLimitText;
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
            if (!AutoPanConfigHooks.EnableLlmAi)
            {
                message = "全局自动盘 AI 未开启。";
                return false;
            }

            AutoPanParsedCommand command = AutoPanCommandParser.Parse(commandText);
            if (command.CommandType == AutoPanCommandType.Unknown)
            {
                message = $"AI 指令无法解析：{commandText}";
                return false;
            }
            if (!AiAllowedCommands.Contains(command.CommandType))
            {
                message = $"AI 指令不在允许范围内：{commandText}";
                return false;
            }
            if (IsDeclareWarBlockedByYear(command, "AI 国家", out string yearLimitText))
            {
                message = yearLimitText;
                return false;
            }

            AutoPanConfigHooks.RollRandomPolicyValuesForOperation();
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
                case AutoPanCommandType.Help:
                    result.Success = true;
                    result.Text = BuildPlayerHelpText();
                    return result;
                case AutoPanCommandType.MyKingdom:
                case AutoPanCommandType.KingdomInfo:
                    result.Success = true;
                    result.Text = AutoPanKingdomService.BuildKingdomInfoText(kingdom);
                    return result;
                case AutoPanCommandType.AllKingdomInfo:
                    result.Success = true;
                    result.Text = AutoPanKingdomService.BuildAllKingdomInfoText();
                    return result;
                case AutoPanCommandType.RenameKingdom:
                    return ExecuteRenameKingdom(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.CityList:
                    result.Success = true;
                    result.Text = AutoPanCityService.BuildCityListText(kingdom);
                    return result;
                case AutoPanCommandType.CityInfo:
                    return ExecuteCityInfo(kingdom, command, result);
                case AutoPanCommandType.UpgradeNation:
                    return ExecuteUpgradeNation(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.UpgradeXiuzhenguo:
                    return ExecuteUpgradeXiuzhenguo(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.ChangeKingdomPolicy:
                    return ExecuteChangeKingdomPolicy(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.GatherSpirit:
                    return ExecuteGatherSpirit(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.NationalMilitia:
                    return ExecuteNationalMilitia(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.Mobilize:
                    return ExecuteMobilize(kingdom, operatorName, isAi, result);
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
                case AutoPanCommandType.AcceptAlliance:
                    return ExecuteAllianceResponse(kingdom, command, operatorName, isAi, result, accept: true);
                case AutoPanCommandType.RejectAlliance:
                    return ExecuteAllianceResponse(kingdom, command, operatorName, isAi, result, accept: false);
                case AutoPanCommandType.LeaveAlliance:
                    return ExecuteLeaveAlliance(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.ChallengeDuel:
                    return ExecuteChallengeDuel(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.AcceptDuel:
                    return ExecuteDuelResponse(kingdom, command, operatorName, isAi, result, accept: true);
                case AutoPanCommandType.RejectDuel:
                    return ExecuteDuelResponse(kingdom, command, operatorName, isAi, result, accept: false);
                case AutoPanCommandType.BloodlineCreate:
                    return ExecuteBloodlineCreate(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.PowerBoard:
                    result.Success = true;
                    result.Text = AutoPanInteractionService.BuildTopPowerBoardText();
                    return result;
                case AutoPanCommandType.CountryPowerBoard:
                    result.Success = true;
                    result.Text = AutoPanInteractionService.BuildCountryPowerBoardText(kingdom);
                    return result;
                case AutoPanCommandType.AuraSabotage:
                    return ExecuteAuraSabotage(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.AssassinateStrongest:
                    return ExecuteAssassinateStrongest(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.CurseEnemy:
                    return ExecuteCurseEnemy(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.KingdomBlessing:
                    return ExecuteKingdomBlessing(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.HeavenPunish:
                    return ExecuteHeavenPunish(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.HeavenBless:
                    return ExecuteHeavenBless(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.DisturbKingdom:
                    return ExecuteDisturbKingdom(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.MeteorStrike:
                    return ExecuteMeteorStrike(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.StartTournament:
                    return ExecuteStartTournament(kingdom, operatorName, isAi, result);
                case AutoPanCommandType.CultivatorSuppress:
                    return ExecuteCultivatorSuppress(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.AncientSuppress:
                    return ExecuteAncientSuppress(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.BeastSuppress:
                    return ExecuteBeastSuppress(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.LowerNation:
                    return ExecuteLowerNation(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.CultivatorBoard:
                case AutoPanCommandType.AncientBoard:
                case AutoPanCommandType.BeastBoard:
                    result.Success = true;
                    result.Text = AutoPanKingdomService.BuildBoardText(kingdom, command.CommandType);
                    return result;
                case AutoPanCommandType.ScoreRank:
                    result.Success = true;
                    result.Text = AutoPanScoreService.BuildRankingText();
                    return result;
                case AutoPanCommandType.FastAdult:
                    return ExecuteFastAdult(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.ConscriptArmy:
                    return ExecuteConscriptArmy(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.TransferCity:
                    return ExecuteTransferCity(kingdom, command, operatorName, isAi, result);
                case AutoPanCommandType.RandomTransferCity:
                    return ExecuteRandomTransferCity(kingdom, command, operatorName, isAi, result);
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
            int totalCost = requestedCount * AutoPanConfigHooks.PlaceRuinCost;
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

            int refund = totalCost - placedCount * AutoPanConfigHooks.PlaceRuinCost;
            if (refund > 0)
            {
                AutoPanKingdomService.AddTreasury(kingdom, refund);
            }

            result.Success = true;
            result.Text = $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 已放置 {placedCount} 座遗迹，消耗 {placedCount * AutoPanConfigHooks.PlaceRuinCost} 金币。";
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
            if (string.IsNullOrWhiteSpace(command.TargetName))
            {
                result.Success = AutoPanCityService.TryConscriptArmyAllCities(kingdom, command.NumericValue, out string allMessage);
                result.Text = allMessage;
                if (result.Success)
                {
                    AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 全国征集军队：{kingdom.name} / {command.TextArg}");
                }
                return result;
            }

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

        private static AutoPanCommandResult ExecuteRandomTransferCity(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanCityService.TryTransferRandomNonCapitalCity(kingdom, command.TargetName, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 随机移交一座非首都城市给 {command.TargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 随机移交城市：{kingdom.name} -> {command.TargetName}");
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
            int cost = AutoPanConfigHooks.NationUpgradeCostPerLevel * level;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out string error))
            {
                result.Text = error;
                return result;
            }

            if (!AutoPanKingdomService.TryAdjustNationLevel(kingdom, 1, out int newLevel, out error))
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                result.Text = error;
                return result;
            }

            result.Success = true;
            result.Text = $"{kingdom.name} 升级国运成功，消耗 {cost} 金币，国家等级提升到 {newLevel}。修真国等级不会随国运自动提升，需要单独发送“升级修真国”。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 国运提升至 {newLevel} 级");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 执行升级国运：{kingdom.name} -> Lv{newLevel}");
            return result;
        }

        private static AutoPanCommandResult ExecuteUpgradeXiuzhenguo(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            int previousXiuzhenguoLevel = XianniAutoPanApi.CalculateXiuzhenguoLevel(kingdom);
            if (previousXiuzhenguoLevel >= AutoPanKingdomService.MaxXiuzhenguoLevel)
            {
                result.Text = "当前修真国等级已达到上限。";
                return result;
            }

            int targetLevel = previousXiuzhenguoLevel + 1;
            int cost = AutoPanConfigHooks.XiuzhenguoUpgradeCostPerLevel * Math.Max(1, targetLevel);
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out string error))
            {
                result.Text = error;
                return result;
            }

            if (!AutoPanKingdomService.TryPromoteXiuzhenguoNaturally(kingdom, out previousXiuzhenguoLevel, out targetLevel, out int spawnedCount, out error))
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                result.Text = error;
                return result;
            }

            int visibleXiuzhenguoLevel = XianniAutoPanApi.RefreshXiuzhenguoLevel(kingdom);
            result.Success = true;
            result.Text = $"{kingdom.name} 升级修真国成功，消耗 {cost} 金币，修真国由 {previousXiuzhenguoLevel} 级推进到 {visibleXiuzhenguoLevel} 级，本次召来 {spawnedCount} 名达标修士。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 修真国提升至 {visibleXiuzhenguoLevel} 级");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 执行升级修真国：{kingdom.name} -> XZG{visibleXiuzhenguoLevel} / spawned={spawnedCount}");
            return result;
        }

        private static AutoPanCommandResult ExecuteRenameKingdom(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            int cost = AutoPanConfigHooks.RenameKingdomCost;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out string spendError))
            {
                result.Text = spendError;
                return result;
            }

            string oldLabel = AutoPanKingdomService.FormatKingdomLabel(kingdom);
            if (!AutoPanKingdomService.TryRenameKingdom(kingdom, command.TextArg, out string finalName, out string renameError))
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                result.Text = renameError;
                return result;
            }

            result.Success = true;
            result.Text = $"{oldLabel} 已改名为 {AutoPanKingdomService.FormatKingdomLabel(kingdom)}，消耗 {cost} 金币。";
            XianniAutoPanApi.Broadcast($"{oldLabel} 已更名为 {finalName}");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 国家改名：{oldLabel} -> {finalName}");
            return result;
        }

        private static AutoPanCommandResult ExecuteGatherSpirit(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConfigHooks.GatherSpiritCost, out string error))
            {
                result.Text = error;
                return result;
            }

            AutoPanKingdomService.ActivateGatherSpirit(kingdom);
            int untilYear = AutoPanKingdomService.GetGatherSpiritUntilYear(kingdom);
            result.Success = true;
            result.Text = $"{kingdom.name} 已开启聚灵国策，持续到第 {untilYear} 年。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 开启聚灵国策，灵气汇聚 {AutoPanConfigHooks.GatherSpiritDurationYears} 年");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 开启聚灵：{kingdom.name} -> {untilYear}");
            return result;
        }

        private static AutoPanCommandResult ExecuteChangeKingdomPolicy(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanKingdomService.TryChangeOccupationPolicy(kingdom, command.TextArg, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 调整国家政策为 {AutoPanKingdomService.GetOccupationPolicyText(kingdom)}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 调整国家政策：{kingdom.name} -> {AutoPanKingdomService.GetOccupationPolicyText(kingdom)}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteNationalMilitia(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanKingdomService.TryActivateNationalMilitia(kingdom, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 开启全民皆兵，持续 {AutoPanConfigHooks.NationalMilitiaDurationYears} 年");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 全民皆兵：{kingdom.name}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteMobilize(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanKingdomService.TryMobilizeForWar(kingdom, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 发起战争动员");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 动员：{kingdom.name}");
            }
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
            int cost = AutoPanConfigHooks.DeclareWarCost;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, cost, out string error))
            {
                result.Text = error;
                return result;
            }

            War war = World.world.diplomacy.startWar(kingdom, target, WarTypeLibrary.normal);
            if (war == null)
            {
                AutoPanKingdomService.AddTreasury(kingdom, cost);
                result.Text = $"对 {target.name} 宣战失败。";
                return result;
            }

            result.Success = true;
            result.Text = $"{kingdom.name} 已向 {target.name} 宣战，消耗 {cost} 金币。";
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
            int offerCost = AutoPanConfigHooks.SeekPeaceCost;
            int totalCost = offerCost * 2;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, totalCost, out string error))
            {
                result.Text = error;
                return result;
            }

            World.world.wars.endWar(war, WarWinner.Peace);
            result.Success = true;
            result.Text = $"{kingdom.name} 已向 {target.name} 求和，先提交求和礼金 {offerCost} 金币，再消耗求和成本 {offerCost} 金币，共消耗 {totalCost} 金币。";
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
            result.Success = AutoPanRequestService.TryCreateAllianceRequest(kingdom, target, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 向 {target.name} 发出结盟请求");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 发起结盟请求：{kingdom.name} -> {target.name}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteAllianceResponse(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result, bool accept)
        {
            result.Success = AutoPanRequestService.TryRespondAllianceRequest(kingdom, command.TargetName, accept, out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} {(accept ? "同意" : "拒绝")}结盟请求：{kingdom.name} / {command.TargetName}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteLeaveAlliance(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            Alliance alliance = kingdom.getAlliance();
            if (alliance == null)
            {
                result.Text = "当前国家并未加入任何联盟。";
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConfigHooks.LeaveAllianceCost, out string error))
            {
                result.Text = error;
                return result;
            }
            result.Success = AutoPanKingdomService.TryLeaveAlliance(kingdom, out string allianceName);
            if (!result.Success)
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConfigHooks.LeaveAllianceCost);
                result.Text = "退盟失败，请稍后再试。";
                return result;
            }

            result.Text = $"{kingdom.name} 已退出联盟 {allianceName}，消耗 {AutoPanConfigHooks.LeaveAllianceCost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 已退出联盟 {allianceName}");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 退盟：{kingdom.name} / {allianceName}");
            return result;
        }

        private static AutoPanCommandResult ExecuteChallengeDuel(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }
            if (target == kingdom)
            {
                result.Text = "不能向自己的国家发起约斗。";
                return result;
            }

            result.Success = AutoPanRequestService.TryCreateDuelRequest(kingdom, target, command.BetAmount, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 向 {target.name} 发出约斗请求{(command.BetAmount > 0 ? $"，赌注 {command.BetAmount} 金币" : string.Empty)}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 发起约斗：{kingdom.name} -> {target.name} / bet={command.BetAmount}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteDuelResponse(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result, bool accept)
        {
            result.Success = AutoPanRequestService.TryRespondDuelRequest(kingdom, command.TargetName, accept, out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} {(accept ? "同意" : "拒绝")}约斗：{kingdom.name} / {command.TargetName}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteBloodlineCreate(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TryCreateFounderBloodline(kingdom, command.ObjectIdArg, out string message);
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

        private static AutoPanCommandResult ExecuteAncientSuppress(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TrySuppressEnemyAncients(kingdom, command.TargetName, command.NumericValue, command.SecondaryNumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 发动古神压境：{command.TargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 古神压境：{kingdom.name} -> {command.TargetName} / 人数{command.NumericValue} / 层数{command.SecondaryNumericValue}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteBeastSuppress(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TrySuppressEnemyBeasts(kingdom, command.TargetName, command.NumericValue, command.SecondaryNumericValue, out string message);
            result.Text = message;
            if (result.Success)
            {
                XianniAutoPanApi.Broadcast($"{kingdom.name} 发动妖兽压境：{command.TargetName}");
                AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 妖兽压境：{kingdom.name} -> {command.TargetName} / 人数{command.NumericValue} / 层数{command.SecondaryNumericValue}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteLowerNation(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }

            if (target == kingdom)
            {
                result.Text = "不能对自己的国家降低国运。";
                return result;
            }

            int levels = Math.Max(1, command.NumericValue);
            int totalCost = AutoPanConfigHooks.LowerNationCostPerLevel * levels;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, totalCost, out string spendError))
            {
                result.Text = spendError;
                return result;
            }

            bool nationAdjusted = AutoPanKingdomService.TryAdjustNationLevel(target, -levels, out int newNationLevel, out _);
            bool xiuzhenguoAdjusted = AutoPanKingdomService.TryLowerXiuzhenguoNaturally(target, levels, out int previousXiuzhenguoLevel, out int newXiuzhenguoLevel, out int killedCount, out string lowerError);
            if (!nationAdjusted && !xiuzhenguoAdjusted)
            {
                AutoPanKingdomService.AddTreasury(kingdom, totalCost);
                result.Text = string.IsNullOrWhiteSpace(lowerError)
                    ? $"{AutoPanKingdomService.FormatKingdomLabel(target)} 的国运与修真国等级都已降到最低，无法继续降低。"
                    : lowerError;
                return result;
            }

            AutoPanKingdomService.ClearSnapshotCache(target.getID());
            result.Success = true;
            result.Text = $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 已压制 {AutoPanKingdomService.FormatKingdomLabel(target)} {levels} 级，国家等级现为 {AutoPanKingdomService.GetLevel(target)}，修真国由 {previousXiuzhenguoLevel} 级自然降到 {XianniAutoPanApi.GetKingdomSnapshot(target, 0, 0, 0).XiuzhenguoLevel} 级，本次斩首 {killedCount} 名达标修士，消耗 {totalCost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 对 {target.name} 发动国运压制");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 降低国运：{kingdom.name} -> {target.name} / {levels} / nationLv={newNationLevel} / xzgLv={newXiuzhenguoLevel} / killed={killedCount}");
            return result;
        }

        private static AutoPanCommandResult ExecuteCultivatorRetreat(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActorById(kingdom, AutoPanCommandType.CultivatorBoard, command.ObjectIdArg, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConfigHooks.CultivatorRetreatCost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!XianniAutoPanApi.TryAddXiuwei(actor, AutoPanConfigHooks.ClosedDoorXiuweiGain))
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConfigHooks.CultivatorRetreatCost);
                result.Text = "闭关失败，未能为修士增加修为。";
                return result;
            }
            XianniAutoPanApi.TryTriggerBreakthrough(actor);
            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 闭关成功，获得 {AutoPanConfigHooks.ClosedDoorXiuweiGain} 修为。";
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 修士闭关：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteCultivatorRealmUp(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActorById(kingdom, AutoPanCommandType.CultivatorBoard, command.ObjectIdArg, out Actor actor, out string error))
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
            if (!AutoPanKingdomService.TryGetBoardActorById(kingdom, AutoPanCommandType.AncientBoard, command.ObjectIdArg, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConfigHooks.AncientTrainCost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!XianniAutoPanApi.TryAddAncientPower(actor, AutoPanConfigHooks.AncientTrainingGain))
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConfigHooks.AncientTrainCost);
                result.Text = "炼体失败，未能增加古神之力。";
                return result;
            }
            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 炼体成功，获得 {AutoPanConfigHooks.AncientTrainingGain} 古神之力。";
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 古神炼体：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteAncientStarUp(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActorById(kingdom, AutoPanCommandType.AncientBoard, command.ObjectIdArg, out Actor actor, out string error))
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
            if (!AutoPanKingdomService.TryGetBoardActorById(kingdom, AutoPanCommandType.BeastBoard, command.ObjectIdArg, out Actor actor, out string error))
            {
                result.Text = error;
                return result;
            }
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConfigHooks.BeastTrainCost, out error))
            {
                result.Text = error;
                return result;
            }
            if (!XianniAutoPanApi.TryAddBeastPower(actor, AutoPanConfigHooks.BeastTrainingGain))
            {
                AutoPanKingdomService.AddTreasury(kingdom, AutoPanConfigHooks.BeastTrainCost);
                result.Text = "养成失败，未能增加妖力。";
                return result;
            }
            AutoPanKingdomService.ClearSnapshotCache(kingdom.getID());
            result.Success = true;
            result.Text = $"{actor.getName()} 养成成功，获得 {AutoPanConfigHooks.BeastTrainingGain} 妖力。";
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 妖兽养成：{kingdom.name} / {actor.getName()}");
            return result;
        }

        private static AutoPanCommandResult ExecuteBeastStageUp(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryGetBoardActorById(kingdom, AutoPanCommandType.BeastBoard, command.ObjectIdArg, out Actor actor, out string error))
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
            AutoPanConfigHooks.OnEnableLlmAiChanged(enabled);
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

        private static AutoPanCommandResult ExecuteAdminViewPolicy(AutoPanCommandResult result)
        {
            result.Success = true;
            result.Text = AutoPanConfigHooks.BuildPolicyText();
            return result;
        }

        private static AutoPanCommandResult ExecuteAdminSetPolicy(AutoPanParsedCommand command, string operatorName, AutoPanCommandResult result)
        {
            ModConfig config = XianniAutoPanMain.Instance.GetConfig();
            result.Success = AutoPanConfigHooks.TrySetPolicy(config, command.TargetName, command.NumericValue.ToString(), out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName} 使用管理员指令：设置政策 {command.TargetName}={command.NumericValue}");
            }
            return result;
        }

        private static bool IsDeclareWarBlockedByYear(AutoPanParsedCommand command, string actorText, out string message)
        {
            message = string.Empty;
            if (command.CommandType != AutoPanCommandType.DeclareWar && command.CommandType != AutoPanCommandType.MeteorStrike)
            {
                return false;
            }

            int currentYear = Date.getCurrentYear();
            int startYear = AutoPanConfigHooks.PlayerDecisionStartYear;
            if (currentYear >= startYear)
            {
                return false;
            }

            string actionText = command.CommandType == AutoPanCommandType.MeteorStrike ? "释放陨石" : "宣战";
            message = $"当前为第 {currentYear} 年，{actorText}需到第 {startYear} 年后才能{actionText}；其它指令不受该年份限制。";
            return true;
        }

        private static bool IsInfoCommand(AutoPanCommandType commandType)
        {
            return InfoCommands.Contains(commandType);
        }

        private static bool ShouldRollPolicyForCommand(AutoPanCommandType commandType)
        {
            if (IsInfoCommand(commandType))
            {
                return false;
            }

            switch (commandType)
            {
                case AutoPanCommandType.Unknown:
                case AutoPanCommandType.AdminViewPolicy:
                case AutoPanCommandType.AdminSetPolicy:
                case AutoPanCommandType.AdminViewBinding:
                case AutoPanCommandType.AdminSetSpeed:
                case AutoPanCommandType.AdminViewSpeedSchedule:
                case AutoPanCommandType.AdminSetSpeedSchedule:
                case AutoPanCommandType.AdminEnableSpeedSchedule:
                case AutoPanCommandType.AdminDisableSpeedSchedule:
                case AutoPanCommandType.AdminCurrentSituationScreenshot:
                    return false;
                default:
                    return true;
            }
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

        private static AutoPanCommandResult ExecuteAdminSetSpeed(AutoPanParsedCommand command, string operatorName, AutoPanCommandResult result)
        {
            result.Success = AutoPanWorldSpeedService.TryApplyManualSpeed(command.TextArg, out string speedText, out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName} 设置游戏倍速为 {speedText}x");
            }
            return result;
        }

        /// <summary>
        /// 查看当前按年份自动切换的倍速计划。
        /// </summary>
        private static AutoPanCommandResult ExecuteAdminViewSpeedSchedule(AutoPanCommandResult result)
        {
            result.Success = true;
            result.Text = AutoPanWorldSpeedService.BuildScheduleText();
            return result;
        }

        /// <summary>
        /// 设置按年份自动切换的倍速计划。
        /// </summary>
        private static AutoPanCommandResult ExecuteAdminSetSpeedSchedule(AutoPanParsedCommand command, string operatorName, AutoPanCommandResult result)
        {
            result.Success = AutoPanConfigHooks.TrySetWorldSpeedSchedule(command.TargetName, out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName} 设置倍速计划：{command.TargetName}");
            }
            return result;
        }

        /// <summary>
        /// 开启或关闭按年份自动切换的倍速计划。
        /// </summary>
        private static AutoPanCommandResult ExecuteAdminToggleSpeedSchedule(bool enabled, string operatorName, AutoPanCommandResult result)
        {
            result.Success = AutoPanConfigHooks.TrySetWorldSpeedScheduleEnabled(enabled ? "1" : "0", out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName} {(enabled ? "开启" : "关闭")}倍速计划");
            }
            return result;
        }

        /// <summary>
        /// 判断指定速度资产是否为自动盘的自定义速度资产。
        /// </summary>
        internal static bool IsCustomWorldSpeedAsset(WorldTimeScaleAsset asset)
        {
            return AutoPanWorldSpeedService.IsCustomWorldSpeedAsset(asset);
        }

        /// <summary>
        /// 为原生滚轮与热键切速提供自定义速度资产的邻接原生档位。
        /// </summary>
        internal static WorldTimeScaleAsset GetAdjacentNativeWorldSpeedAsset(bool next, bool cycle)
        {
            return AutoPanWorldSpeedService.GetAdjacentNativeWorldSpeedAsset(next, cycle);
        }

        private static AutoPanCommandResult ExecuteAdminSpawnKingdom(AutoPanParsedCommand command, string operatorName, AutoPanCommandResult result)
        {
            result.Success = AutoPanKingdomService.TrySpawnUnboundKingdom(command.TargetName, out string message);
            result.Text = message;
            if (result.Success)
            {
                AutoPanLogService.Info($"{operatorName} 管理员生成无绑定国家：{command.TargetName}");
            }
            return result;
        }

        private static AutoPanCommandResult ExecuteHeavenPunish(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConfigHooks.HeavenPunishCost, out string spendError))
            {
                result.Text = spendError;
                return result;
            }
            result.Success = AutoPanInteractionService.TryHeavenPunish(command.TargetName, out string message);
            result.Text = message;
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 天运惩罚：{command.TargetName}");
            return result;
        }

        private static AutoPanCommandResult ExecuteHeavenBless(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, AutoPanConfigHooks.HeavenBlessCost, out string spendError))
            {
                result.Text = spendError;
                return result;
            }
            result.Success = AutoPanInteractionService.TryHeavenBless(command.TargetName, out string message);
            result.Text = message;
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 天运赐福：{command.TargetName}");
            return result;
        }

        private static AutoPanCommandResult ExecuteDisturbKingdom(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanInteractionService.TryDisturbKingdom(kingdom, command.TargetName, out string message);
            result.Text = message;
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 扰动国家：{command.TargetName}");
            return result;
        }

        private static AutoPanCommandResult ExecuteMeteorStrike(Kingdom kingdom, AutoPanParsedCommand command, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            if (!AutoPanKingdomService.TryResolveKingdom(command.TargetName, out Kingdom target, out string resolveError))
            {
                result.Text = resolveError;
                return result;
            }

            if (target == kingdom)
            {
                result.Text = "不能对自己的国家释放陨石。";
                return result;
            }

            int maxCount = Math.Max(1, AutoPanConfigHooks.MeteorMaxCount);
            int count = Math.Max(1, Math.Min(command.NumericValue, maxCount));
            int costPerStone = Math.Max(0, AutoPanConfigHooks.MeteorCostPerStone);
            long totalCostLong = (long)costPerStone * count;
            if (totalCostLong > int.MaxValue)
            {
                result.Text = "本次陨石花费超过自动盘国库可处理上限，请降低数量或单价。";
                return result;
            }

            int totalCost = (int)totalCostLong;
            if (!AutoPanKingdomService.TrySpendTreasury(kingdom, totalCost, out string spendError))
            {
                result.Text = spendError;
                return result;
            }

            if (!AutoPanKingdomService.TrySpawnMeteoritesInKingdom(target, count, out int spawnedCount, out string meteorMessage))
            {
                AutoPanKingdomService.AddTreasury(kingdom, totalCost);
                result.Text = meteorMessage + $" 已退回 {totalCost} 金币。";
                return result;
            }

            result.Success = true;
            result.Text = $"{AutoPanKingdomService.FormatKingdomLabel(kingdom)} 已对 {AutoPanKingdomService.FormatKingdomLabel(target)} 释放 {spawnedCount} 颗陨石，消耗 {totalCost} 金币。";
            XianniAutoPanApi.Broadcast($"{kingdom.name} 对 {target.name} 释放了 {spawnedCount} 颗陨石");
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 陨石：{kingdom.name} -> {target.name} / count={spawnedCount} / cost={totalCost}");
            return result;
        }

        private static AutoPanCommandResult ExecuteStartTournament(Kingdom kingdom, string operatorName, bool isAi, AutoPanCommandResult result)
        {
            result.Success = AutoPanTournamentService.TryStart(kingdom, result.UserId, operatorName, out string message);
            result.Text = message;
            AutoPanLogService.Info($"{operatorName}{(isAi ? "(AI)" : string.Empty)} 开启比武大会：{kingdom.name} / success={result.Success}");
            return result;
        }

        private static string BuildPlayerHelpText()
        {
            return string.Join("\n", new[]
            {
                "玩家指令总览：",
                "加入人类 / 加入兽人 / 加入精灵 / 加入矮人(其它文明种族)",
                "加入 国家名（绑定现有无主国）",
                "我的国家 / 国家信息",
                "当前局势 / 查看所有国家信息 / 玩家排名",
                "国家改名 新名字",
                "城市列表 / 城市信息",
                "升级国运 / 升级修真国",
                "降低国运 目标国家 [kingdomId] 1",
                "政策 开放占领 / 政策 坚守城池",
                "国策 聚灵 / 全民皆兵 / 动员",
                "增加人数 10 / 放置遗迹 1",
                "转账 目标国家 [kingdomId] 1000",
                "宣战 目标国家 [kingdomId] 或 宣战 @对方",
                "求和 目标国家 [kingdomId] 或 求和 @对方",
                "结盟 目标国家 [kingdomId] 或 结盟 @对方",
                "同意结盟 / 拒绝结盟 / 退盟",
                "约斗 目标国家 [kingdomId] 500",
                "血脉创立 单位id",
                "天榜 / 战力榜",
                "削灵 目标国家 [kingdomId] 500",
                "斩首 目标国家 [kingdomId]",
                "诅咒 目标国家 [kingdomId] 3",
                "国家祝福 全员 或 国家祝福 5",
                "修士降境 目标国家 [kingdomId] 3 1",
                "古神降星 目标国家 [kingdomId] 3 1",
                "妖兽降阶 目标国家 [kingdomId] 3 1",
                "修士榜 / 古神榜 / 妖兽榜",
                "修士 单位id 闭关 / 修士 单位id 升境",
                "古神 单位id 炼体 / 古神 单位id 升星",
                "妖兽 单位id 养成 / 妖兽 单位id 升阶",
                "快速成年 全城 或 快速成年 城市名 [cityId]",
                "征集军队 城市名 [cityId] 全部/ 征集军队",
                "移交城市 城市名 [cityId] 给 目标国家 [kingdomId]",
                "移交 目标国家 [kingdomId]随机一座城市",
                "军备 城市名 [cityId] 精金 全军",
                "天运惩罚(赐福) 目标国家（可@）",
                "扰动国家 目标国家（可@）",
                "陨石 目标国家（可@） 数量",
                "开启比武大会",
                "#5x / #查看倍速计划 / #设置倍速计划 1年5倍速，10年20倍速 / #开启倍速计划（管理员）",
                "#结盘（管理员）"
            });
        }
    }
}
