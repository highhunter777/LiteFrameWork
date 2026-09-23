using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 房间级不可变玩法配置快照（《商业级通用服务端框架总设计》§4 冲突裁定 / §5 P0-5：
    /// "房间创建时固定不可变玩法配置，替代进程全局可变 CombatConfig"）。
    ///
    /// R1 口径（已定决策）：**宿主级快照**——RoomRuntime 构造时一次性复制 <see cref="CombatConfig"/>
    /// 的 8 个字段并绑定规范化摘要；此后本房间的 Create 侧消费（出生 HP 等）一律读快照，
    /// 运行中被改的全局值不再影响已创建房间。Sim 系统内部仍读全局静态字段（其参数化迁移
    /// 归 G1 ConfigSnapshot/热更批）——单房间形态下两者一致，多房间差异在 R2 前不会出现。
    ///
    /// <see cref="Digest"/> = <see cref="CombatConfigDigest.Compute"/> 在**创建时刻**的取值：
    /// 随 StartGame 下发，客户端据此比对"本局数值身份"；创建后进程再改配置不回改本房间摘要。
    /// </summary>
    public readonly struct FixedCombatConfig
    {
        public readonly float MoveSpeed;
        public readonly float Gravity;
        public readonly float HitscanRange;
        public readonly float HitscanRadius;
        public readonly float HitscanHeight;
        public readonly int BaseDamage;
        public readonly int DamageSpread;
        public readonly int EntityHp;

        /// <summary>创建时刻的规范化配置摘要（SHA-256 低 32 位；proto uint32 口径）。</summary>
        public readonly uint Digest;

        private FixedCombatConfig(float moveSpeed, float gravity, float hitscanRange, float hitscanRadius,
            float hitscanHeight, int baseDamage, int damageSpread, int entityHp, uint digest)
        {
            MoveSpeed = moveSpeed;
            Gravity = gravity;
            HitscanRange = hitscanRange;
            HitscanRadius = hitscanRadius;
            HitscanHeight = hitscanHeight;
            BaseDamage = baseDamage;
            DamageSpread = damageSpread;
            EntityHp = entityHp;
            Digest = digest;
        }

        /// <summary>快照当前全局装载值（房间创建时调用一次；摘要同刻绑定）。</summary>
        public static FixedCombatConfig Capture()
        {
            return new FixedCombatConfig(
                CombatConfig.MoveSpeed, CombatConfig.Gravity, CombatConfig.HitscanRange,
                CombatConfig.HitscanRadius, CombatConfig.HitscanHeight,
                CombatConfig.BaseDamage, CombatConfig.DamageSpread, CombatConfig.EntityHp,
                CombatConfigDigest.Compute());
        }
    }
}
