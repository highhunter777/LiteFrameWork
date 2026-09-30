using System;
using LiteSim;
using RoomServer.Runtime;

namespace RoomServer.Application
{
    /// <summary>
    /// 宿主侧控制消息封装。
    ///
    /// <para>
    /// <see cref="RoomCommand"/> 只表示 Runtime 能理解的纯命令；这个封装补上
    /// 宿主路由所需的房间、连接和会话代次。代次暂由装配方提供，等 Session
    /// 引入连接复用防护后可用来丢弃迟到消息；消息本身不持有可变的 Session 引用。
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

        public RoomControlEnvelope(string roomId, int connectionId, int playerId,
            long sessionEpoch, long receivedAtMs, in RoomCommand command)
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
        }

        public static RoomControlEnvelope FromCommand(string roomId, int connectionId,
            int playerId, long sessionEpoch, long receivedAtMs, in RoomCommand command)
            => new RoomControlEnvelope(roomId, connectionId, playerId, sessionEpoch,
                receivedAtMs, command);

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
    /// 宿主侧出站封装。
    ///
    /// <para>
    /// 当前批次仍可在同一 Owner 上直接应用 <see cref="RoomOutput"/>；此类型先把
    /// 输出与房间代次、序号及来源连接绑定，为后续 Worker → Host 出站队列留下稳定
    /// 边界。它不持有 Session 引用，也不把 Transport 放进 Runtime。
    /// </para>
    /// </summary>
    public readonly struct RoomOutboundEnvelope
    {
        public string RoomId { get; }
        public long RoomGeneration { get; }
        public long Sequence { get; }
        public int OriginConnectionId { get; }
        public long OriginSessionEpoch { get; }
        public RoomOutput Output { get; }

        public RoomOutboundEnvelope(string roomId, long roomGeneration, long sequence,
            int originConnectionId, long originSessionEpoch, RoomOutput output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            RoomId = roomId;
            RoomGeneration = roomGeneration;
            Sequence = sequence;
            OriginConnectionId = originConnectionId;
            OriginSessionEpoch = originSessionEpoch;
            Output = output;
        }

        public override string ToString()
            => $"room={RoomId ?? "<unbound>"} gen={RoomGeneration} seq={Sequence} output={Output.GetType().Name}";
    }
}
