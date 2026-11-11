namespace LiteGame.Modules.Battle.SubModules.ArenaSubModule.RootMotion
{
    /// <summary>根位移方向策略（《联机动画与根位移专项设计》§3 DeltaYaw / DirectionPolicy）。
    /// 起始锁定 = 动作全程按发起朝向旋转局部增量（逐帧 yaw 数组恒零）；
    /// 其余值预留（逐帧转体/有限输入修正——接入时两端同步定稿）。</summary>
    public enum RootMotionDirectionPolicy : byte
    {
        StartFacingLocked = 0,
        FrameByFrame = 1,
        InputCorrected = 2,
    }

    /// <summary>动作期间普通输入位移策略（§3 MovementPolicy：禁用/叠加/有限修正——执行层消费）。</summary>
    public enum RootMotionMovementPolicy : byte
    {
        Disable = 0,
        Stack = 1,
        Limited = 2,
    }

    /// <summary>动作取消策略（§3 CancelPolicy：取消帧窗与取消后速度归属由执行层按策略定稿）。</summary>
    public enum RootMotionCancelPolicy : byte
    {
        NotCancellable = 0,
        Windowed = 1,
    }

    /// <summary>
    /// 单个动作的烘焙根位移（§3 位移数据契约：**只读玩法数据**，服务端不加载 AnimationClip/Avatar/Animator——两端经同一份数据产物共享同一组数值）。
    /// </summary>
    public sealed class RootMotionEntry
    {
        public int ActionId;
        public int MotionVersion;

        /// <summary>每 tick 位移增量数（有效索引 i ∈ [0, SampleCount)，i = SimFrame − StartFrame）。</summary>
        public int SampleCount;

        public RootMotionDirectionPolicy DirectionPolicy;
        public RootMotionMovementPolicy MovementPolicy;
        public RootMotionCancelPolicy CancelPolicy;

        /// <summary>烘焙期检测到的 Y 位移峰值（毫米）——校验用（YPolicy=Sim 重力时非零应复查源资产）。</summary>
        public int YPeakMm;

        /// <summary>平面局部增量（毫米）：[2i]=X、[2i+1]=Z；长度恒 = 2×SampleCount。</summary>
        public short[] DeltaLocalXzMm;

        /// <summary>每 tick 偏航（厘度）：长度恒 = SampleCount（恒零）。</summary>
        public short[] DeltaYawCentiDeg;
    }
}
