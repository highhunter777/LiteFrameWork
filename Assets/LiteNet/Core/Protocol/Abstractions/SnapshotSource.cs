using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 服务器广播状态（《M10实施指导》决策 7）。
    ///
    /// 两段式（多客户端必需）：<see cref="BeginFrame"/> 每广播帧一次（算差分、打包比赛状态层、推进基线），
    /// <see cref="BuildFor"/> 每客户端一次（纯读，按各自 AOI 视点过滤 + 附本人私有面）——见 <see cref="SnapshotDiffer"/> 注释。
    /// </summary>
    public interface ISnapshotSource
    {
        /// <summary>每广播帧一次：算本帧变化集 / 判定全量 / 推进基线。<paramref name="forceFull"/> = 整帧强制全量。</summary>
        void BeginFrame(int frame, SimWorldState state, bool forceFull = false);

        /// <summary>每客户端一次：取该客户端可见的槽位（全量帧 = 全部可见活体；增量帧 = 可见 ∩ 变化集）+
        /// 比赛状态层 + 本人私有面（<paramref name="viewerEntityId"/> ≠ 0 时）。</summary>
        Proto.StateSnapshot BuildFor(int frame, SimWorldState state, int ackInput, SimVector3 viewPos, float aoiRadius, long viewerEntityId);

        /// <summary>便捷组合（单客户端/测试）：BeginFrame + BuildFor。</summary>
        Proto.StateSnapshot Build(int frame, SimWorldState state, int ackInput, SimVector3 viewPos, float aoiRadius, bool forceFull = false);

        /// <summary>便捷组合（默认视点：AOI 关/开按 <see cref="SimConfig.AoiRadius"/>）。</summary>
        Proto.StateSnapshot Build(int frame, SimWorldState state, int ackInput);

        /// <summary>该 ack 的客户端是否应改发全量兜底（ack 落后 / 距上次全量过久）。</summary>
        bool NeedsFull(int clientAckSnapshot);

        /// <summary>最近一次构建/广播的帧号（-1 = 尚未构建）。诊断与用例断言用。</summary>
        int LastBroadcastFrame { get; }

        /// <summary>距上次全量的帧数（周期兜底判据；无全量史 = int.MaxValue）。</summary>
        int FramesSinceFull { get; }

        /// <summary>本帧是否被判为全量帧（<see cref="BeginFrame"/> 之后有效）。</summary>
        bool FrameIsFull { get; }
    }
}
