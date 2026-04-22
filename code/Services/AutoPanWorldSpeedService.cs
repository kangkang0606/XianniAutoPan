using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NeoModLoader.api;
using XianniAutoPan.Model;
using xn.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 管理自动盘自定义世界倍速与按年份自动切换的倍速计划。
    /// </summary>
    internal static class AutoPanWorldSpeedService
    {
        private const float MinWorldSpeed = 0.1f;
        private const float MaxWorldSpeed = 200f;
        private const string CustomWorldSpeedIdPrefix = "xianniautopan_speed_";
        private const string DefaultWorldSpeedIconPath = "ui/Icons/iconClockX1";
        private static readonly Regex ScheduleEntryRegex = new Regex(@"(?<year>\d+)\s*(?:年|y|Y)?\s*(?:[:=：,，、\s]+)?\s*(?<speed>\d+(?:\.\d+)?)\s*(?:倍速|倍|x|X)?", RegexOptions.Compiled);

        private static readonly WorldTimeScaleAsset CustomWorldSpeedAsset = new WorldTimeScaleAsset
        {
            id = CustomWorldSpeedIdPrefix + "1x",
            multiplier = 1f,
            ticks = 1,
            conway_ticks = 1,
            path_icon = DefaultWorldSpeedIconPath
        };

        private static int _lastAppliedScheduleYear = int.MinValue;
        private static float _lastAppliedScheduleSpeed = float.NaN;

        /// <summary>
        /// 运行时使用的单条倍速计划。
        /// </summary>
        private sealed class SpeedScheduleEntry
        {
            public int Year { get; set; }

            public float Speed { get; set; }
        }

        /// <summary>
        /// 解析并应用一次管理员即时倍速设置。
        /// </summary>
        public static bool TryApplyManualSpeed(string rawSpeed, out string speedText, out string message)
        {
            speedText = string.Empty;
            message = string.Empty;
            if (!TryParseSpeed(rawSpeed, out float parsed))
            {
                message = "无法解析倍速值。";
                return false;
            }

            float speed = ClampSpeed(parsed);
            if (Config.time_scale_asset == null)
            {
                message = "当前世界未加载，无法设置倍速。";
                return false;
            }

            ApplyCustomWorldSpeed(speed);
            speedText = FormatSpeedValue(speed);
            message = $"游戏倍速已设置为 {speedText}x。";
            return true;
        }

        /// <summary>
        /// 归一化可持久化的倍速计划文本。
        /// </summary>
        public static bool TryNormalizeSchedule(string rawSchedule, out string normalizedSchedule, out string message)
        {
            normalizedSchedule = string.Empty;
            message = string.Empty;
            string raw = (rawSchedule ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw) || raw == "清空" || raw == "关闭" || raw.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                message = "倍速计划已清空。";
                return true;
            }

            if (!TryParseSchedule(raw, out List<SpeedScheduleEntry> entries, out message))
            {
                return false;
            }

            normalizedSchedule = FormatSchedule(entries);
            message = $"倍速计划已更新：\n{normalizedSchedule}";
            return true;
        }

        /// <summary>
        /// 按当前年份应用倍速计划；没有到达任何配置年份时回到 1x 基准速度。
        /// </summary>
        public static bool ApplyScheduledSpeedForYear(int currentYear, bool force = false)
        {
            if (!AutoPanConfigHooks.WorldSpeedScheduleEnabled || currentYear <= 0 || !TryParseSchedule(AutoPanConfigHooks.WorldSpeedScheduleText, out List<SpeedScheduleEntry> entries, out _))
            {
                return false;
            }

            if (entries.Count == 0 || Config.time_scale_asset == null)
            {
                return false;
            }

            SpeedScheduleEntry selected = entries
                .Where(item => item.Year <= currentYear)
                .OrderByDescending(item => item.Year)
                .FirstOrDefault();
            int scheduleYear = selected?.Year ?? 0;
            float speed = selected?.Speed ?? 1f;
            if (!force && scheduleYear == _lastAppliedScheduleYear && Math.Abs(speed - _lastAppliedScheduleSpeed) < 0.0001f)
            {
                return false;
            }

            ApplyCustomWorldSpeed(speed);
            _lastAppliedScheduleYear = scheduleYear;
            _lastAppliedScheduleSpeed = speed;
            AutoPanLogService.Info($"第 {currentYear} 年应用倍速计划：{(scheduleYear <= 0 ? "默认" : $"第 {scheduleYear} 年起")} {FormatSpeedValue(speed)}x");
            return true;
        }

        /// <summary>
        /// 构建前端使用的倍速计划快照。
        /// </summary>
        public static AutoPanSpeedScheduleSnapshot BuildScheduleSnapshot()
        {
            AutoPanSpeedScheduleSnapshot snapshot = new AutoPanSpeedScheduleSnapshot
            {
                Enabled = AutoPanConfigHooks.WorldSpeedScheduleEnabled,
                RawText = AutoPanConfigHooks.WorldSpeedScheduleText,
                NormalizedText = AutoPanConfigHooks.WorldSpeedScheduleText,
                CurrentSpeedText = GetCurrentSpeedText()
            };

            if (TryParseSchedule(AutoPanConfigHooks.WorldSpeedScheduleText, out List<SpeedScheduleEntry> entries, out _))
            {
                foreach (SpeedScheduleEntry entry in entries)
                {
                    snapshot.Entries.Add(new AutoPanSpeedScheduleEntrySnapshot
                    {
                        Year = entry.Year,
                        Speed = entry.Speed,
                        SpeedText = FormatSpeedValue(entry.Speed)
                    });
                }
            }

            return snapshot;
        }

        /// <summary>
        /// 构建管理员查看倍速计划的文本。
        /// </summary>
        public static string BuildScheduleText()
        {
            if (string.IsNullOrWhiteSpace(AutoPanConfigHooks.WorldSpeedScheduleText))
            {
                return $"当前未配置倍速计划，开关：{(AutoPanConfigHooks.WorldSpeedScheduleEnabled ? "开启" : "关闭")}。示例：#设置倍速计划 1年5倍速，10年20倍速";
            }

            return $"当前倍速计划（开关：{(AutoPanConfigHooks.WorldSpeedScheduleEnabled ? "开启" : "关闭")}）：\n" + AutoPanConfigHooks.WorldSpeedScheduleText;
        }

        /// <summary>
        /// 获取当前游戏倍速的展示文本。
        /// </summary>
        public static string GetCurrentSpeedText()
        {
            return Config.time_scale_asset == null ? "未知" : FormatSpeedValue(Config.time_scale_asset.multiplier);
        }

        /// <summary>
        /// 判断指定速度资产是否为自动盘自定义速度资产。
        /// </summary>
        public static bool IsCustomWorldSpeedAsset(WorldTimeScaleAsset asset)
        {
            return ReferenceEquals(asset, CustomWorldSpeedAsset);
        }

        /// <summary>
        /// 为原生滚轮与热键切速提供自定义速度资产的邻接原生档位。
        /// </summary>
        public static WorldTimeScaleAsset GetAdjacentNativeWorldSpeedAsset(bool next, bool cycle)
        {
            WorldTimeScaleAsset referenceAsset = ResolveReferenceWorldSpeedAsset(CustomWorldSpeedAsset.multiplier);
            if (referenceAsset == null)
            {
                return CustomWorldSpeedAsset;
            }

            return next ? referenceAsset.getNext(cycle) : referenceAsset.getPrevious(cycle);
        }

        private static void ApplyCustomWorldSpeed(float speed)
        {
            WorldTimeScaleAsset referenceAsset = ResolveReferenceWorldSpeedAsset(speed);
            CustomWorldSpeedAsset.id = CustomWorldSpeedIdPrefix + FormatSpeedValue(speed) + "x";
            CustomWorldSpeedAsset.locale_key = null;
            CustomWorldSpeedAsset.multiplier = speed;
            CustomWorldSpeedAsset.ticks = 1;
            CustomWorldSpeedAsset.conway_ticks = 1;
            CustomWorldSpeedAsset.sonic = false;
            CustomWorldSpeedAsset.render_skip = false;
            // 借用最接近原生速度的图标，避免自定义速度下时钟按钮显示为空。
            CustomWorldSpeedAsset.path_icon = string.IsNullOrWhiteSpace(referenceAsset?.path_icon)
                ? DefaultWorldSpeedIconPath
                : referenceAsset.path_icon;
            Config.setWorldSpeed(CustomWorldSpeedAsset, pUpdateDebug: false);
        }

        private static bool TryParseSchedule(string rawSchedule, out List<SpeedScheduleEntry> entries, out string error)
        {
            entries = new List<SpeedScheduleEntry>();
            error = string.Empty;
            string raw = (rawSchedule ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            Dictionary<int, float> byYear = new Dictionary<int, float>();
            MatchCollection matches = ScheduleEntryRegex.Matches(raw);
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups["year"].Value, out int year) || !TryParseSpeed(match.Groups["speed"].Value, out float speed))
                {
                    continue;
                }

                if (year < 1 || year > 100000)
                {
                    error = "倍速计划年份必须在 1~100000 之间。";
                    return false;
                }
                if (speed < MinWorldSpeed || speed > MaxWorldSpeed)
                {
                    error = $"倍速计划中的倍速必须在 {FormatSpeedValue(MinWorldSpeed)}x~{FormatSpeedValue(MaxWorldSpeed)}x 之间。";
                    return false;
                }

                byYear[year] = speed;
            }

            if (byYear.Count == 0)
            {
                error = "倍速计划格式错误，请使用类似“1年5倍速，10年20倍速”的格式。";
                return false;
            }

            entries = byYear
                .OrderBy(item => item.Key)
                .Select(item => new SpeedScheduleEntry { Year = item.Key, Speed = item.Value })
                .ToList();
            return true;
        }

        private static string FormatSchedule(IEnumerable<SpeedScheduleEntry> entries)
        {
            return string.Join("\n", entries.Select(item => $"{item.Year}年 {FormatSpeedValue(item.Speed)}倍速"));
        }

        private static bool TryParseSpeed(string rawSpeed, out float speed)
        {
            return float.TryParse(rawSpeed, NumberStyles.Float, CultureInfo.InvariantCulture, out speed)
                || float.TryParse(rawSpeed, out speed);
        }

        private static float ClampSpeed(float speed)
        {
            return Math.Max(MinWorldSpeed, Math.Min(MaxWorldSpeed, speed));
        }

        private static WorldTimeScaleAsset ResolveReferenceWorldSpeedAsset(float speed)
        {
            if (AssetManager.time_scales == null)
            {
                return Config.time_scale_asset;
            }

            WorldTimeScaleAsset bestAsset = null;
            float bestDistance = float.MaxValue;
            string[] candidateIds = { "slow_mo", "x1", "x2", "x3", "x4", "x5", "x10", "x15", "x20", "x40" };
            foreach (string candidateId in candidateIds)
            {
                WorldTimeScaleAsset candidate = AssetManager.time_scales.get(candidateId);
                if (candidate == null)
                {
                    continue;
                }

                float distance = Math.Abs(candidate.multiplier - speed);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestAsset = candidate;
                }
            }

            return bestAsset ?? Config.time_scale_asset;
        }

        private static string FormatSpeedValue(float speed)
        {
            return speed.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }
}
