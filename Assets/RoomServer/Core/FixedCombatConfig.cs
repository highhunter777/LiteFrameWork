using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 房间级不可变玩法配置快照（《商业级通用服务端框架总设计》§4 冲突裁定 / §5 P0-5： "房间创建时固定不可变玩法配置"）。
    /// </summary>
    public readonly struct FixedCombatConfig
    {
        /// <summary>房间级玩法数值实例（创建时固定）。</summary>
        public readonly CombatValues Values;

        /// <summary>房间级武器表实例（创建时固定；与数值同链传入）。</summary>
        public readonly WeaponTable Weapons;

        /// <summary>创建时刻的规范化配置摘要（SHA-256 低 32 位；proto uint32 口径）。</summary>
        public readonly uint Digest;

        private FixedCombatConfig(in CombatValues values, WeaponTable weapons, uint digest)
        {
            Values = values;
            Weapons = weapons;
            Digest = digest;
        }

        /// <summary>从装载实例 + 基摘要捕获（房间创建时调用一次；摘要同刻绑定——不读全局静态面）。
        /// <paramref name="gameplayDigestBase"/> = 装载链产出的判定面表字节基摘要
        /// （`ServerTableLoad.Digest`——生产链恒为真表集；测试直构形态传 0 = 无表身份形态）。</summary>
        public static FixedCombatConfig Capture(in CombatValues values, WeaponTable weapons, uint gameplayDigestBase)
        {
            return new FixedCombatConfig(values, weapons,
                CombatConfigDigest.WithTestRules(gameplayDigestBase, values.NoDeath, values.InfiniteAmmo));
        }
    }
}
