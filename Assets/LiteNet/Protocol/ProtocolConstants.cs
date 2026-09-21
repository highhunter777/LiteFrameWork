using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 快照/背压相关常量集中地（《状态同步实施方案》§11-3 常量集中：一处定义，禁散落魔数）。
    /// 这些都是"广播行为"参数而非"判定"参数——不进 <see cref="SimConfig"/>（Sim 核不该知道网络怎么发），
    /// 但同样是两端/两端可调旋钮，改这里即改行为。
    /// </summary>
    public static class ProtocolConstants
    {
        // ---- 全量兜底（决策 7）----

        /// <summary>定期全量间隔（帧）：60 帧 = 1s。快照丢包累积到最后由它兜底重同步。</summary>
        public const int FullEveryFrames = 60;

        /// <summary>ack 落后触发全量的阈值（帧）：30 帧 = 0.5s 没收到客户端 ack 说明它掉队了。</summary>
        public const int FullResendAckLagFrames = 30;

        // ---- 输入数值边界（R0：《商业级通用服务端框架总设计》§5 P0-3；两端同源旋钮，改值即改校验）----

        /// <summary>Move/Aim 单分量绝对值上限（采集侧契约"长度 ≤1 由采集侧保证"的服务器侧强制值）。</summary>
        public const float MoveComponentLimit = 1f;

        /// <summary>Move/Aim 向量长度平方上限（=1；用平方比较避免开方——开方属超越函数纪律管辖区）。</summary>
        public const float VectorLengthSquaredLimit = 1f;

        // ---- E1 背压降级（决策 11；《服务端架构设计》§10-E1：基线无背压，一个慢客户端会拖垮房间）----
        // 传输层为 kcp2k 内建发送队列（KcpPeer 定长窗口，无慢启动），**队列水位由应用层估算并记账**——
        // 比读 kcp2k 内部状态更稳（T5 断言"其余客户端不受慢客户端拖累"的前提）。

        /// <summary>背压队列上限（字节）：按连接累计未确认的发送字节，超过即降档。4 KB ≈ 30Hz 下 2 帧快照的水位。</summary>
        public const long BackpressureQueueLimitBytes = 4096;

        /// <summary>队列水位低于此比例（40%）且持续 <see cref="RecoverHoldMillis"/> 才逐档恢复（防抖动）。</summary>
        public const double BackpressureRecoverRatio = 0.4;

        /// <summary>恢复保持时长（毫秒）：水位回落后要连续达标这么久才升档。</summary>
        public const long RecoverHoldMillis = 2000;

        /// <summary>档位 1：抽帧广播（发送节拍降到 1/2）。</summary>
        public const int ThrottledStride = 2;

        /// <summary>档位 2：收缩 AOI 半径（米）——从 <see cref="SimConfig.AoiRadius"/>(30) 收到 15。</summary>
        public const float ThrottleAoiRadius = 15f;

        /// <summary>档位 3（最重）：额外裁掉"距视点最远的"实体比例（保留最近 50%）。</summary>
        public const float ThrottleEntityKeepRatio = 0.5f;
    }
}
