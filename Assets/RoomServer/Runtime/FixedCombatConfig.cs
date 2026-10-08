using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 房间级不可变玩法配置快照（《商业级通用服务端框架总设计》§4 冲突裁定 / §5 P0-5：
    /// "房间创建时固定不可变玩法配置"）。
    ///
    /// **技术债 #1 根治后**：快照从**装载实例**捕获（`Capture(in CombatValues, WeaponTable)`）——
    /// 不再复制全局静态面；房间创建时把装载产物显式传入
    /// （`HostAssembly`→`ServerHost`→`RoomInstance`→`RoomRuntime`）。
    /// 此后本房间的一切消费（Create 侧出生 HP、权威 Sim Step、回溯单系统、准入 AimPoint 距离闸、
    /// 武器懒装备/节拍/换弹）一律读本快照值——进程内任何后续变化都不可能影响已创建房间。
    ///
    /// <see cref="Digest"/> = 创建时刻按数值实例计算的规范化摘要
    /// （<see cref="CombatConfigDigest.Compute(in CombatValues)"/>）：随 StartGame 下发，
    /// 客户端据此比对"本局数值身份"（武器表行同受 buildHash 表字节保护，不进本摘要）。
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

        /// <summary>从装载实例捕获（房间创建时调用一次；摘要同刻绑定——不读全局静态面）。</summary>
        public static FixedCombatConfig Capture(in CombatValues values, WeaponTable weapons)
        {
            return new FixedCombatConfig(values, weapons, CombatConfigDigest.Compute(values));
        }
    }
}
