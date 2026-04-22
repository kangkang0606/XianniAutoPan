using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using xn.tournament;
using xn.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 管理国家最强者之间的专属约斗擂台。
    /// </summary>
    internal static class AutoPanDuelService
    {
        private sealed class ActiveDuel
        {
            /// <summary>
            /// 发起国 ID。
            /// </summary>
            public long ChallengerKingdomId;

            /// <summary>
            /// 应战国 ID。
            /// </summary>
            public long DefenderKingdomId;

            /// <summary>
            /// 一号参战者 ID。
            /// </summary>
            public long FighterAId;

            /// <summary>
            /// 二号参战者 ID。
            /// </summary>
            public long FighterBId;

            /// <summary>
            /// 一号原始坐标。
            /// </summary>
            public Vector2 OriginalTileA;

            /// <summary>
            /// 二号原始坐标。
            /// </summary>
            public Vector2 OriginalTileB;

            /// <summary>
            /// 擂台中心。
            /// </summary>
            public WorldTile ArenaCenterTile;

            /// <summary>
            /// 开始时间。
            /// </summary>
            public float StartTime;

            /// <summary>
            /// 一号名称。
            /// </summary>
            public string FighterAName;

            /// <summary>
            /// 二号名称。
            /// </summary>
            public string FighterBName;

            /// <summary>
            /// 本场约斗赌注。
            /// </summary>
            public int BetAmount;
        }

        private const float DuelTimeoutSeconds = 60f;
        private const float DuelTantrumSeconds = 300f;
        private const float DuelDefeatHpThreshold = 0.1f;
        private static ActiveDuel _activeDuel;

        /// <summary>
        /// 当前是否存在进行中的约斗。
        /// </summary>
        public static bool IsRunning => _activeDuel != null;

        /// <summary>
        /// 尝试以两国战力前五随机强者开启专属约斗。
        /// </summary>
        public static bool TryStartStrongestDuel(Kingdom challenger, Kingdom defender, int betAmount, out string message)
        {
            message = string.Empty;
            if (_activeDuel != null)
            {
                message = "当前已有一场约斗正在进行，请等待上一场结束。";
                return false;
            }

            if (TournamentManager.IsRunning)
            {
                message = "仙逆比武大会正在进行中，不能开启约斗。";
                return false;
            }

            if (!AutoPanInteractionService.TryGetRandomTopPowerActor(challenger, out Actor fighterA, out string fighterASummary))
            {
                message = fighterASummary;
                return false;
            }
            if (!AutoPanInteractionService.TryGetRandomTopPowerActor(defender, out Actor fighterB, out string fighterBSummary))
            {
                message = fighterBSummary;
                return false;
            }

            WorldTile arenaCenter = TournamentArena.FindOceanTileForArena();
            if (arenaCenter == null)
            {
                message = "当前世界找不到合适的海域擂台，无法开启约斗。";
                return false;
            }

            TournamentArena.BuildArena(arenaCenter);
            var arenaTiles = TournamentArena.GetArenaTiles(arenaCenter);
            if (arenaTiles == null || arenaTiles.Count < 2)
            {
                TournamentArena.CleanupArena(arenaCenter);
                message = "约斗擂台建造失败，请稍后再试。";
                return false;
            }

            _activeDuel = new ActiveDuel
            {
                ChallengerKingdomId = challenger.getID(),
                DefenderKingdomId = defender.getID(),
                FighterAId = fighterA.getID(),
                FighterBId = fighterB.getID(),
                OriginalTileA = ResolveOriginalTile(fighterA, challenger),
                OriginalTileB = ResolveOriginalTile(fighterB, defender),
                ArenaCenterTile = arenaCenter,
                StartTime = Time.time,
                FighterAName = fighterA.getName(),
                FighterBName = fighterB.getName(),
                BetAmount = Mathf.Max(0, betAmount)
            };

            PrepareFighter(fighterA, fighterB, arenaTiles[0]);
            PrepareFighter(fighterB, fighterA, arenaTiles[arenaTiles.Count / 2]);

            string betText = _activeDuel.BetAmount > 0 ? $"，赌注 {_activeDuel.BetAmount} 金币" : string.Empty;
            XianniAutoPanApi.Broadcast($"约斗开启：{AutoPanKingdomService.FormatKingdomLabel(challenger)} 的 {fighterA.getName()} 对战 {AutoPanKingdomService.FormatKingdomLabel(defender)} 的 {fighterB.getName()}{betText}");
            message = $"{AutoPanKingdomService.FormatKingdomLabel(challenger)} 与 {AutoPanKingdomService.FormatKingdomLabel(defender)} 的约斗已开启：双方各从国家战力前 5 随机出战，{fighterA.getName()} VS {fighterB.getName()}{betText}。";
            return true;
        }

        /// <summary>
        /// 每帧维护进行中的约斗。
        /// </summary>
        public static void Update()
        {
            if (_activeDuel == null)
            {
                return;
            }

            Actor fighterA = World.world?.units?.get(_activeDuel.FighterAId);
            Actor fighterB = World.world?.units?.get(_activeDuel.FighterBId);
            if (fighterA == null && fighterB == null)
            {
                FinishDuel(null, null, "约斗结束：双方均已失去作战资格。");
                return;
            }

            if (ShouldFinishByDefeat(fighterA, fighterB, out Actor winner, out Actor loser, out string reason))
            {
                FinishDuel(winner, loser, reason);
                return;
            }

            if (Time.time - _activeDuel.StartTime >= DuelTimeoutSeconds)
            {
                ResolveTimeout(fighterA, fighterB, out winner, out loser, out reason);
                FinishDuel(winner, loser, reason);
                return;
            }

            EnsureFighting(fighterA, fighterB);
            EnsureFighting(fighterB, fighterA);
            EnsureStayInArena(fighterA);
            EnsureStayInArena(fighterB);
        }

        /// <summary>
        /// 清理当前约斗状态。
        /// </summary>
        public static void ClearAll()
        {
            if (_activeDuel == null)
            {
                return;
            }

            Actor fighterA = World.world?.units?.get(_activeDuel.FighterAId);
            Actor fighterB = World.world?.units?.get(_activeDuel.FighterBId);
            RestoreFighter(fighterA, _activeDuel.OriginalTileA, healToFull: true);
            RestoreFighter(fighterB, _activeDuel.OriginalTileB, healToFull: true);
            if (_activeDuel.ArenaCenterTile != null)
            {
                TournamentArena.CleanupArena(_activeDuel.ArenaCenterTile);
            }

            _activeDuel = null;
        }

        private static void PrepareFighter(Actor fighter, Actor enemy, WorldTile spawnTile)
        {
            if (fighter == null || !fighter.isAlive() || spawnTile == null)
            {
                return;
            }

            fighter.cancelAllBeh();
            fighter.clearAttackTarget();
            fighter.data.health = fighter.getMaxHealth();
            fighter.finishStatusEffect("cursed");
            fighter.addStatusEffect("tantrum", DuelTantrumSeconds);
            fighter.spawnOn(spawnTile);
            if (enemy != null && enemy.isAlive())
            {
                fighter.addAggro(enemy);
                fighter.startFightingWith(enemy);
            }
        }

        private static bool ShouldFinishByDefeat(Actor fighterA, Actor fighterB, out Actor winner, out Actor loser, out string reason)
        {
            winner = null;
            loser = null;
            reason = string.Empty;

            bool aliveA = fighterA != null && fighterA.isAlive();
            bool aliveB = fighterB != null && fighterB.isAlive();
            if (!aliveA && !aliveB)
            {
                reason = "约斗结束：双方同归于尽。";
                return true;
            }

            if (!aliveA)
            {
                winner = fighterB;
                loser = fighterA;
                reason = $"{_activeDuel.FighterBName} 取得约斗胜利，{_activeDuel.FighterAName} 已倒下。";
                return true;
            }

            if (!aliveB)
            {
                winner = fighterA;
                loser = fighterB;
                reason = $"{_activeDuel.FighterAName} 取得约斗胜利，{_activeDuel.FighterBName} 已倒下。";
                return true;
            }

            float hpA = fighterA.getMaxHealth() <= 0 ? 0f : (float)fighterA.data.health / fighterA.getMaxHealth();
            float hpB = fighterB.getMaxHealth() <= 0 ? 0f : (float)fighterB.data.health / fighterB.getMaxHealth();
            if (hpA <= DuelDefeatHpThreshold)
            {
                winner = fighterB;
                loser = fighterA;
                reason = $"{fighterB.getName()} 在约斗中获胜，{fighterA.getName()} 血量跌破 10%。";
                return true;
            }

            if (hpB <= DuelDefeatHpThreshold)
            {
                winner = fighterA;
                loser = fighterB;
                reason = $"{fighterA.getName()} 在约斗中获胜，{fighterB.getName()} 血量跌破 10%。";
                return true;
            }

            return false;
        }

        private static void ResolveTimeout(Actor fighterA, Actor fighterB, out Actor winner, out Actor loser, out string reason)
        {
            winner = null;
            loser = null;
            if (fighterA == null || !fighterA.isAlive())
            {
                winner = fighterB;
                loser = fighterA;
                reason = $"{_activeDuel.FighterBName} 在约斗超时判定中获胜。";
                return;
            }

            if (fighterB == null || !fighterB.isAlive())
            {
                winner = fighterA;
                loser = fighterB;
                reason = $"{_activeDuel.FighterAName} 在约斗超时判定中获胜。";
                return;
            }

            float hpA = fighterA.getMaxHealth() <= 0 ? 0f : (float)fighterA.data.health / fighterA.getMaxHealth();
            float hpB = fighterB.getMaxHealth() <= 0 ? 0f : (float)fighterB.data.health / fighterB.getMaxHealth();
            if (hpA > hpB)
            {
                winner = fighterA;
                loser = fighterB;
                reason = $"{fighterA.getName()} 在约斗超时后凭借更高血量获胜。";
                return;
            }

            if (hpB > hpA)
            {
                winner = fighterB;
                loser = fighterA;
                reason = $"{fighterB.getName()} 在约斗超时后凭借更高血量获胜。";
                return;
            }

            winner = fighterA.getID() <= fighterB.getID() ? fighterA : fighterB;
            loser = winner == fighterA ? fighterB : fighterA;
            reason = $"{winner.getName()} 在约斗超时后通过裁定获胜。";
        }

        private static void EnsureFighting(Actor fighter, Actor enemy)
        {
            if (fighter == null || enemy == null || !fighter.isAlive() || !enemy.isAlive())
            {
                return;
            }

            if (!fighter.hasStatus("tantrum"))
            {
                fighter.addStatusEffect("tantrum", DuelTantrumSeconds);
            }

            if (!fighter.has_attack_target || fighter.attack_target != enemy || !fighter.isTask("fighting"))
            {
                fighter.cancelAllBeh();
                fighter.startFightingWith(enemy);
            }
        }

        private static void EnsureStayInArena(Actor fighter)
        {
            if (fighter == null || !fighter.isAlive() || _activeDuel?.ArenaCenterTile == null)
            {
                return;
            }

            if (!TournamentArena.IsInArena(fighter.current_tile))
            {
                fighter.cancelAllBeh();
                fighter.spawnOn(_activeDuel.ArenaCenterTile);
            }
        }

        private static void FinishDuel(Actor winner, Actor loser, string reason)
        {
            if (_activeDuel == null)
            {
                return;
            }

            ActiveDuel duel = _activeDuel;

            Actor fighterA = World.world?.units?.get(duel.FighterAId);
            Actor fighterB = World.world?.units?.get(duel.FighterBId);
            RestoreFighter(fighterA, duel.OriginalTileA, healToFull: true);
            RestoreFighter(fighterB, duel.OriginalTileB, healToFull: true);
            if (duel.ArenaCenterTile != null)
            {
                TournamentArena.CleanupArena(duel.ArenaCenterTile);
            }

            string finalReason = reason;
            string winnerKingdomLabel = winner?.kingdom != null ? $"【{winner.kingdom.name}】" : string.Empty;
            if (winner != null && !string.IsNullOrWhiteSpace(winnerKingdomLabel))
            {
                finalReason = $"{winnerKingdomLabel} 胜！{reason}";
            }

            if (winner != null && loser != null && duel.BetAmount > 0)
            {
                Kingdom winnerKingdom = winner.kingdom;
                Kingdom loserKingdom = loser.kingdom;
                int payable = 0;
                if (winnerKingdom != null && winnerKingdom.isAlive() && loserKingdom != null && loserKingdom.isAlive())
                {
                    payable = Math.Min(duel.BetAmount, AutoPanKingdomService.GetTreasury(loserKingdom));
                    if (payable > 0)
                    {
                        AutoPanKingdomService.TryTransferTreasury(loserKingdom, winnerKingdom, payable, out _);
                    }
                }

                string payoutKingdomLabel = winnerKingdom == null ? "获胜国家" : AutoPanKingdomService.FormatKingdomLabel(winnerKingdom);
                finalReason = payable > 0
                    ? $"{reason} 本场赌金 {duel.BetAmount}，实际兑现 {payable} 金币给 {payoutKingdomLabel}。"
                    : $"{reason} 本场赌金 {duel.BetAmount}，败方国家国库不足，未能兑现赌金。";
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                XianniAutoPanApi.Broadcast(finalReason);
                List<string> atUserIds = AutoPanStateRepository.GetBindingsByKingdomId(duel.ChallengerKingdomId)
                    .Concat(AutoPanStateRepository.GetBindingsByKingdomId(duel.DefenderKingdomId))
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.UserId))
                    .Select(item => item.UserId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                AutoPanNotificationService.BroadcastToKnownGroups(finalReason, atUserIds);
            }

            _activeDuel = null;
        }

        private static void RestoreFighter(Actor fighter, Vector2 originalTile, bool healToFull)
        {
            if (fighter == null || !fighter.isAlive())
            {
                return;
            }

            fighter.cancelAllBeh();
            fighter.clearAttackTarget();
            fighter.finishStatusEffect("tantrum");
            if (healToFull)
            {
                fighter.data.health = fighter.getMaxHealth();
            }

            WorldTile tile = World.world?.GetTile((int)originalTile.x, (int)originalTile.y);
            if (tile != null)
            {
                fighter.spawnOn(tile);
            }
        }

        private static Vector2 ResolveOriginalTile(Actor fighter, Kingdom kingdom)
        {
            WorldTile tile = fighter?.current_tile ?? kingdom?.capital?.getTile();
            if (tile == null)
            {
                return Vector2.zero;
            }

            return new Vector2(tile.x, tile.y);
        }
    }
}
