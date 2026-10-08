namespace LiteSim
{
    /// <summary>
    /// 玩法数值实例（**技术债 #1 根治面**）：装载面 6 字段从"全局可变静态 + LoadFrom 回填"
    /// 改为**按值传递的实例**——装载链（服务端 `CombatNumbers`／客户端 `ConfigService`）解析表产出本实例，
    /// 经参数显式传入机制消费（`SimStep` → 系统；房间快照 `FixedCombatConfig`）。
    /// 不再有"装载即回填全局"的顺序问题，测试不再需要串行集 + finally 还原。
    ///
    /// - **按值传递**；持有点构造后不得再改（约定——实例可自由复制，无共享可变面）。
    /// - <see cref="Default"/> = 表装面默认值（灰盒实测值），必须与表值一致（L1 守卫卡漂移）。
    /// - 联机身份（摘要）见 <see cref="CombatConfigDigest.Compute(in CombatValues)"/>——摘要按实例计算，
    ///   字段序/格式与旧口径逐字节一致（同值同摘要，两端拒进房判据不变）。
    /// - 编译期常量（窗长/枪口烘焙/判定几何等）**不在**本实例——它们留在 <see cref="CombatConfig"/>。
    /// </summary>
    public struct CombatValues
    {
        public float MoveSpeed;
        /// <summary>tbmovementconfig.gravity 投影；本局固定值，不在 combatnum 重复存储。</summary>
        public float Gravity;
        public float HitscanRange;
        public int BaseDamage;
        public int DamageSpread;
        /// <summary>tbentityconfig.initial_hp 投影；本局默认角色的出生生命。</summary>
        public int EntityHp;

        /// <summary>瞄准态移动上限 = MoveSpeed × <see cref="CombatConfig.AimMoveSpeedFactor"/>
        /// （乘 2 的幂——位级精确）。参与 <see cref="CombatConfigDigest"/>（联机身份）。</summary>
        public float AimMoveSpeed => MoveSpeed * CombatConfig.AimMoveSpeedFactor;

        /// <summary>构造固定对局数值；重力与出生生命由各自所属表装配。</summary>
        public CombatValues(float moveSpeed, float gravity, float hitscanRange,
            int baseDamage, int damageSpread, int entityHp)
        {
            MoveSpeed = moveSpeed;
            Gravity = gravity;
            HitscanRange = hitscanRange;
            BaseDamage = baseDamage;
            DamageSpread = damageSpread;
            EntityHp = entityHp;
        }

        /// <summary>表装面默认值（必须与表值一致——漂移由装载闸门与 L1 守卫卡）。
        /// **属性而非静态字段**：每次取副本，防"共享静态可变存储"复辟（债 #1 的形态）。</summary>
        public static CombatValues Default => new CombatValues(5f, MovementValues.Default.Gravity,
            100f, 25, 1, EntityValues.Default.InitialHp);
    }
}
