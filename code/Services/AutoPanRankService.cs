using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using XianniAutoPan.Model;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 管理玩家积分段位配置与段位奖励。
    /// </summary>
    internal static class AutoPanRankService
    {
        private sealed class PersistedRankConfig
        {
            /// <summary>
            /// 是否启用段位系统。
            /// </summary>
            public bool Enabled { get; set; }

            /// <summary>
            /// 已配置的段位列表。
            /// </summary>
            public List<AutoPanRankTierSnapshot> Ranks { get; set; } = new List<AutoPanRankTierSnapshot>();
        }

        /// <summary>
        /// 按积分匹配后的段位收益。
        /// </summary>
        public sealed class RankBenefits
        {
            /// <summary>
            /// 当前是否启用段位系统。
            /// </summary>
            public bool Enabled { get; set; }

            /// <summary>
            /// 匹配到的段位名称。
            /// </summary>
            public string RankName { get; set; }

            /// <summary>
            /// 新建国家初始人口。
            /// </summary>
            public int InitialPopulation { get; set; }

            /// <summary>
            /// 新建国家初始国库。
            /// </summary>
            public int InitialTreasury { get; set; }

            /// <summary>
            /// 每年额外国库收入。
            /// </summary>
            public int YearlyIncomeBonus { get; set; }

            /// <summary>
            /// 加入成功回包前缀。
            /// </summary>
            public string EntryPrefix { get; set; }
        }

        private const int MinInitialPopulation = 1;
        private const int MaxInitialPopulation = 100;
        private const int MaxEntryPrefixLength = 80;
        private const int MaxConfigValue = 1_000_000_000;
        private static readonly object Sync = new object();
        private static bool _enabled;
        private static string _rankPath = string.Empty;
        private static List<AutoPanRankTierSnapshot> _ranks = new List<AutoPanRankTierSnapshot>();

        /// <summary>
        /// 初始化段位配置文件路径并加载配置。
        /// </summary>
        public static void Initialize(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder))
            {
                return;
            }

            _rankPath = Path.Combine(modFolder, "autopan_rank_config.json");
            Load();
        }

        /// <summary>
        /// 构建当前段位配置快照。
        /// </summary>
        public static AutoPanRankConfigSnapshot BuildSnapshot()
        {
            lock (Sync)
            {
                return new AutoPanRankConfigSnapshot
                {
                    Enabled = _enabled,
                    Ranks = NormalizeRanks(_ranks).Select(CloneRank).ToList()
                };
            }
        }

        /// <summary>
        /// 通过前端保存段位配置。
        /// </summary>
        public static bool TrySetConfig(string rawEnabled, string ranksJson, out string message)
        {
            message = string.Empty;
            bool enabled = ParseBool(rawEnabled, false);
            List<AutoPanRankTierSnapshot> parsedRanks;
            try
            {
                parsedRanks = string.IsNullOrWhiteSpace(ranksJson)
                    ? new List<AutoPanRankTierSnapshot>()
                    : JsonConvert.DeserializeObject<List<AutoPanRankTierSnapshot>>(ranksJson) ?? new List<AutoPanRankTierSnapshot>();
            }
            catch (Exception ex)
            {
                message = $"段位配置 JSON 解析失败：{ex.Message}";
                return false;
            }

            List<AutoPanRankTierSnapshot> normalized = NormalizeRanks(parsedRanks);
            lock (Sync)
            {
                _enabled = enabled;
                _ranks = normalized;
            }

            Save();
            message = $"段位系统已{(enabled ? "启用" : "关闭")}，当前 {normalized.Count} 个段位。";
            return true;
        }

        /// <summary>
        /// 获取玩家积分对应的段位名称。
        /// </summary>
        public static string GetRankNameForPoints(int points)
        {
            RankBenefits benefits = GetBenefitsForPoints(points);
            return benefits.Enabled ? benefits.RankName : "未启用";
        }

        /// <summary>
        /// 获取玩家积分对应的加入与年收入收益。
        /// </summary>
        public static RankBenefits GetBenefitsForPoints(int points)
        {
            lock (Sync)
            {
                if (!_enabled)
                {
                    AutoPanRankTierSnapshot disabledTier = FindTier(points, NormalizeRanks(_ranks));
                    return CreateDisabledBenefits(disabledTier);
                }

                AutoPanRankTierSnapshot activeTier = FindTier(points, NormalizeRanks(_ranks));
                return new RankBenefits
                {
                    Enabled = true,
                    RankName = activeTier.Name,
                    InitialPopulation = activeTier.InitialPopulation,
                    InitialTreasury = activeTier.InitialTreasury,
                    YearlyIncomeBonus = activeTier.YearlyIncomeBonus,
                    EntryPrefix = activeTier.EntryPrefix ?? string.Empty
                };
            }
        }

        private static RankBenefits CreateDefaultBenefits(bool enabled)
        {
            AutoPanRankTierSnapshot tier = CreateDefaultRank();
            return new RankBenefits
            {
                Enabled = enabled,
                RankName = tier.Name,
                InitialPopulation = tier.InitialPopulation,
                InitialTreasury = tier.InitialTreasury,
                YearlyIncomeBonus = tier.YearlyIncomeBonus,
                EntryPrefix = tier.EntryPrefix ?? string.Empty
            };
        }

        private static RankBenefits CreateDisabledBenefits(AutoPanRankTierSnapshot tier)
        {
            AutoPanRankTierSnapshot displayTier = tier ?? CreateDefaultRank();
            return new RankBenefits
            {
                Enabled = false,
                RankName = displayTier.Name,
                InitialPopulation = AutoPanConstants.JoinUnitCount,
                InitialTreasury = AutoPanConfigHooks.InitialTreasury,
                YearlyIncomeBonus = 0,
                EntryPrefix = displayTier.EntryPrefix ?? string.Empty
            };
        }

        private static AutoPanRankTierSnapshot FindTier(int points, List<AutoPanRankTierSnapshot> ranks)
        {
            int safePoints = Math.Max(0, points);
            return ranks
                .Where(item => item.MinPoints <= safePoints)
                .OrderByDescending(item => item.MinPoints)
                .ThenBy(item => item.Name ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault() ?? CreateDefaultRank();
        }

        private static List<AutoPanRankTierSnapshot> NormalizeRanks(IEnumerable<AutoPanRankTierSnapshot> ranks)
        {
            List<AutoPanRankTierSnapshot> normalized = new List<AutoPanRankTierSnapshot>();
            foreach (AutoPanRankTierSnapshot rank in ranks ?? Enumerable.Empty<AutoPanRankTierSnapshot>())
            {
                if (rank == null)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(rank.Name) ? "新人" : rank.Name.Trim();
                normalized.Add(new AutoPanRankTierSnapshot
                {
                    Name = name.Length > 24 ? name.Substring(0, 24) : name,
                    MinPoints = Clamp(rank.MinPoints, 0, MaxConfigValue),
                    InitialPopulation = Clamp(rank.InitialPopulation, MinInitialPopulation, MaxInitialPopulation),
                    InitialTreasury = Clamp(rank.InitialTreasury, 0, MaxConfigValue),
                    YearlyIncomeBonus = Clamp(rank.YearlyIncomeBonus, 0, MaxConfigValue),
                    EntryPrefix = NormalizeEntryPrefix(rank.EntryPrefix)
                });
            }

            if (!normalized.Any(item => item.MinPoints == 0))
            {
                normalized.Add(CreateDefaultRank());
            }

            return normalized
                .GroupBy(item => item.MinPoints)
                .Select(group => group.Last())
                .OrderBy(item => item.MinPoints)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToList();
        }

        private static AutoPanRankTierSnapshot CreateDefaultRank()
        {
            return new AutoPanRankTierSnapshot
            {
                Name = "新人",
                MinPoints = 0,
                InitialPopulation = AutoPanConstants.JoinUnitCount,
                InitialTreasury = AutoPanConfigHooks.InitialTreasury,
                YearlyIncomeBonus = 0,
                EntryPrefix = string.Empty
            };
        }

        private static AutoPanRankTierSnapshot CloneRank(AutoPanRankTierSnapshot rank)
        {
            return new AutoPanRankTierSnapshot
            {
                Name = rank?.Name ?? "新人",
                MinPoints = Math.Max(0, rank?.MinPoints ?? 0),
                InitialPopulation = Clamp(rank?.InitialPopulation ?? AutoPanConstants.JoinUnitCount, MinInitialPopulation, MaxInitialPopulation),
                InitialTreasury = Clamp(rank?.InitialTreasury ?? AutoPanConfigHooks.InitialTreasury, 0, MaxConfigValue),
                YearlyIncomeBonus = Clamp(rank?.YearlyIncomeBonus ?? 0, 0, MaxConfigValue),
                EntryPrefix = NormalizeEntryPrefix(rank?.EntryPrefix)
            };
        }

        private static string NormalizeEntryPrefix(string value)
        {
            string prefix = (value ?? string.Empty).Trim();
            if (prefix.Length <= MaxEntryPrefixLength)
            {
                return prefix;
            }

            return prefix.Substring(0, MaxEntryPrefixLength);
        }

        private static void Load()
        {
            lock (Sync)
            {
                _enabled = false;
                _ranks = new List<AutoPanRankTierSnapshot>();
                if (string.IsNullOrWhiteSpace(_rankPath) || !File.Exists(_rankPath))
                {
                    return;
                }

                try
                {
                    PersistedRankConfig persisted = JsonConvert.DeserializeObject<PersistedRankConfig>(File.ReadAllText(_rankPath));
                    _enabled = persisted?.Enabled ?? false;
                    _ranks = NormalizeRanks(persisted?.Ranks);
                }
                catch (Exception ex)
                {
                    _enabled = false;
                    _ranks = new List<AutoPanRankTierSnapshot>();
                    AutoPanLogService.Error($"读取段位配置失败：{ex.Message}");
                }
            }
        }

        private static void Save()
        {
            if (string.IsNullOrWhiteSpace(_rankPath))
            {
                return;
            }

            try
            {
                string folder = Path.GetDirectoryName(_rankPath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                PersistedRankConfig persisted;
                lock (Sync)
                {
                    persisted = new PersistedRankConfig
                    {
                        Enabled = _enabled,
                        Ranks = NormalizeRanks(_ranks).Select(CloneRank).ToList()
                    };
                }

                File.WriteAllText(_rankPath, JsonConvert.SerializeObject(persisted, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AutoPanLogService.Error($"保存段位配置失败：{ex.Message}");
            }
        }

        private static int Clamp(int value, int minValue, int maxValue)
        {
            if (value < minValue)
            {
                return minValue;
            }

            return value > maxValue ? maxValue : value;
        }

        private static bool ParseBool(string value, bool defaultValue)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return defaultValue;
            }

            return normalized == "1"
                || normalized == "true"
                || normalized == "on"
                || normalized == "yes"
                || normalized == "开"
                || normalized == "开启";
        }
    }
}
