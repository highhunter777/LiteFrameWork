namespace LiteSim
{
    /// <summary>
    /// 玩法数值单源（2026-09-19 解耦定案：**手感参数与协议常量分离**——本类只装"一局战斗怎么打"，
    /// SimConfig 只装"确定性架构怎么搭"；两者生命周期不同：前者可调表迭代，后者编译期锁死）。
    ///
    /// - 默认值 = M8 灰盒实测值（原 SimConfig 玩法段迁移，消费点改名同步）；
    /// - **Luban 表链路已通（2026-09-19）**：表源 `Luban/Data/#combatnum.xlsx` → `gen.bat` 双产物
    ///   （客户端 bin `Assets/GameData/Config/tbcombatnum.bytes`；服务端 json `RoomServer/Data/tbcombatnum.json`）
    ///   → 启动装配调 <see cref="LoadFrom"/> 回填（客户端 `ConfigService`；服务端 `Program`）。
    ///   **本类的默认值必须与表值一致**（L1 守卫用例 `CombatNumbersTests` 卡住漂移）；装载后两端同值（表数据进 buildHash，不一致直接拒进房）。
    /// - 确定性：全部 float/int 常量语义不变（位级确定的输入，无运算）。
    /// </summary>
    public static class CombatConfig
    {
        // ---- 移动 ----

        /// <summary>玩家移动速度（m/s，2.5D XZ 平面）。</summary>
        public static float MoveSpeed { get; private set; } = 5f;

        /// <summary>
        /// 瞄准态移速倍率（右键 ADS 期间移动上限 = <see cref="MoveSpeed"/> × 本值）。
        /// **取 0.5 = 乘 2 的幂**：位级精确、不引舍入（确定性纪律）。5 × 0.5 = **2.5 m/s**，
        /// 恰好等于视图 Walk 档上界（`LocomotionBlendMath.WalkFullMps`）——限速后"瞄准移动"
        /// 只需要 `AimWalk_*` 一套片段，不需要 AimJog。
        /// **表化待补**：数值表加列 `aim_move_speed` 后本常量让位（登记在《角色状态与动作专项设计》§7）。
        /// </summary>
        public const float AimMoveSpeedFactor = 0.5f;

        /// <summary>瞄准态移动速度上限（m/s）= <see cref="MoveSpeed"/> × <see cref="AimMoveSpeedFactor"/>。
        /// 参与 <see cref="CombatConfigDigest"/>——**联机身份**：两端不一致会以摘要不符当场拒进房。</summary>
        public static float AimMoveSpeed => MoveSpeed * AimMoveSpeedFactor;

        /// <summary>重力加速度（m/s²，y 轴向下，§3.5）。</summary>
        public static float Gravity { get; private set; } = -20f;

        /// <summary>**最大速度硬上限**（m/s，水平合速度）——服务器代码兜底（2026-09-28 用户裁决：
        /// "配置只做软上限"）：表值（走/跑/冲/滑铲/钩爪……MovementConfig）怎么调都是设计软值，
        /// 本护栏只对配置错误/增益叠加/未来机制 bug 生效，防实体被吹飞。取值盖过表内最快设计速度
        /// （钩爪拉拽 20）留 ~25% 余量；**刻意不进 digest**（代码常量两端编译期同值，无需摘要）。</summary>
        public const float HardMaxSpeed = 25f;

        // ---- 射击 ----

        /// <summary>hitscan 射程（m）。</summary>
        public static float HitscanRange { get; private set; } = 100f;

        /// <summary>
        /// 射击窗长（逻辑帧数）——**开火态时间**（2026-09-30 六次裁决：**1s @60Hz = 60 帧独立常量**，
        /// 与开火动画时长解耦——四次修正"窗长=动画时长换算"废止；事件刷新＝重置满窗，上限即窗长）。
        /// View 侧驻留窗按 `FireStanceFrames / SimConfig.TickRate` **同源派生**（改窗长只动此处）。
        /// **不变式：窗长 ≥ 开火片段播放时长**（防事件后窗先尽截断在播射击片段——装配期校验）。
        /// 开火限速的 Sim 权威镜像（`EntitySlot.FireStanceFrames` ＋ `InputSystem` 限速 ＋ digest）
        /// 随批次C 落地——本常量先作窗长单源（《角色状态与动作专项设计》§7 限速行）。
        /// </summary>
        public const int FireStanceFrames = 60;

        /// <summary>命中圆柱半径（m）。</summary>
        public static float HitscanRadius { get; private set; } = 0.5f;

        /// <summary>命中圆柱高度（m，区间 [Pos.Y, Pos.Y + Height]）。</summary>
        public static float HitscanHeight { get; private set; } = 2f;

        // ---- 伤害 ----

        /// <summary>基础伤害（命中值 = BaseDamage ± DamageSpread 内浮动）。</summary>
        public static int BaseDamage { get; private set; } = 25;

        /// <summary>伤害浮动幅度（命中值 = Base + rng.NextRange(-Spread, Spread+1)；0 = 无浮动）。
        /// ——自原 `+ rng.NextRange(0, 3) - 1` 表达式提取，语义等价 ±1。</summary>
        public static int DamageSpread { get; private set; } = 1;

        /// <summary>出生 HP（实体初始生命）。**表字段 `entity_hp`**（M11 实体表化前并入本表：单一职责暂借位）。</summary>
        public static int EntityHp { get; private set; } = 100;

        /// <summary>
        /// Luban 表装载接缝（批⑤/M11 接线）：tb_combat_num 读取后调用，逐字段覆写。
        /// 当前表链路未落地——保留默认值；参数未做合法性钳制（来源是策划表而非外部输入）。
        /// </summary>
        public static void LoadFrom(float moveSpeed, float gravity, float hitscanRange, float hitscanRadius,
            float hitscanHeight, int baseDamage, int damageSpread, int entityHp)
        {
            MoveSpeed = moveSpeed;
            Gravity = gravity;
            HitscanRange = hitscanRange;
            HitscanRadius = hitscanRadius;
            HitscanHeight = hitscanHeight;
            BaseDamage = baseDamage;
            DamageSpread = damageSpread;
            EntityHp = entityHp;
        }
    }
}
