namespace LiteSim
{
    /// <summary>
    /// 移动数值**读口**（原"静态装载面"的收窄形态——技术债 #1 根治后）。
    /// </summary>
    public static class MovementConfig
    {
        private static MovementValues _loaded = MovementValues.Default;

        /// <summary>发布装载值（原子实例替换；测试不依赖本槽——机制路径直接传实例）。</summary>
        public static void Publish(in MovementValues values) { _loaded = values; }

        /// <summary>当前装载值实例（表现/工具读口；机制禁读）。</summary>
        public static MovementValues Loaded => _loaded;

        // ---- 表现/工具便捷属性（≈ 原静态字段名，零破坏面） ----
        public static float WalkSpeed => _loaded.WalkSpeed;
        public static float RunSpeed => _loaded.RunSpeed;
        public static float SprintSpeed => _loaded.SprintSpeed;
        public static float Acceleration => _loaded.Acceleration;
        public static float SprintDuration => _loaded.SprintDuration;
        public static float SlideSpeed => _loaded.SlideSpeed;
        public static float SlideFriction => _loaded.SlideFriction;
        public static float SlideTurnPenalty => _loaded.SlideTurnPenalty;
        public static float AirControl => _loaded.AirControl;
        public static float Gravity => _loaded.Gravity;
        public static float JumpSpeed => _loaded.JumpSpeed;
        public static int DoubleJumpCount => _loaded.DoubleJumpCount;
        public static float DoubleJumpSpeed => _loaded.DoubleJumpSpeed;
        public static float GrappleDistance => _loaded.GrappleDistance;
        public static float GrappleSpeed => _loaded.GrappleSpeed;
        public static float GrappleCooldown => _loaded.GrappleCooldown;
        public static float BlinkDistance => _loaded.BlinkDistance;
        public static float BlinkCooldown => _loaded.BlinkCooldown;
    }
}
