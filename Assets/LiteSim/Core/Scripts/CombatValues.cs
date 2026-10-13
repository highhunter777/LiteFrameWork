namespace LiteSim
{
    /// <summary>
    /// 玩法数值实例（**技术债 #1 根治面**）：装载面 8 字段从"全局可变静态 + LoadFrom 回填"
    /// 改为**按值传递的实例**——装载链（服务端 `ServerTableLoader`／客户端 `ConfigService`）解析表产出本实例，
    /// 经参数显式传入机制消费（`SimStep` → 系统；房间快照 `FixedCombatConfig`）。
    /// 不再有"装载即回填全局"的顺序问题，测试不再需要串行集 + finally 还原。
    ///
    /// - **按值传递**；持有点构造后不得再改（约定——实例可自由复制，无共享可变面）。
    /// - <see cref="Default"/> = 表装面默认值（灰盒实测值），必须与表值一致（L1 守卫卡漂移）。
    /// - 联机身份（摘要）见 <see cref="CombatConfigDigest"/>——**批 E 起按判定面表字节计算**（本实例的字段
    ///   是那些表的投影：值相同 ⇔ 表相同；值侧唯一独立输入是本局的 <see cref="NoDeath"/>/<see cref="InfiniteAmmo"/>
    ///   规则位，经 `WithTestRules` 进终值）。
    /// - 编译期常量（枪口烘焙/判定几何等）**不在**本实例——它们留在 <see cref="CombatConfig"/>。
    ///   （射击窗长为表化值——原编译期常量已迁入本实例。）
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

        /// <summary>进房初值的免死规则（测试房；运行态经 <see cref="MatchTestRules"/> 注入——见该类注释）。
        /// 进联机身份摘要（终值规则位）与重放装载值；常规对局恒 false。</summary>
        public bool NoDeath;

        /// <summary>进房初值的无限子弹规则（测试房；同上）。常规对局恒 false。</summary>
        public bool InfiniteAmmo;

        /// <summary>瞄准态（ADS）移动上限（m/s），表值 tb_combatnum.aim_move_speed。
        /// 所在判定表进 <see cref="CombatConfigDigest"/>（联机身份）。
        ///
        /// **语义（勿按旧式派生推断）**：本值**不是** <see cref="MoveSpeed"/> 的派生——直接落表
        /// （旧口径 `MoveSpeed × 0.5`、倍率取 2 的幂保位级精确，已随表化退役：直落最终值无乘法、
        /// 无舍入面）。**代价：调 <see cref="MoveSpeed"/> 不再带动限速档**，两列各自独立。
        /// 表值默认 2.5＝视图 Walk 档上界（<c>LocomotionBlendMath.WalkFullMps</c>）——
        /// 瞄准移动只需一套 AimWalk 片段的口径不变。</summary>
        public float AimMoveSpeed;

        /// <summary>射击窗长（逻辑帧数）＝开火态时间，表值 tb_combatnum.fire_stance_frames。
        /// 在 <c>ShootingSystem</c> 判定点置满、<c>InputSystem</c> 逐帧统一递减，
        /// 并按"瞄准 ∨ 开火态"限速 <see cref="AimMoveSpeed"/>。所在判定表进 <see cref="CombatConfigDigest"/>（联机身份）。
        /// View 侧驻留窗按 <c>本值 / SimConfig.TickRate</c> 同源派生。
        ///
        /// **不变式**：<c>1 &lt;= 本值 &lt;= 255</c>——落进 <c>EntitySlot.FireStanceFrames</c>（<c>public byte</c>，
        /// 且进公共快照/双口径 checksum/proto），越界会在强转处静默截断。L1 守卫卡该边界。</summary>
        public int FireStanceFrames;

        /// <summary>构造固定对局数值；重力与出生生命由各自所属表装配（测试规则初值恒 false——
        /// 测试房由入口经 <see cref="MatchTestRules"/> 叠加）。</summary>
        public CombatValues(float moveSpeed, float gravity, float hitscanRange,
            int baseDamage, int damageSpread, int entityHp,
            float aimMoveSpeed, int fireStanceFrames)
        {
            MoveSpeed = moveSpeed;
            Gravity = gravity;
            HitscanRange = hitscanRange;
            BaseDamage = baseDamage;
            DamageSpread = damageSpread;
            EntityHp = entityHp;
            AimMoveSpeed = aimMoveSpeed;
            FireStanceFrames = fireStanceFrames;
            NoDeath = false;
            InfiniteAmmo = false;
        }

        /// <summary>表装面默认值（必须与表值一致——漂移由装载闸门与 L1 守卫卡）。
        /// **属性而非静态字段**：每次取副本，防"共享静态可变存储"复辟（债 #1 的形态）。</summary>
        public static CombatValues Default => new CombatValues(5f, MovementValues.Default.Gravity,
            100f, 25, 1, EntityValues.Default.InitialHp, 2.5f, 60);
    }
}
