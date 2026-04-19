using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using XianniAutoPan.Model;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 管理跨局累计积分榜。
    /// </summary>
    internal static class AutoPanScoreService
    {
        private sealed class PersistedScoreFile
        {
            /// <summary>
            /// 玩家或 AI 积分记录。
            /// </summary>
            public List<AutoPanScoreRecord> Scores { get; set; } = new List<AutoPanScoreRecord>();
        }

        private sealed class AutoPanScoreRecord
        {
            /// <summary>
            /// 玩家唯一标识。
            /// </summary>
            public string UserId { get; set; }

            /// <summary>
            /// 最近显示名。
            /// </summary>
            public string PlayerName { get; set; }

            /// <summary>
            /// 累计积分；字段名保留 Wins 以兼容旧积分文件和前端接口。
            /// </summary>
            public int Wins { get; set; }

            /// <summary>
            /// 最近积分更新时间，UTC ISO 字符串。
            /// </summary>
            public string LastWinUtc { get; set; }
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, AutoPanScoreRecord> ScoresByUser = new Dictionary<string, AutoPanScoreRecord>(StringComparer.Ordinal);
        private static string _scorePath = string.Empty;

        /// <summary>
        /// 初始化积分榜文件路径并载入已有积分。
        /// </summary>
        public static void Initialize(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder))
            {
                return;
            }

            _scorePath = Path.Combine(modFolder, "autopan_scoreboard.json");
            Load();
        }

        /// <summary>
        /// 为玩家或 AI 增加结盘积分。
        /// </summary>
        public static int AddPoints(string userId, string playerName, int points)
        {
            string normalizedUserId = (userId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedUserId) || points <= 0)
            {
                return 0;
            }

            int totalPoints;
            lock (Sync)
            {
                if (!ScoresByUser.TryGetValue(normalizedUserId, out AutoPanScoreRecord record) || record == null)
                {
                    record = new AutoPanScoreRecord
                    {
                        UserId = normalizedUserId,
                        Wins = 0
                    };
                    ScoresByUser[normalizedUserId] = record;
                }

                record.PlayerName = string.IsNullOrWhiteSpace(playerName) ? normalizedUserId : playerName.Trim();
                record.Wins += points;
                record.LastWinUtc = DateTime.UtcNow.ToString("o");
                totalPoints = record.Wins;
            }

            Save();
            return totalPoints;
        }

        /// <summary>
        /// 旧制胜场入口，按 1 分处理以兼容已有调用。
        /// </summary>
        public static int AddWin(string userId, string playerName)
        {
            return AddPoints(userId, playerName, 1);
        }

        /// <summary>
        /// 构建积分排名文本。
        /// </summary>
        public static string BuildRankingText()
        {
            List<AutoPanScoreRecord> rankings;
            lock (Sync)
            {
                rankings = ScoresByUser.Values
                    .Where(item => item != null && item.Wins > 0)
                    .OrderByDescending(item => item.Wins)
                    .ThenByDescending(item => item.LastWinUtc, StringComparer.Ordinal)
                    .ThenBy(item => item.PlayerName ?? item.UserId, StringComparer.Ordinal)
                    .Take(20)
                    .ToList();
            }

            if (rankings.Count == 0)
            {
                return "当前还没有玩家或 AI 获得结盘积分。";
            }

            List<string> lines = new List<string> { "玩家积分排名：" };
            for (int index = 0; index < rankings.Count; index++)
            {
                AutoPanScoreRecord item = rankings[index];
                lines.Add($"{index + 1}. {item.PlayerName}({item.UserId})：{item.Wins} 分");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 构建前端积分榜记录。
        /// </summary>
        public static List<AutoPanScoreDashboardRecord> BuildDashboardRecords()
        {
            lock (Sync)
            {
                return ScoresByUser.Values
                    .Where(item => item != null)
                    .OrderByDescending(item => item.Wins)
                    .ThenByDescending(item => item.LastWinUtc, StringComparer.Ordinal)
                    .ThenBy(item => item.PlayerName ?? item.UserId, StringComparer.Ordinal)
                    .Select(item => new AutoPanScoreDashboardRecord
                    {
                        UserId = item.UserId,
                        PlayerName = string.IsNullOrWhiteSpace(item.PlayerName) ? item.UserId : item.PlayerName,
                        Wins = Math.Max(0, item.Wins),
                        LastWinUtc = item.LastWinUtc
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// 手动设置玩家或 AI 积分榜分数与显示名。
        /// </summary>
        public static bool TrySetScore(string userId, string playerName, int wins, out string message)
        {
            message = string.Empty;
            string normalizedUserId = (userId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedUserId))
            {
                message = "保存积分失败：userId 不能为空。";
                return false;
            }

            if (wins < 0)
            {
                message = "保存积分失败：积分不能小于 0。";
                return false;
            }

            lock (Sync)
            {
                if (!ScoresByUser.TryGetValue(normalizedUserId, out AutoPanScoreRecord record) || record == null)
                {
                    record = new AutoPanScoreRecord
                    {
                        UserId = normalizedUserId
                    };
                    ScoresByUser[normalizedUserId] = record;
                }

                record.PlayerName = string.IsNullOrWhiteSpace(playerName) ? normalizedUserId : playerName.Trim();
                record.Wins = wins;
                record.LastWinUtc = DateTime.UtcNow.ToString("o");
            }

            Save();
            message = $"已保存 {normalizedUserId} 的积分：{wins} 分。";
            return true;
        }

        /// <summary>
        /// 删除玩家或 AI 积分榜记录。
        /// </summary>
        public static bool TryDeleteScore(string userId, out string message)
        {
            message = string.Empty;
            string normalizedUserId = (userId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedUserId))
            {
                message = "删除积分失败：userId 不能为空。";
                return false;
            }

            bool removed;
            lock (Sync)
            {
                removed = ScoresByUser.Remove(normalizedUserId);
            }

            if (!removed)
            {
                message = $"删除积分失败：未找到 {normalizedUserId}。";
                return false;
            }

            Save();
            message = $"已删除 {normalizedUserId} 的积分记录。";
            return true;
        }

        private static void Load()
        {
            lock (Sync)
            {
                ScoresByUser.Clear();
                if (string.IsNullOrWhiteSpace(_scorePath) || !File.Exists(_scorePath))
                {
                    return;
                }

                try
                {
                    PersistedScoreFile file = JsonConvert.DeserializeObject<PersistedScoreFile>(File.ReadAllText(_scorePath));
                    foreach (AutoPanScoreRecord record in file?.Scores ?? new List<AutoPanScoreRecord>())
                    {
                        if (record == null || string.IsNullOrWhiteSpace(record.UserId))
                        {
                            continue;
                        }

                        ScoresByUser[record.UserId.Trim()] = record;
                    }
                }
                catch (Exception ex)
                {
                    AutoPanLogService.Error($"读取积分榜失败：{ex.Message}");
                }
            }
        }

        private static void Save()
        {
            if (string.IsNullOrWhiteSpace(_scorePath))
            {
                return;
            }

            try
            {
                string folder = Path.GetDirectoryName(_scorePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                PersistedScoreFile file;
                lock (Sync)
                {
                    file = new PersistedScoreFile
                    {
                        Scores = ScoresByUser.Values.OrderBy(item => item.UserId, StringComparer.Ordinal).ToList()
                    };
                }

                File.WriteAllText(_scorePath, JsonConvert.SerializeObject(file, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"保存积分榜失败：{ex.Message}");
            }
        }
    }
}
