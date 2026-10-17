using System;
using LiteNet;
using LiteNet.Proto;
using LiteNet.Protocol;
using RoomServer.Application;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 重连入站族（结构批 6B 自 <see cref="ServerHost"/> 外提）：一次性票据校验 → 席位移交/重绑 →
    /// 版本确认 + 权威快照 + 输入历史。宿主保留会话记账、房间编排与发送（经
    /// <see cref="IInboundReconnectHost"/>/<see cref="IRoomGateway"/>/<see cref="IInboundDiagnostics"/>）。
    /// </summary>
    internal sealed class InboundReconnectHandlers
    {
        private sealed class ReconnectTicketRaceException : Exception
        {
        }

        private readonly IInboundReconnectHost _host;

        public InboundReconnectHandlers(IInboundReconnectHost host)
        {
            _host = host;
        }

        /// <summary>
        /// 重连（§5.6 首选路径 + §9.3 恢复顺序的服务器侧）：
        /// 一次性票据 → 校验（绑定房间号）→ 重绑席位（Restoring）→ 版本确认 + 权威快照 + 输入历史。
        /// 客户端恢复完成 ACK（RestoreComplete）到达前，席位停在 Restoring——管线抑制其增量广播。
        /// </summary>
        public void Handle(Session session, Proto.ReconnectRequest request, long now)
        {
            // 分层限流·IP 维度·入场：与 Join 共表。被限 → **静默丢弃**——
            // 重连洪水不放大（不回 ReconnectResponse），也不向探测者泄露票据窗口状态。
            if (!_host.RateLimit.TryAcquireIpEntry(_host.Transport.GetRemoteAddress(session.ConnectionId), now))
                return;

            if (_host.MailboxRouting)
            {
                HandleMailbox(session, request, now);
                return;
            }
            if (!TryConsumeTicket(request, out int playerId, out string roomId))
            {
                SendFailure(session, "票据无效或已过期");
                return;
            }
            RoomInstance instance = _host.Rooms.Find(roomId);
            if (instance == null)                              // 票据绑定房间号
            {
                SendFailure(session, "票据与房间不符");
                return;
            }

            RoomRuntime room = instance.Runtime;
            if (!room.TryGetSeat(playerId, out PlayerSession seat))
            {
                SendFailure(session, "席位不存在");
                return;
            }
            if (!room.Started)   // 重连只在 Running 有效（终态房间无增量可恢复）
            {
                SendFailure(session, "房间不在进行中");
                return;
            }

            // 会话重挂：旧连接（若还在）标记断开，新连接接管席位（§9.2 重连原子绑定新 Connection）
            if (seat.ConnectionId != RoomOutboundEnvelope.NoConnectionId && seat.ConnectionId != session.ConnectionId
                && _host.Sessions.TryGet(seat.ConnectionId, out Session old))
            {
                old.Disconnected = true;
            }
            _host.Rooms.Submit(instance, RoomCommand.Rebind(playerId, session.ConnectionId));   // 席位 → Restoring（增量广播抑制中）
            session.PlayerId = playerId;
            session.RoomId = roomId;
            instance.Seats[playerId] = session;

            // 重连响应里的快照是**独立探测**（不推进广播基线）：显式构造一份全量。
            // 分层顺序（§5.6/§9.3）：版本确认 → 公共全量（PackFull 槽位）→ 比赛状态（PackFull 内附）→ 本人私有状态 → 输入历史；
            // 客户端应用完毕发 RestoreComplete（§9.3 步骤 6），席位回 Active 后广播管线恢复投递。
            _host.Diagnostics.SendToSession(session, PacketType.ReconnectResponse,
                ReconnectResponder.Build(room, playerId, ServerHost.ServerBuildHash), reliable: true);
            _host.Ops.NoteReconnectServed();
        }

        /// <summary>
        /// Mailbox 路由下的重连 admission。先 peek 目标房间并检查控制 lane 容量，
        /// 再消费一次性票据；Rebind 经过 Control lane 后才提交 Session/Seats 与响应。
        /// 宿主 owner 形态在本调用内排空控制消息（响应先于 Runtime 重绑不可能发生）；
        /// Worker 执行形态由归属 Worker 校验席位/开局并回传响应（宿主在应用 SeatRebound 时更新映射）。
        /// </summary>
        private void HandleMailbox(Session session, Proto.ReconnectRequest request, long now)
        {
            if (!_host.Reconnects.TryPeek(request.OneTimeToken, out int peekPlayerId, out string peekRoomId))
            {
                SendFailure(session, "票据无效或已过期");
                return;
            }

            RoomInstance instance = _host.Rooms.Find(peekRoomId);
            if (instance == null)
            {
                SendFailure(session, "票据与房间不符");
                return;
            }
            if (instance.Mailbox.IsClosed)
            {
                _host.Diagnostics.NoteMailboxClosed();
                SendFailure(session, "房间控制队列已关闭");
                return;
            }
            if (instance.Mailbox.ControlCount >= instance.Mailbox.ControlCapacity)
            {
                _host.Diagnostics.NoteMailboxRejectedFull();
                SendFailure(session, "房间控制队列已满");
                return;
            }

            int playerId = peekPlayerId;
            string roomId = peekRoomId;

            // Worker 执行形态：席位/开局等房间状态由归属 Worker 校验（宿主不读 Runtime 状态），
            // 校验失败由 Worker 经 Outbound 回传拒绝响应；成功路径经 SeatRebound + Send 回传，
            // 宿主在应用回传时更新会话映射。此处只做宿主侧 admission（票据/队列容量）。
            if (_host.WorkerExecution)
            {
                RoomCommand workerRebind = RoomCommand.Rebind(playerId, session.ConnectionId);
                RoomMailboxEnqueueResult workerAdmission;
                try
                {
                    workerAdmission = instance.Mailbox.TryEnqueueControl(() =>
                        ConsumeTicketInLock(instance, session, request, playerId, roomId, workerRebind,
                            expectRebindResponse: true, now));
                }
                catch (ReconnectTicketRaceException)
                {
                    SendFailure(session, "票据无效或已过期");
                    return;
                }

                if (workerAdmission != RoomMailboxEnqueueResult.Accepted)
                {
                    if (workerAdmission == RoomMailboxEnqueueResult.RejectedFull)
                        _host.Diagnostics.NoteMailboxRejectedFull();
                    else
                        _host.Diagnostics.NoteMailboxClosed();
                    SendFailure(session, workerAdmission == RoomMailboxEnqueueResult.RejectedFull
                        ? "房间控制队列已满" : "房间控制队列已关闭");
                    return;
                }
                _host.Rooms.SchedulePump(instance);
                return;
            }

            RoomRuntime room = instance.Runtime;
            if (!room.TryGetSeat(peekPlayerId, out PlayerSession seat))
            {
                SendFailure(session, "席位不存在");
                return;
            }
            if (!room.Started)
            {
                SendFailure(session, "房间不在进行中");
                return;
            }
            int oldConnectionId = seat.ConnectionId;

            // 在 mailbox 的容量锁内消费票据：满载/关闭会在 factory 之前返回，
            // 因而不会吞掉仍可重试的一次性票据。当前 Host owner 串行处理入站，
            // TryPeek 与此处 TryConsume 之间不存在另一个 owner admission。
            RoomCommand command = RoomCommand.Rebind(playerId, session.ConnectionId);
            RoomMailboxEnqueueResult admission;
            try
            {
                admission = instance.Mailbox.TryEnqueueControl(() =>
                    ConsumeTicketInLock(instance, session, request, playerId, roomId, command,
                        expectRebindResponse: false, now));
            }
            catch (ReconnectTicketRaceException)
            {
                SendFailure(session, "票据无效或已过期");
                return;
            }

            if (admission != RoomMailboxEnqueueResult.Accepted)
            {
                if (admission == RoomMailboxEnqueueResult.RejectedFull)
                    _host.Diagnostics.NoteMailboxRejectedFull();
                else
                    _host.Diagnostics.NoteMailboxClosed();
                SendFailure(session, admission == RoomMailboxEnqueueResult.RejectedFull
                    ? "房间控制队列已满" : "房间控制队列已关闭");
                return;
            }

            // 由 Host owner 消费；无论立即还是延迟装配，重连响应都必须
            // 在 Runtime 完成 Rebind 后发送，避免客户端先收到快照再发生重绑。
            _host.Rooms.DrainMailbox(instance);

            // Runtime Rebind 已把 seat.ConnectionId 改成新连接；必须使用重绑前
            // 捕获的旧连接号，否则旧 Session 会继续被当作活跃连接。
            if (oldConnectionId != RoomOutboundEnvelope.NoConnectionId && oldConnectionId != session.ConnectionId
                && _host.Sessions.TryGet(oldConnectionId, out Session old))
                old.Disconnected = true;
            session.PlayerId = playerId;
            session.RoomId = roomId;
            instance.Seats[playerId] = session;

            _host.Diagnostics.SendToSession(session, PacketType.ReconnectResponse,
                ReconnectResponder.Build(room, playerId, ServerHost.ServerBuildHash), reliable: true);
            _host.Ops.NoteReconnectServed();
        }

        private bool TryConsumeTicket(Proto.ReconnectRequest request, out int playerId, out string roomId)
            => _host.Reconnects.TryConsume(request.OneTimeToken, out playerId, out roomId);

        /// <summary>
        /// 在 Mailbox 容量锁内消费票据并构造控制信封（Worker 形态与宿主 owner 形态**共用**）：
        /// 容量已满/已关闭时 factory 根本不会被调用，票据因而不会被吞掉（仍可重试）。
        /// 绑定不一致（peek 出的席位/房间 ≠ 真正消费到的）→ 抛竞态异常，由调用方回拒绝响应——
        /// 这条校验此前在两条路径上各写一遍，漏写即"绑了别处的票据静默通过"。
        /// </summary>
        private RoomControlEnvelope ConsumeTicketInLock(RoomInstance instance, Session session,
            Proto.ReconnectRequest request, int playerId, string roomId, RoomCommand command,
            bool expectRebindResponse, long now)
        {
            if (!_host.Reconnects.TryConsume(request.OneTimeToken, out int consumedPlayer, out string consumedRoom)
                || consumedPlayer != playerId
                || !string.Equals(consumedRoom, roomId, StringComparison.Ordinal))
                throw new ReconnectTicketRaceException();

            return RoomControlEnvelope.FromCommand(
                instance.RoomId, session.ConnectionId, playerId, session.Epoch,
                now, command, expectRebindResponse);
        }

        private void SendFailure(Session session, string reason)
        {
            _host.Diagnostics.SendToSession(session, PacketType.ReconnectResponse,
                new Proto.ReconnectResponse { Ok = false, Reason = reason }, reliable: true);
        }
    }
}
