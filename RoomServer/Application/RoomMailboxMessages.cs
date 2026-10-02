using System;
using Google.Protobuf;
using LiteNet.Protocol;
using LiteSim;
using RoomServer.Runtime;

namespace RoomServer.Application
{
    /// <summary>
    /// 宿主侧控制消息封装。
    ///
    /// <para>
    /// <see cref="RoomCommand"/> 只表示 Runtime 能理解的纯命令；这个封装补上
    /// 宿主路由所需的房间、连接和会话代次。代次由装配方提供，用来丢弃迟到消息；
    /// 消息本身不持有可变的 Session 引用。
    /// </para>
    /// </summary>
    public readonly struct RoomControlEnvelope
    {
        public string RoomId { get; }
        public int ConnectionId { get; }
        public int PlayerId { get; }
        public long SessionEpoch { get; }
        public long ReceivedAtMs { get; }
        public RoomCommand Command { get; }

        /// <summary>
        /// 该 Rebind 是重连握手（Worker 执行形态下需回传 ReconnectResponse）。
        /// 由重连 admission 显式标注——不在 Worker 侧按命令种类猜。
        /// </summary>
        public bool ExpectRebindResponse { get; }

        public RoomControlEnvelope(string roomId, int connectionId, int playerId,
            long sessionEpoch, long receivedAtMs, in RoomCommand command, bool expectRebindResponse = false)
        {
            if (command.Kind == RoomCommandKind.ClientInput)
                throw new ArgumentException("ClientInput 必须进入 Input lane", nameof(command));
            if ((command.Kind == RoomCommandKind.AuthenticatedJoin
                    || command.Kind == RoomCommandKind.Disconnect)
                && command.ConnectionId != connectionId)
                throw new ArgumentException("Control 元数据的连接 Id 必须与命令一致", nameof(connectionId));
            if ((command.Kind == RoomCommandKind.Rebind
                    || command.Kind == RoomCommandKind.RestoreAck)
                && command.PlayerId != playerId)
                throw new ArgumentException("Control 元数据的玩家 Id 必须与命令一致", nameof(playerId));

            RoomId = roomId;
            ConnectionId = connectionId;
            PlayerId = playerId;
            SessionEpoch = sessionEpoch;
            ReceivedAtMs = receivedAtMs;
            Command = command;
            ExpectRebindResponse = expectRebindResponse;
        }

        public static RoomControlEnvelope FromCommand(string roomId, int connectionId,
            int playerId, long sessionEpoch, long receivedAtMs, in RoomCommand command,
            bool expectRebindResponse = false)
            => new RoomControlEnvelope(roomId, connectionId, playerId, sessionEpoch,
                receivedAtMs, command, expectRebindResponse);

        public override string ToString()
            => $"room={RoomId ?? "<unbound>"} conn={ConnectionId} player={PlayerId} kind={Command.Kind}";
    }

    /// <summary>
    /// 宿主侧普通输入封装。
    ///
    /// <para>
    /// 构造时复制 <see cref="ClientInputBatch.Frames"/>。当前 ServerHost 为降低热路径
    /// 分配而复用输入数组；不复制就把复用数组的生命周期带进 Mailbox，下一包会覆盖
    /// 尚未消费的输入。<see cref="Count"/> 原值保留，因此超冗余包仍由 InputGate
    /// 按原有语义拒绝，而不是在队列边界被悄悄修形。
    /// </para>
    /// </summary>
    public readonly struct RoomInputEnvelope
    {
        public string RoomId { get; }
        public int ConnectionId { get; }
        public int PlayerId { get; }
        public long SessionEpoch { get; }
        public long ReceivedAtMs { get; }
        public RoomCommand Command { get; }

        public RoomInputEnvelope(string roomId, int connectionId, int playerId,
            long sessionEpoch, long receivedAtMs, in RoomCommand command)
        {
            if (command.Kind != RoomCommandKind.ClientInput)
                throw new ArgumentException("Input lane 只能承载 ClientInput", nameof(command));
            if (command.PlayerId != playerId)
                throw new ArgumentException("Input 元数据的玩家 Id 必须与命令一致", nameof(playerId));

            RoomId = roomId;
            ConnectionId = connectionId;
            PlayerId = playerId;
            SessionEpoch = sessionEpoch;
            ReceivedAtMs = receivedAtMs;
            Command = RoomCommand.ClientInput(command.PlayerId, CloneInput(command.Input));
        }

        public static RoomInputEnvelope FromCommand(string roomId, int connectionId,
            int playerId, long sessionEpoch, long receivedAtMs, in RoomCommand command)
            => new RoomInputEnvelope(roomId, connectionId, playerId, sessionEpoch,
                receivedAtMs, command);

        private static ClientInputBatch CloneInput(in ClientInputBatch source)
        {
            // InputGate 先检查 Count，再访问 Frames；保留原 Count 可让超限包继续走
            // DroppedOversizedMessage，而不是被封装层改成另一种错误。
            int sourceLength = source.Frames == null ? 0 : source.Frames.Length;
            int copyLength = Math.Min(sourceLength, ClientInputBatch.MaxFrames);
            SimInputFrame[] frames = copyLength == 0
                ? Array.Empty<SimInputFrame>()
                : new SimInputFrame[copyLength];
            if (copyLength != 0)
                Array.Copy(source.Frames, frames, copyLength);

            return new ClientInputBatch
            {
                Frame = source.Frame,
                AckSnapshot = source.AckSnapshot,
                ViewFrame = source.ViewFrame,
                Count = source.Count,
                Frames = frames,
            };
        }

        public override string ToString()
            => $"room={RoomId ?? "<unbound>"} conn={ConnectionId} player={PlayerId} frame={Command.Input.Frame}";
    }

