using System;
using Google.Protobuf;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 信封编解码（§4.6 可替换性：纯函数，零 IO 零状态零 kcp2k 依赖）。
    /// 线格式：[1B PacketType][proto 载荷]。
    /// 健壮性约定：坏包/未知类型/截断载荷 → TryDecode 返回 false（丢弃不抛——服务器对传输层垃圾零容忍零崩溃）。
    /// </summary>
    public static class PacketCodec
    {
        public static byte[] Encode(PacketType type, IMessage message)
        {
            byte[] payload = message.ToByteArray();
            byte[] packet = new byte[1 + payload.Length];
            packet[0] = (byte)type;
            Buffer.BlockCopy(payload, 0, packet, 1, payload.Length);
            return packet;
        }

        /// <summary>解包；未知类型、截断、proto 解析失败一律 false。载荷缓冲为独立拷贝（调用方可长期持有）。</summary>
        public static bool TryDecode(ArraySegment<byte> data, out PacketType type, out IMessage message)
        {
            type = PacketType.None;
            message = null;
            if (data.Count < 1) return false;

            type = (PacketType)data.Array[data.Offset];
            if (type == PacketType.None) return false;

            byte[] payload = new byte[data.Count - 1];
            Buffer.BlockCopy(data.Array, data.Offset + 1, payload, 0, payload.Length);

            try
            {
                switch (type)
                {
                    case PacketType.Join: message = Proto.JoinRequest.Parser.ParseFrom(payload); return true;
                    case PacketType.JoinAck: message = Proto.JoinAck.Parser.ParseFrom(payload); return true;
                    case PacketType.StartGame: message = Proto.StartGame.Parser.ParseFrom(payload); return true;
                    case PacketType.Input: message = Proto.InputMessage.Parser.ParseFrom(payload); return true;
                    case PacketType.StateSnapshot: message = Proto.StateSnapshot.Parser.ParseFrom(payload); return true;
                    case PacketType.MismatchReport: message = Proto.MismatchReport.Parser.ParseFrom(payload); return true;
                    case PacketType.Heartbeat: message = Proto.Heartbeat.Parser.ParseFrom(payload); return true;
                    case PacketType.Leave: message = Proto.Leave.Parser.ParseFrom(payload); return true;
                    case PacketType.ReconnectRequest: message = Proto.ReconnectRequest.Parser.ParseFrom(payload); return true;
                    case PacketType.ReconnectResponse: message = Proto.ReconnectResponse.Parser.ParseFrom(payload); return true;
                    case PacketType.RestoreComplete: message = Proto.RestoreComplete.Parser.ParseFrom(payload); return true;
                    default: return false;
                }
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }
        }
    }
}
