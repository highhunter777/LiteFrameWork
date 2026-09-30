namespace LiteSim
{
    /// <summary>
    /// 移动数值单源（2026-09-28 用户配置批）：与 <see cref="CombatConfig"/> 同构的**表化数值面**——
    /// 表源 `Luban/Data/#movementconfig.xlsx` → `ConfigService.ApplyMovementNumbers` 回填。
    ///
    /// **当前消费面**：表链路与装载（两端同源）；**机制消费（走/跑/冲/滑铲/空中控制/跳跃/钩爪/闪现）
    /// 随对应 Sim 系统落地逐项接入**——接入时随批次进 <see cref="CombatConfigDigest"/>（联机身份：
    /// 两端不一致以摘要不符拒进房），本版不预挂（不预建无消费者的纪律同样适用于摘要面）。
    /// **硬上限不在本类**：<see cref="CombatConfig.HardMaxSpeed"/> 是代码兜底护栏（用户裁决——
    /// "配置只做软上限，服务器代码兜底"），本类的速度字段全部是设计软值。
    ///
    /// - 默认值必须与表值一致（漂移由装载闸门与对账守卫卡）；
    /// - <see cref="Gravity"/> 与 combatnum.gravity 是**同一物理量的双表位**——一致性由
    ///   `ConfigService.ValidateCandidate` 闸门卡死（重力单源在 combatnum：Sim 的
    ///   <c>MovementSystem</c> 只读 CombatConfig.Gravity）。
    /// </summary>
    public static class MovementConfig
    {
        // ---- 基础移速（软上限——硬护栏见 CombatConfig.HardMaxSpeed） ----

        /// <summary>走路速度（m/s）。</summary>
        public static float WalkSpeed { get; private set; } = 2.5f;

        /// <summary>跑步速度（m/s）。</summary>
        public static float RunSpeed { get; private set; } = 5f;

        /// <summary>冲刺速度（m/s）。</summary>
        public static float SprintSpeed { get; private set; } = 7.5f;

        /// <summary>加减速度（m/s²）。</summary>
        public static float Acceleration { get; private set; } = 25f;

        /// <summary>冲刺持续时间（s）。</summary>
        public static float SprintDuration { get; private set; } = 2f;

        // ---- 滑铲 ----

        /// <summary>滑铲初速（m/s）。</summary>
        public static float SlideSpeed { get; private set; } = 9f;

        /// <summary>滑铲衰减（m/s²）。</summary>
        public static float SlideFriction { get; private set; } = 8f;

        /// <summary>滑铲转向惩罚（0~1：1 = 转向完全生效减速，0 = 不惩罚）。</summary>
        public static float SlideTurnPenalty { get; private set; } = 0.5f;

        // ---- 空中 / 跳跃 ----

        /// <summary>空中控制系数（0~1：1 = 地面全速转向，0 = 完全无控）。</summary>
        public static float AirControl { get; private set; } = 0.35f;

        /// <summary>重力（m/s²，负值向下）。**双表位一致性由装载闸门守卫**——消费单源在
        /// <see cref="CombatConfig.Gravity"/>（MovementSystem 只读那边），本字段是移动家族的表位镜像。</summary>
        public static float Gravity { get; private set; } = -20f;

        /// <summary>跳跃初速（m/s）。</summary>
        public static float JumpSpeed { get; private set; } = 8f;

        /// <summary>二段跳次数（0 = 无二段跳）。</summary>
        public static int DoubleJumpCount { get; private set; } = 1;

        /// <summary>二段跳速度（m/s）。</summary>
        public static float DoubleJumpSpeed { get; private set; } = 7f;

        // ---- 位移技能（钩爪 / 闪现） ----

        /// <summary>钩爪距离（m）。</summary>
        public static float GrappleDistance { get; private set; } = 30f;

        /// <summary>钩爪拉拽速度（m/s）。</summary>
        public static float GrappleSpeed { get; private set; } = 20f;

        /// <summary>钩爪冷却（s）。</summary>
        public static float GrappleCooldown { get; private set; } = 8f;

        /// <summary>闪现距离（m）。</summary>
        public static float BlinkDistance { get; private set; } = 10f;

        /// <summary>闪现冷却（s）。</summary>
        public static float BlinkCooldown { get; private set; } = 10f;

        /// <summary>
        /// Luban 表装载接缝（tbmovementconfig 读取后调用，逐字段覆写——与 <see cref="CombatConfig.LoadFrom"/>
        /// 同纪律：参数不做合法性钳制，来源是策划表而非外部输入；一致性闸门在装载侧 ValidateCandidate）。
        /// </summary>
        public static void LoadFrom(float walkSpeed, float runSpeed, float sprintSpeed, float acceleration,
            float sprintDuration, float slideSpeed, float slideFriction, float slideTurnPenalty,
            float airControl, float gravity, float jumpSpeed, int doubleJumpCount, float doubleJumpSpeed,
            float grappleDistance, float grappleSpeed, float grappleCooldown,
            float blinkDistance, float blinkCooldown)
        {
            WalkSpeed = walkSpeed;
            RunSpeed = runSpeed;
            SprintSpeed = sprintSpeed;
            Acceleration = acceleration;
            SprintDuration = sprintDuration;
            SlideSpeed = slideSpeed;
            SlideFriction = slideFriction;
            SlideTurnPenalty = slideTurnPenalty;
            AirControl = airControl;
            Gravity = gravity;
            JumpSpeed = jumpSpeed;
            DoubleJumpCount = doubleJumpCount;
            DoubleJumpSpeed = doubleJumpSpeed;
            GrappleDistance = grappleDistance;
            GrappleSpeed = grappleSpeed;
            GrappleCooldown = grappleCooldown;
            BlinkDistance = blinkDistance;
            BlinkCooldown = blinkCooldown;
        }
    }
}
