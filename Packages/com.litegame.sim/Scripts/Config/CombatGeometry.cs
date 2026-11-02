namespace LiteSim
{
    /// <summary>
    /// 判定几何（表 `#combatgeom` 的 13 列投影）：随 <see cref="CombatValues"/> 按值传递，
    /// 机制面只读实例，不读任何静态读口。字段来源与顺序见 `TableLayout`/`#combatgeom`。
    /// </summary>
    public readonly struct CombatGeometry
    {
        public readonly float BodyRadius;
        public readonly float HitscanHeight;
        public readonly float HitscanRadius;
        public readonly float HeadRatio;
        public readonly float HeadRadius;
        public readonly float MuzzleForward;
        public readonly float MuzzleRight;
        public readonly float MuzzleHeight;
        public readonly int HeadshotDamageShift;
        public readonly float HipSpreadFactor;
        public readonly int CorpseFrames;
        public readonly float HardMaxSpeed;
        public readonly float FaceTurnRadPerSec;

        public CombatGeometry(float bodyRadius, float hitscanHeight, float hitscanRadius,
            float headRatio, float headRadius, float muzzleForward, float muzzleRight, float muzzleHeight,
            int headshotDamageShift, float hipSpreadFactor, int corpseFrames,
            float hardMaxSpeed, float faceTurnRadPerSec)
        {
            BodyRadius = bodyRadius;
            HitscanHeight = hitscanHeight;
            HitscanRadius = hitscanRadius;
            HeadRatio = headRatio;
            HeadRadius = headRadius;
            MuzzleForward = muzzleForward;
            MuzzleRight = muzzleRight;
            MuzzleHeight = muzzleHeight;
            HeadshotDamageShift = headshotDamageShift;
            HipSpreadFactor = hipSpreadFactor;
            CorpseFrames = corpseFrames;
            HardMaxSpeed = hardMaxSpeed;
            FaceTurnRadPerSec = faceTurnRadPerSec;
        }

        /// <summary>头部带下沿（相对脚底）＝身高 × 比例；派生属性不落表列。</summary>
        public float HeadHitLine => HitscanHeight * HeadRatio;

        /// <summary>爆头柱半径；表列直落。</summary>
        public float HeadshotRadius => HeadRadius;

        /// <summary>逻辑枪口世界位：本体 Pos + 朝向系偏移（前向/右向/高度）。</summary>
        public SimVector3 MuzzleOrigin(in SimVector3 pos, float yaw)
        {
            float cy = SimTrig.Cos(yaw);
            float sy = SimTrig.Sin(yaw);
            return new SimVector3(
                pos.X + cy * MuzzleForward + sy * MuzzleRight,
                pos.Y + MuzzleHeight,
                pos.Z + sy * MuzzleForward - cy * MuzzleRight);
        }

        /// <summary>
        /// 展示/测试兜底值（与当前编译期常量同值，非权威）；运行期权威值来自 `Combat/tbcombatgeom`。
        /// </summary>
        public static CombatGeometry Default { get; } = new CombatGeometry(
            0.32f, 1.8f, 0.45f, 0.805f, 0.15f,
            0.7739859f, 0.0829204246f, 1.29482734f,
            1, 4f, 180, 25f, 12f);
    }
}
