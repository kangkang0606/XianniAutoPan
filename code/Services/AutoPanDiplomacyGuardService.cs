using System;
using System.Linq;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 隔离原版外交法则对自动盘绑定国家的影响，并为自动盘指令提供显式放行作用域。
    /// </summary>
    internal static class AutoPanDiplomacyGuardService
    {
        private static int _autoPanDiplomacyDepth;

        /// <summary>
        /// 创建一个自动盘外交操作放行作用域。
        /// </summary>
        public static IDisposable AllowAutoPanDiplomacyChange()
        {
            _autoPanDiplomacyDepth++;
            return new AutoPanDiplomacyScope();
        }

        /// <summary>
        /// 判断原版战争创建是否应被阻止。
        /// </summary>
        public static bool ShouldBlockNativeWar(Kingdom attacker, Kingdom defender, WarTypeAsset warType)
        {
            if (_autoPanDiplomacyDepth > 0)
            {
                return false;
            }

            if (AutoPanStateRepository.IsPlayerOwnedKingdom(attacker) || AutoPanStateRepository.IsPlayerOwnedKingdom(defender))
            {
                return true;
            }

            return warType != null && warType.total_war && HasAnyPlayerOwnedKingdom();
        }

        /// <summary>
        /// 判断原版联盟创建是否应被阻止。
        /// </summary>
        public static bool ShouldBlockNativeAlliance(Kingdom first, Kingdom second)
        {
            return _autoPanDiplomacyDepth <= 0
                && (AutoPanStateRepository.IsPlayerOwnedKingdom(first) || AutoPanStateRepository.IsPlayerOwnedKingdom(second));
        }

        /// <summary>
        /// 判断原版联盟成员变更是否应被阻止。
        /// </summary>
        public static bool ShouldBlockNativeAllianceMembershipChange(Alliance alliance, Kingdom kingdom)
        {
            if (_autoPanDiplomacyDepth > 0)
            {
                return false;
            }

            if (kingdom != null && AutoPanStateRepository.IsPlayerOwnedKingdom(kingdom))
            {
                return true;
            }

            if (alliance?.kingdoms_hashset == null)
            {
                return false;
            }

            foreach (Kingdom member in alliance.kingdoms_hashset)
            {
                if (member != null && AutoPanStateRepository.IsPlayerOwnedKingdom(member))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyPlayerOwnedKingdom()
        {
            if (World.world?.kingdoms == null)
            {
                return false;
            }

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (AutoPanStateRepository.IsPlayerOwnedKingdom(kingdom))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class AutoPanDiplomacyScope : IDisposable
        {
            /// <summary>
            /// 退出自动盘外交操作放行作用域。
            /// </summary>
            public void Dispose()
            {
                _autoPanDiplomacyDepth = Math.Max(0, _autoPanDiplomacyDepth - 1);
            }
        }
    }
}
