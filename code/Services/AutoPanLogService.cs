using System;
using System.Collections.Generic;
using UnityEngine;
using XianniAutoPan.Model;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 自动盘日志服务。
    /// </summary>
    internal static class AutoPanLogService
    {
        private static readonly object Sync = new object();
        private static readonly List<AutoPanLogEntry> Entries = new List<AutoPanLogEntry>();

        /// <summary>
        /// 记录普通日志。
        /// </summary>
        public static void Info(string message)
        {
            Append(message, isError: false);
        }

        /// <summary>
        /// 记录错误日志。
        /// </summary>
        public static void Error(string message)
        {
            Append(message, isError: true);
        }

        /// <summary>
        /// 获取最近日志快照。
        /// </summary>
        public static List<AutoPanLogEntry> GetRecentEntries()
        {
            lock (Sync)
            {
                return new List<AutoPanLogEntry>(Entries);
            }
        }

        private static void Append(string message, bool isError)
        {
            string line = $"{AutoPanConstants.LogPrefix} {message}";
            if (isError)
            {
                Debug.LogError(line);
            }

            lock (Sync)
            {
                Entries.Add(new AutoPanLogEntry
                {
                    TimeText = DateTime.Now.ToString("HH:mm:ss"),
                    Message = line
                });
                if (Entries.Count > AutoPanConstants.LogCapacity)
                {
                    Entries.RemoveAt(0);
                }
            }
        }
    }
}
