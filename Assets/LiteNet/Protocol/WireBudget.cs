using System;
using kcp2k;
using LiteNet.Transport;

namespace LiteNet.Protocol
{
    /// <summary>
    /// **不可靠信封的载荷预算**（《状态同步专项设计》§5.2）。
    ///
    /// 由来：联机实测"打一会儿双方不同步"，根因是快照包被 kcp2k **静默丢弃**——
    /// `KcpPeer.SendUnreliable` 在 `content.Count > unreliableMax` 时只打一行日志就 `return`，
    /// 既不重发也不降级。而当时没有任何一处代码知道"这笔载荷还装得下吗"。
    ///
    /// 本类把这条上限**单源化**：全部数值都从真实常量推导（MTU / kcp 元数据 / 信封头 / 尾标签），
    /// 任何一处改了（换 MTU、加信封字段、换传输），预算自动跟着走，不会留下第二个手抄的 1194。
    ///
    /// 用法：发送前 <c>WireBudget.FitsUnreliable(msg.CalculateSize())</c> 判定；
    /// 装不下就走降级阶梯（见 <c>CombatEventPacker</c>/<c>SnapshotPipeline</c>），**永不静默丢**。
    /// </summary>
    public static class WireBudget
    {
        /// <summary>KCP MTU（kcp2k 默认值——两端同源常量，不在这里另写字面量）。</summary>
        public const int Mtu = Kcp.MTU_DEF;

        /// <summary>kcp2k 对不可靠消息的上限（= MTU − 通道头 − cookie − kcp 头，由 kcp2k 自己算）。</summary>
        public static readonly int UnreliableMax = KcpPeer.UnreliableMaxMessageSize(Mtu);

        /// <summary>安全信封的每包开销（19B 数据头 + 16B AEAD 标签；单源 = 信封协议常量）。</summary>
        public const int EnvelopeBytes = SecureEnvelopeProtocol.DataHeaderBytes + SecureEnvelopeProtocol.TagBytes;

        /// <summary><see cref="PacketCodec"/> 的类型字节（线格式 <c>[1B PacketType][proto 载荷]</c>）。</summary>
        public const int TypeBytes = 1;

        /// <summary>
        /// 单个不可靠包可承载的 **proto 载荷**上限（= 1194 − 35 − 1 = 1158）。
        /// 载荷指 <c>IMessage.CalculateSize()</c> 的值——**不含**信封与类型字节。
        /// </summary>
        public static readonly int ProtoPayload = UnreliableMax - EnvelopeBytes - TypeBytes;

        /// <summary>该 proto 载荷是否装得进一个不可靠包。</summary>
        public static bool FitsUnreliable(int protoBytes) => protoBytes >= 0 && protoBytes <= ProtoPayload;

        /// <summary>
        /// 装不进时的**唯一合法处置**：返回 true 表示需要降级（调用方按各平面的阶梯处理并计数），
        /// false 表示可以直接发。刻意做成"判定而非发送"——发送口只有一条，降级策略各平面自持。
        /// </summary>
        public static bool RequiresFallback(int protoBytes) => !FitsUnreliable(protoBytes);

        /// <summary>用量描述（Ops 横幅/日志用：把"差多少字节"一眼说出来，不用翻常量表）。</summary>
        public static string Describe(int protoBytes)
            => $"proto={protoBytes}B budget={ProtoPayload}B (wire={protoBytes + EnvelopeBytes + TypeBytes}B max={UnreliableMax}B)";
    }
}
