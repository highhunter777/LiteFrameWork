using System;
using LiteNet;
using LiteNet.Protocol;
using LiteSync.Input;
using LiteNet.Proto;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 入站消息面（结构批 6B 自 <see cref="ServerHost"/> 外提）：包体边界/包速率/会话触碰/解码分发。
    /// </summary>
    internal sealed class InboundHandlers
    {
        private readonly IInboundCoreHost _host;
        private readonly InboundJoinHandlers _join;
        private readonly InboundReconnectHandlers _reconnect;

        private readonly SimInputFrame[] _batchFrames = new SimInputFrame[ClientInputBatch.MaxFrames];
        private ClientInputBatch _inputBatch;

        public InboundHandlers(IInboundCoreHost host, InboundJoinHandlers join, InboundReconnectHandlers reconnect)
        {
            _host = host;
            _join = join;
            _reconnect = reconnect;
        }

        public void OnData(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            long now = _host.BeginInbound();

            // P0-3 包体硬边界：长度限制在 protobuf 解析**之前**（超长包不进解析器 = 不给"解析耗尽内存"留门）
            if (data.Count > _host.MaxInboundPacketBytes)
            {
                _host.Ops.NotePacketOversized();
                return;
            }

            // 分层限流·Session 维度·包速率：单热连接灌包被限速**静默丢弃**——
            // 不回应（对洪水响应即放大），计数在 <see cref="RateLimiter.RejectedSessionPackets"/>。
            if (!_host.RateLimit.TryAcquireSessionPacket(connectionId, now)) return;

            Session session = _host.Sessions.GetOrAddOnFirstPacket(connectionId, now);
            if (session == null) return;
            if (session.Disconnected) return;
            session.Touch(now);

            if (!PacketCodec.TryDecode(data, out PacketType type, out Google.Protobuf.IMessage message))
            {
                _host.Ops.NotePacketRejected();                                 // 未知类型/截断/坏 proto：计数（P0-3 统一拒绝并计数）
                return;
            }

            switch (type)
            {
                case PacketType.Join:
                    _join.Handle(session, (Proto.JoinRequest)message, now);
                    break;
                case PacketType.Input:
                    HandleInput(session, (InputMessage)message);
                    break;
                case PacketType.MismatchReport:
                    HandleMismatch(session, (Proto.MismatchReport)message);
                    break;
                case PacketType.ReconnectRequest:
                    _reconnect.Handle(session, (Proto.ReconnectRequest)message, now);
                    break;
                case PacketType.RestoreComplete:
                    HandleRestoreComplete(session);
                    break;
                case PacketType.Leave:
                    HandleLeave(session);
                    break;
                case PacketType.StartMatchRequest:
                    HandleStartMatchRequest(session);
                    break;
                case PacketType.Heartbeat:
                    HandleHeartbeat(session, (Proto.Heartbeat)message);
                    break;
            }
        }

        /// <summary>
        /// RTT/丢包探针回声（Unreliable）：**原样回显** client_time——客户端据此配对算 RTT/丢包千分比。
        /// 保活由 kcp2k 内建 Ping/Timeout 承担，本包不参与保活，故除回显外不做任何处置（限流在入站层）。
        /// </summary>
        private void HandleHeartbeat(Session session, Proto.Heartbeat heartbeat)
        {
            _host.Diagnostics.SendToSession(session, PacketType.Heartbeat,
                new Proto.Heartbeat { ClientTime = heartbeat.ClientTime }, reliable: false);
        }

        private void HandleInput(Session session, InputMessage msg)
        {
            if (session.PlayerId < 0) return;

            // 直投路径先做 ACK 验证；Mailbox 路径把 ACK 验证延后到 Host owner
            // 实际消费输入之后，避免队列满拒仍前推会话 ledger。
            if (!_host.MailboxRouting)
                _host.Ops.CountAck(session.TryAcceptAck(msg.AckSnapshot));

            // proto → 纯数据（App 职责；Runtime 不见 proto）。
            int raw = InputPacker.UnpackFrames(msg, _batchFrames);
            _inputBatch.Frame = msg.Frame;
            _inputBatch.AckSnapshot = msg.AckSnapshot;
            _inputBatch.ViewFrame = msg.ViewFrame;
            _inputBatch.Count = raw;
            _inputBatch.Frames = _batchFrames;

            // 输入按会话所在房间路由（§6：一个连接只属于一个房间）
            RoomInstance inputRoom = _host.Rooms.Find(session.RoomId);
            if (inputRoom == null) return;

            if (_host.MailboxRouting)
            {
                _host.Rooms.TrySubmit(inputRoom, RoomCommand.ClientInput(session.PlayerId, _inputBatch), session);
                return;
            }

            long acceptedBefore = inputRoom.Runtime.Gate.AcceptedCount;
            _host.Rooms.Submit(inputRoom, RoomCommand.ClientInput(session.PlayerId, _inputBatch));
            if (inputRoom.Runtime.Gate.AcceptedCount > acceptedBefore)   // 只统计被闸门接受的包
                _host.Ops.CountAcceptedInput();
        }

        /// <summary>和解上报：只汇总计数（Ops 输出和解触发率）。</summary>
        private void HandleMismatch(Session session, Proto.MismatchReport report)
        {
            if (session.PlayerId < 0) return;
            _host.Ops.NoteMismatch(report.Frame);
            _host.Diagnostics.NoteMismatch(session.RoomId, report.Frame);
        }

        /// <summary>恢复完成 ACK（§9.3 步骤 6）：席位 Restoring → Active——增量广播自此恢复该席位。</summary>
        private void HandleRestoreComplete(Session session)
        {
            if (session.PlayerId < 0) return;
            // 幂等：非 Restoring 时 Runtime 静默忽略。路由到会话所在房间。
            _host.Rooms.TrySubmit(_host.Rooms.Find(session.RoomId), RoomCommand.RestoreAck(session.PlayerId), session);
        }

        private void HandleLeave(Session session)
        {
            RoomInstance room = _host.Rooms.Find(session.RoomId);
            RoomCommand command = RoomCommand.Disconnect(session.ConnectionId);
            bool accepted = _host.Rooms.TrySubmit(room, command, session);
            if (accepted)
            {
                session.Disconnected = true;
            }
            else if (_host.MailboxRouting)
            {
                // Leave 与传输断线具有相同的控制面语义；满载时保留事实，
                // 等 owner 按 Control FIFO 重试，不静默丢失。
                session.Disconnected = true;
                _host.Rooms.EnqueuePendingDisconnect(room, session);
            }
        }

        /// <summary>
        /// 房主开局请求（开房裁决；**唯一开局入口**——满员不再自动开局）： 会话已入座且房间在册才投递 <see cref="RoomCommand.StartEarly"/>——owner 判定在 Runtime（"开房者=。）。
        /// </summary>
        private void HandleStartMatchRequest(Session session)
        {
            if (session.PlayerId < 0 || session.JoinPending) return;      // 未入座/进房中：无发起资格
            RoomInstance room = _host.Rooms.Find(session.RoomId);
            if (room == null) return;
            _host.Rooms.TrySubmit(room, RoomCommand.StartEarly(session.PlayerId), session);
        }
    }
}
