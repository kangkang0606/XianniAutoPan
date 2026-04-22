using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using XianniAutoPan.Frontend;
using XianniAutoPan.Model;
using xn.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 处理 QQ 群“当前局势”截图指令与冷却。
    /// </summary>
    internal static class AutoPanScreenshotService
    {
        private const int ScreenshotReadyAttempts = 40;
        private const int ScreenshotReadyDelayMs = 250;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, DateTime> LastCaptureByGroup = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static string _screenshotFolder = string.Empty;

        /// <summary>
        /// 初始化截图文件输出目录。
        /// </summary>
        public static void Initialize(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder))
            {
                return;
            }

            _screenshotFolder = Path.Combine(modFolder, "qq_screenshots");
        }

        /// <summary>
        /// 截取当前游戏全屏画面，并通过 QQ 群发送图片。
        /// </summary>
        public static bool TrySendCurrentSituation(FrontendInboundMessage message, bool bypassCooldown, out string replyText)
        {
            replyText = string.Empty;
            if (message == null || message.SourceType != AutoPanInputSourceType.QqGroup)
            {
                replyText = "当前局势截图只能在 QQ 群中使用。";
                return false;
            }

            string groupId = AutoPanQqBridgeService.NormalizeQqDigits(message.ContextId);
            if (string.IsNullOrWhiteSpace(groupId))
            {
                groupId = AutoPanQqBridgeService.NormalizeQqDigits(message.ReplyTargetId);
            }

            if (string.IsNullOrWhiteSpace(groupId))
            {
                replyText = "当前局势截图失败：无法识别 QQ 群号。";
                return false;
            }

            DateTime now = DateTime.UtcNow;
            if (!bypassCooldown)
            {
                lock (Sync)
                {
                    if (LastCaptureByGroup.TryGetValue(groupId, out DateTime lastCaptureUtc))
                    {
                        double remainingSeconds = AutoPanConfigHooks.CurrentSituationCooldownSeconds - (now - lastCaptureUtc).TotalSeconds;
                        if (remainingSeconds > 0)
                        {
                            replyText = $"当前局势截图冷却中，还需 {Math.Ceiling(remainingSeconds)} 秒。";
                            return false;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(_screenshotFolder))
            {
                replyText = "当前局势截图失败：截图目录尚未初始化。";
                return false;
            }

            try
            {
                Directory.CreateDirectory(_screenshotFolder);
                string filePath = Path.Combine(_screenshotFolder, $"current_situation_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
                ScreenCapture.CaptureScreenshot(filePath);
                if (!bypassCooldown)
                {
                    lock (Sync)
                    {
                        LastCaptureByGroup[groupId] = now;
                    }
                }

                FrontendInboundMessage replySource = CloneReplySource(message);
                string titleText = $"当前{Date.getCurrentYear()}年倍速{AutoPanWorldSpeedService.GetCurrentSpeedText()}倍：";
                _ = Task.Run(() => SendScreenshotWhenReadyAsync(replySource, filePath, titleText));
                replyText = "正在截取当前局势，稍后发送图片。";
                return true;
            }
            catch (Exception ex)
            {
                replyText = $"当前局势截图失败：{ex.Message}";
                AutoPanLogService.Error($"当前局势截图失败：{ex}");
                return false;
            }
        }

        private static FrontendInboundMessage CloneReplySource(FrontendInboundMessage message)
        {
            return new FrontendInboundMessage
            {
                SessionId = message.SessionId,
                UserId = message.UserId,
                PlayerName = message.PlayerName,
                SourceType = message.SourceType,
                ReplyTargetId = message.ReplyTargetId,
                ContextId = message.ContextId,
                BotSelfId = message.BotSelfId
            };
        }

        private static async Task SendScreenshotWhenReadyAsync(FrontendInboundMessage sourceMessage, string filePath, string titleText)
        {
            bool ready = false;
            for (int attempt = 0; attempt < ScreenshotReadyAttempts; attempt++)
            {
                if (IsFileReady(filePath))
                {
                    ready = true;
                    break;
                }

                await Task.Delay(ScreenshotReadyDelayMs).ConfigureAwait(false);
            }

            string message = ready
                ? $"{titleText}\n[CQ:image,file={new Uri(Path.GetFullPath(filePath)).AbsoluteUri}]"
                : "当前局势截图失败：截图文件未生成。";
            AutoPanLocalWebServer.Instance.SendQqGroupRawMessage(sourceMessage, message);
        }

        private static bool IsFileReady(string filePath)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                return fileInfo.Exists && fileInfo.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
