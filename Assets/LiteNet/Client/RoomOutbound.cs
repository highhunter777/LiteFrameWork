using System;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;

namespace LiteNet
{
    /// <summary>
    /// C→S 协议动作的打包与发送（RoomClient 的行内件，M3-6 拆分）：Join / 逐帧输入 / 和解上报 /
    /// 开局请求 / 离场 / 恢复宣告 / 重拨 / 探针心跳——每条动作只做"按协议单源编码 + 交给传输"。
    ///
    /// **边界**：不做相位/终局门控（那是 <see cref="RoomClient"/> 的会话策略），不持有会话事实；
    /// 复用同一 <see cref="PacketWriter"/>（高频输入零分配）。传输是注入依赖，本类**不 Dispose 传输**。
    /// </summary>
    internal sealed class RoomOutbound
    {
        private readonly IClientTransport _transport;
        private readonly PacketWriter _writer = new PacketWriter();
        private bool _disposed;

        public RoomOutbound(IClientTransport transport)
            => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

        /// <summary>输入冗余窗口（记录/打包窗口由 RoomClient 驱动；本类只编码）。</summary>
        public InputWindow Input { get; } = new InputWindow();

        public void Join(string roomId, string token, string buildHash)
            => Send(PacketType.Join, new Proto.JoinRequest { RoomId = roomId, Token = token, BuildHash = buildHash }, true);

        public void StartMatchRequest()
            => Send(PacketType.StartMatchRequest, new Proto.StartMatchRequest(), true);

        /// <summary>逐帧输入（<paramref name="window"/> = 未确认段冗余帧；打包口径见 <see cref="InputPacker"/>）。</summary>
        public void SendInput(int frame, ReadOnlySpan<SimInputFrame> window, int ackSnapshot, int viewFrame)
            => Send(PacketType.Input, InputPacker.Pack(frame, window, ackSnapshot, viewFrame), false);

        public void Mismatch(int frame)
            => Send(PacketType.MismatchReport, new Proto.MismatchReport { Frame = frame }, false);

        public void Leave() => Send(PacketType.Leave, new Proto.Leave(), true);

        public void RestoreComplete() => Send(PacketType.RestoreComplete, new Proto.RestoreComplete(), true);

        public void ReconnectRequest(string oneTimeToken)
            => Send(PacketType.ReconnectRequest, new Proto.ReconnectRequest { OneTimeToken = oneTimeToken }, true);

        /// <summary>探针心跳（client_time = 发送时刻，兼作探针 id——服务器原样回显）。</summary>
        public void Heartbeat(long clientTime)
            => Send(PacketType.Heartbeat, new Proto.Heartbeat { ClientTime = clientTime }, false);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }

        /// <summary>编码并发包（传输同步拷贝，返回后缓冲即可被下次 Write 覆盖）。</summary>
        public void Send(PacketType type, Google.Protobuf.IMessage message, bool reliable)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RoomOutbound));
            _transport.Send(_writer.Write(type, message), reliable);
        }
    }
}
