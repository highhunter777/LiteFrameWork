namespace LiteFramework.Animation
{
    /// <summary>
    /// 启动结果（区分接受与拒绝）。拒绝**不改变现有播放**（提交前失败保持原姿态）。
    /// </summary>
    public readonly struct AnimationStartResult
    {
        /// <summary>拒绝原因（稳定可诊断；Accepted 时为 <see cref="None"/>）。</summary>
        public enum Reason
        {
            None = 0,
            /// <summary>AnimationId 无对应定义（Profile 缺失）。</summary>
            InvalidDefinition,
            /// <summary>当前后端不支持该能力（不把不支持的能力静默降级）。</summary>
            UnsupportedCapability,
            /// <summary>通道容量/混合尾部超限。</summary>
            CapacityExceeded,
            /// <summary>Owner 已失效（代次过期/已释放）。</summary>
            OwnerUnavailable,
            /// <summary>请求字段非法（空 ID、非有限速度、越界开始位置等）。</summary>
            InvalidRequest,
        }

        public readonly bool Accepted;
        public readonly AnimationHandle Handle;
        public readonly Reason RejectReason;

        private AnimationStartResult(bool accepted, AnimationHandle handle, Reason reason)
        {
            Accepted = accepted;
            Handle = handle;
            RejectReason = reason;
        }

        public static AnimationStartResult Accept(AnimationHandle handle) => new AnimationStartResult(true, handle, Reason.None);
        public static AnimationStartResult Reject(Reason reason) => new AnimationStartResult(false, default, reason);
    }
}
