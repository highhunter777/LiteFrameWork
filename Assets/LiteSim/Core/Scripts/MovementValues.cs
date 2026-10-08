namespace LiteSim
{
    /// <summary>
    /// 移动数值实例（`tbmovementconfig` 单行表）：与 <see cref="CombatValues"/> 同规——**按值传递**、
    /// 构造后不得再改、<see cref="Default"/> 必须与表值一致（装载侧对账）。
    ///
    /// **机制消费（走/跑/冲/滑铲/空中控制/跳跃/钩爪/闪现）随对应 Sim 系统落地逐项接入**——
    /// 接入时经参数接收本实例（不再读静态面）。重力双表位：本实例 Gravity 是移动家族镜像，
    /// 消费单源在 <see cref="CombatValues.Gravity"/>（一致性由 `ConfigService.ValidateCandidate` 闸门卡）。
    /// </summary>
    public struct MovementValues
    {
        // ---- 基础移速（软上限——硬护栏见 CombatConfig.HardMaxSpeed） ----
        public float WalkSpeed;
        public float RunSpeed;
        public float SprintSpeed;
        public float Acceleration;
        public float SprintDuration;

        // ---- 滑铲 ----
        public float SlideSpeed;
        public float SlideFriction;
        public float SlideTurnPenalty;

        // ---- 空中 / 跳跃 ----
        public float AirControl;
        public float Gravity;
        public float JumpSpeed;
        public int DoubleJumpCount;
        public float DoubleJumpSpeed;

        // ---- 位移技能（钩爪 / 闪现） ----
        public float GrappleDistance;
        public float GrappleSpeed;
        public float GrappleCooldown;
        public float BlinkDistance;
        public float BlinkCooldown;

        /// <summary>构造（字段顺序与 tbmovementconfig 单行表一致）。</summary>
        public MovementValues(float walkSpeed, float runSpeed, float sprintSpeed, float acceleration,
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

        /// <summary>表装面默认值（属性而非静态字段——同 <see cref="CombatValues.Default"/> 防共享可变存储）。</summary>
        public static MovementValues Default => new MovementValues(
            2.5f, 5f, 7.5f, 25f, 2f,
            9f, 8f, 0.5f,
            0.35f, -20f, 8f, 1, 7f,
            30f, 20f, 8f,
            10f, 10f);
    }
}
