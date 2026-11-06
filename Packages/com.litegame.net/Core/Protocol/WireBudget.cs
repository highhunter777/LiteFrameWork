using System;
using kcp2k;
using LiteNet.Transport;

namespace LiteNet.Protocol
{
    /// <summary>
    /// **不可靠信封的载荷预算**（《状态同步专项设计》§5.2）。
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