    /// <summary>
    /// Worker → Host 的出站事实基类（Outbound lane 载荷；宿主是唯一消费者）。
    ///
    /// **为什么不是 RoomOutput**：Runtime 的输出是纯域事件（不见 proto）；而 Worker 执行形态下
    /// Worker 还要回传"已构建好的协议消息"（快照/重连响应）与"会话映射事实"这些 App 层产物。
    /// 本基类把三者统一成同一条有界回传通道上的事实，宿主按类型应用。
    /// </summary>
    public abstract class RoomOutboundItem
    {
    }

    /// <summary>Runtime 纯输出（信令/拒绝/状态迁移/结算）——宿主按信封来源会话解析应用。</summary>
    public sealed class RoomOutputItem : RoomOutboundItem
    {
        public readonly RoomOutput Output;

        public RoomOutputItem(RoomOutput output)
        {
            Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public override string ToString() => $"output={Output.GetType().Name}";
    }

    /// <summary>
    /// 发送意图：Worker 已构建消息（快照/重连响应），宿主解析会话、编码并经 Transport 发送。
    /// 编码与发送留在宿主——Transport 是宿主所有权。
    /// </summary>
    public sealed class RoomSendItem : RoomOutboundItem
    {
        public readonly int ConnectionId;
        public readonly long SessionEpoch;
        public readonly PacketType Type;
        public readonly IMessage Message;
        public readonly bool Reliable;

        public RoomSendItem(int connectionId, long sessionEpoch, PacketType type, IMessage message, bool reliable)
        {
            ConnectionId = connectionId;
            SessionEpoch = sessionEpoch;
            Type = type;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Reliable = reliable;
        }

        public override string ToString() => $"conn={ConnectionId} type={Type}";
    }

    /// <summary>
    /// 重连落定：席位已从旧连接换绑到新连接（Runtime 侧已完成）。
    /// 宿主据此更新会话映射（新连接接管席位、旧连接失效）并计 Ops；响应包随后经
    /// <see cref="RoomSendItem"/> 发送（同一 lane FIFO——映射先于响应）。
    /// </summary>
    public sealed class RoomSeatReboundItem : RoomOutboundItem
    {
        public readonly int PlayerId;
        public readonly int OldConnectionId;
        public readonly int NewConnectionId;

        public RoomSeatReboundItem(int playerId, int oldConnectionId, int newConnectionId)
        {
            PlayerId = playerId;
            OldConnectionId = oldConnectionId;
            NewConnectionId = newConnectionId;
        }

        public override string ToString() => $"player={PlayerId} conn {OldConnectionId}->{NewConnectionId}";
    }

    /// <summary>
    /// 宿主侧出站封装（Worker → Host 回传边界）。
    ///
    /// <para>
    /// 把出站事实与房间号、序号及**产出该事实的命令来源**（连接 + 会话代次）绑定：
    /// 宿主应用纯输出时按来源解析进房会话（JoinAck 的发送对象），发送项则按自身连接校验。
    /// 它不持有 Session 引用，也不把 Transport 放进 Runtime。
    /// </para>
    /// </summary>
    public readonly struct RoomOutboundEnvelope
    {
        /// <summary>
        /// 连接号哨兵（未绑定 / 宿主自发命令，如 Tick）。
        /// **连接号是传输层原值**——kcp2k 用随机 int，可为负；因此"无连接"判定必须用**相等比较**，
        /// 不能用符号判断（旧式 `&lt; 0` 会把真实负连接号误判成无连接）。
        /// </summary>
        public const int NoConnectionId = -1;

        /// <summary>来源连接（产出该事实的命令连接；Tick/超时等宿主自发命令为 <see cref="NoConnectionId"/>）。</summary>
        public const int NoOrigin = NoConnectionId;

        public string RoomId { get; }
        public long Sequence { get; }
        public int OriginConnectionId { get; }

        /// <summary>来源连接的会话代次（0 = 不校验——旧式直驱用例）。</summary>
        public long OriginSessionEpoch { get; }

        public RoomOutboundItem Item { get; }

        public RoomOutboundEnvelope(string roomId, long sequence, int originConnectionId,
            long originSessionEpoch, RoomOutboundItem item)
        {
            if (string.IsNullOrEmpty(roomId)) throw new ArgumentException("roomId is required.", nameof(roomId));
            RoomId = roomId;
            Sequence = sequence;
            OriginConnectionId = originConnectionId;
            OriginSessionEpoch = originSessionEpoch;
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }

        public static RoomOutboundEnvelope FromOutput(string roomId, long sequence, int originConnectionId,
            long originSessionEpoch, RoomOutput output)
            => new RoomOutboundEnvelope(roomId, sequence, originConnectionId, originSessionEpoch,
                new RoomOutputItem(output));

        public static RoomOutboundEnvelope FromSend(string roomId, long sequence, RoomSendItem send)
            => new RoomOutboundEnvelope(roomId, sequence, NoOrigin, 0, send);

        /// <summary>重连落定：来源 = 新连接（宿主据此解析接管席位的会话）。</summary>
        public static RoomOutboundEnvelope FromSeatRebound(string roomId, long sequence,
            int originConnectionId, long originSessionEpoch, RoomSeatReboundItem rebound)
            => new RoomOutboundEnvelope(roomId, sequence, originConnectionId, originSessionEpoch, rebound);

        public override string ToString()
            => $"room={RoomId} seq={Sequence} origin={OriginConnectionId}/{OriginSessionEpoch} {Item}";
    }
}
