using System;
using System.Collections.Generic;
using System.Threading;
using Google.Protobuf;
using LiteNet;
using LiteNet.Protocol;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 宿主向 <see cref="RoomWorkerDriver"/> 提供的**窄端口**（P4 外提契约）。
    ///
    /// 只列驱动体真正需要的东西（池/计数/时钟/版本哈希）——契约越窄，"外提"才不会变成
    /// "把 ServerHost 整体换个名字持有"（那是拿新 god class 换旧 god class）。
    /// 由 <see cref="ServerHost"/> 显式实现（不扩大其公共面）。
    /// </summary>
    internal interface IRoomWorkerHost
    {
        /// <summary>Worker 池（驱动体把"还有活"的房间重新入池）。</summary>
        RoomWorkerPool Pool { get; }

        /// <summary>Ops 计数（ACK 归类 / 输入接受）。</summary>
        Ops Ops { get; }

        /// <summary>单调毫秒（命令信封时间戳）。</summary>
        long NowMs { get; }

        /// <summary>buildHash（重连响应里的版本确认）。</summary>
        string BuildHash { get; }

        /// <summary>Worker 执行形态下已执行的房间命令数 +1。</summary>
        void NoteWorkerCommand();

        /// <summary>完成的房间驱动轮次 +1。</summary>
        void NoteWorkerPump();

        /// <summary>池内工作项异常隔离 +1。</summary>
        void NoteWorkerFailure();

        /// <summary>陈旧入站/回传计数 +1（迟到 Join、代次不符的输入）。</summary>
        void NoteMailboxStale();
    }

    /// <summary>
    /// 房间 Worker **驱动体**（P4 从 <c>ServerHost</c> 外提，约 210 行）：在归属 Worker 线程上消费
    /// 本房间 Control/Input 两条 lane（每步 Control 优先），执行 Runtime 命令与快照广播，
    /// 产出经 Outbound lane **回传宿主**（Outbound 归宿主消费——Worker 绝不读它）。
    ///
    /// **边界（这条线就是外提的依据）**——同一件事的三段，线程归属不同：
    /// - **本类 = Worker 线程上跑的东西**：驱动体（去重登记配对）＋命令执行＋席位侧效应＋回传入盒；
    /// - **留宿主 = 生产侧**：`ScheduleRoomPump`/`SubmitWorkerTick`（先入盒 + 再登记唤醒——
    ///   被 Join/输入/重连/排空等多条宿主路径调用，是"生产者协议"的另一半）；
    /// - **留宿主 = 回传应用侧**：`ApplyOutboundEnvelope` 族（宿主线程解析会话、编码并经 Transport 发送）。
    ///
    /// 混在一个类里正是 god class 的成因；按线程归属切开后，每条规则只有一个落点。
    ///
    /// **行为零变化**：本类由原宿主方法**逐行搬移**，只把宿主成员（`_ops`/`_nowMs`/计数/池/哈希）
    /// 改为经 <see cref="IRoomWorkerHost"/> 调用。公共面只有两个入口：<see cref="Drive"/>（驱动一体）
    /// 与 <see cref="Send"/>（广播管线的发送钩子）。
    /// </summary>
    internal sealed class RoomWorkerDriver
    {
        private readonly IRoomWorkerHost _host;

        public RoomWorkerDriver(IRoomWorkerHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// 房间 Worker 驱动体（装配到 <see cref="RoomInstance.DriveAction"/>）：
        /// 进入在途计数 → 驱动核心 → **在重新登记之后再复位登记位/在途计数**
        /// （宿主以"无待驱动且在途=0"判定房间静默，故先后顺序有语义）。
        /// </summary>
        public void Drive(RoomInstance room)
        {
            _host.NoteWorkerPump();
            room.EnterWorkerDrive();
            try
            {
                DriveCore(room);
            }
            finally
            {
                room.CompletePump();
            }
        }

        /// <summary>广播管线的发送钩子（装配期绑定；把一条发送意图入 Outbound 回传）。</summary>
        public void Send(RoomInstance room, IBroadcastSeat seat, PacketType type, IMessage message, bool reliable)
            => EnqueueSend(room, seat.ConnectionId, seat.SessionEpoch, type, message, reliable);

        /// <summary>
        /// 驱动核心：清登记 → 消费两条 lane（Control 优先）→ 仍有活则重新登记入池。
        /// "清标记 → 消费 → 复检后重登记"的配对保证**不丢唤醒、也不堆积工作项**
        /// （生产者协议的另一半在宿主 <c>ScheduleRoomPump</c>）。
        /// </summary>
        private void DriveCore(RoomInstance room)
        {
            while (true)
            {
                room.ClearPumpScheduled();

                while (true)
                {
                    if (room.Mailbox.TryDequeueControl(out RoomControlEnvelope control))
                    {
                        _host.NoteWorkerCommand();
                        ExecuteControl(room, control);
                        continue;
                    }
                    if (room.Mailbox.TryDequeueInput(out RoomInputEnvelope input))
                    {
                        _host.NoteWorkerCommand();
                        ExecuteInput(room, input);
                        continue;
                    }
                    break;
                }

                if (room.Mailbox.ControlCount == 0 && room.Mailbox.InputCount == 0) return;
                if (room.Mailbox.IsClosed) continue;   // Complete 后的残留（宿主 Dispose 收口）：本工作项内消费到空
                if (!room.TrySchedulePump()) return;
                if (!_host.Pool.TryEnqueue(room.RoomId, room.DriveAction))
                {
                    room.ClearPumpScheduled();
                    _host.NoteWorkerFailure();
                    return;
                }
                return;
            }
        }

        /// <summary>Worker 侧执行一条控制命令并回传输出（入盒前的会话/路由校验归宿主）。</summary>
        private void ExecuteControl(RoomInstance room, RoomControlEnvelope envelope)
        {
            RoomCommandKind kind = envelope.Command.Kind;
            if (kind == RoomCommandKind.Tick)
                room.ClearPendingTick();

            // 重连握手（Rebind）：席位/开局校验在 Worker 侧（宿主不读 Runtime 状态），走专用路径。
            if (kind == RoomCommandKind.Rebind && envelope.ExpectRebindResponse)
            {
                ExecuteReconnectRebind(room, envelope);
                return;
            }

            IReadOnlyList<RoomOutput> outputs = room.ExecuteCommand(envelope.Command);
            ApplyControlSideEffects(room, envelope, outputs, kind);   // 先扫输出（含 PlayerAdmitted 的席位绑定）
            EnqueueOutputs(room, envelope.ConnectionId, envelope.SessionEpoch, outputs);   // 再入盒回传

            if (kind == RoomCommandKind.Tick)
                Broadcast(room);
        }

        /// <summary>
        /// 重连换绑（Worker 侧）：校验席位存在且对局进行中 → 执行 Rebind → 回传
        /// SeatRebound（宿主更新会话映射/计 Ops）+ 成功响应；校验失败回传拒绝响应。
        /// 换绑重置本席位广播记账（新连接没有历史发送记录）。
        /// </summary>
        private void ExecuteReconnectRebind(RoomInstance room, RoomControlEnvelope envelope)
        {
            int playerId = envelope.PlayerId;
            PlayerSession seat = room.Runtime.SeatOf(playerId);
            string rejectReason = null;
            if (seat == null) rejectReason = "席位不存在";
            else if (!room.Runtime.Started) rejectReason = "房间不在进行中";

            if (rejectReason != null)
            {
                EnqueueSend(room, envelope.ConnectionId, envelope.SessionEpoch,
                    PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = rejectReason },
                    reliable: true);
                return;
            }

            int oldConnectionId = seat.ConnectionId;
            EnqueueOutputs(room, envelope.ConnectionId, envelope.SessionEpoch,
                room.ExecuteCommand(envelope.Command));

            // 落定顺序与宿主 owner 形态一致：映射/旧连接失效先于响应发送（同一 lane FIFO）。
            EnqueueOutbound(room, RoomOutboundEnvelope.FromSeatRebound(room.RoomId,
                room.NextOutboundSequence(), envelope.ConnectionId, envelope.SessionEpoch,
                new RoomSeatReboundItem(playerId, oldConnectionId, envelope.ConnectionId)));
            EnqueueSend(room, envelope.ConnectionId, envelope.SessionEpoch, PacketType.ReconnectResponse,
                ReconnectResponder.Build(room.Runtime, playerId, _host.BuildHash), reliable: true);

            BindSeat(room, playerId, envelope.ConnectionId, envelope.SessionEpoch, resetAccount: true);
        }

        /// <summary>
        /// Worker 侧执行一条输入：先用 **Runtime 席位（唯一真相）** 做迟到校验（连接/相位），
        /// 再执行；ACK 记账进本席位广播记账（与宿主 owner 形态同一语义、同一实现）。
        /// </summary>
        private void ExecuteInput(RoomInstance room, RoomInputEnvelope envelope)
        {
            PlayerSession seat = room.Runtime.SeatOf(envelope.PlayerId);
            if (seat == null || seat.ConnectionId != envelope.ConnectionId
                || seat.Phase == SeatPhase.Disconnected)
            {
                _host.NoteMailboxStale();
                return;
            }

            long acceptedBefore = room.Runtime.Gate.AcceptedCount;
            IReadOnlyList<RoomOutput> outputs = room.ExecuteCommand(envelope.Command);
            if (outputs.Count > 0)              // 输入命令当前不产输出；防御性保留回传路径
                EnqueueOutputs(room, envelope.ConnectionId, envelope.SessionEpoch, outputs);

            _host.Ops.CountAck(room.WorkerSeats[envelope.PlayerId].Broadcast.TryAcceptAck(envelope.Command.Input.AckSnapshot));
            if (room.Runtime.Gate.AcceptedCount > acceptedBefore)
                _host.Ops.CountAcceptedInput();
        }

        /// <summary>命令执行后的席位侧效应（Worker 单线程）：进房/恢复刷新广播席位槽；断线刷新全部席位。</summary>
        private static void ApplyControlSideEffects(RoomInstance room, RoomControlEnvelope envelope,
            IReadOnlyList<RoomOutput> outputs, RoomCommandKind kind)
        {
            if (kind == RoomCommandKind.Disconnect)
            {
                // Runtime 断线会移除 connection→player 映射，只能全量刷新（断线低频，代价可忽略）。
                for (int p = 0; p < room.WorkerSeats.Length; p++) RefreshSeatFromRuntime(room, p);
                return;
            }
            if (kind != RoomCommandKind.AuthenticatedJoin) return;

            for (int i = 0; i < outputs.Count; i++)
            {
                if (outputs[i] is SignalOutput signal && signal.Signal is PlayerAdmitted admitted)
                    BindSeat(room, admitted.PlayerId, envelope.ConnectionId, envelope.SessionEpoch, resetAccount: true);
            }
        }

        /// <summary>Worker 侧广播：权威步进后拉取（§8.2 房间单线程；Restoring 席位抑制由 Runtime 相位判定）。</summary>
        private static void Broadcast(RoomInstance room)
        {
            if (!room.Runtime.Started) return;
            room.Pipeline.BroadcastIfDue(room.Runtime.AuthSim.Frame, room.Runtime.AuthSim,
                room.Runtime.Gate, room.Runtime.EntityIdOf, room.SeatBroadcastable);
        }

        /// <summary>把 Runtime 纯输出包成回传项入 Outbound（来源=产出它的命令连接/代次）。</summary>
        private void EnqueueOutputs(RoomInstance room, int originConnectionId, long originSessionEpoch,
            IReadOnlyList<RoomOutput> outputs)
        {
            // 产出缓冲归房间所有（<see cref="RoomInstance.ExecuteCommand"/> 下次执行前自清），
            // 本方法只读不回写——传引用进来清空会让"谁拥有缓冲"重新变成口头约定。
            for (int i = 0; i < outputs.Count; i++)
                EnqueueOutbound(room, RoomOutboundEnvelope.FromOutput(room.RoomId,
                    room.NextOutboundSequence(), originConnectionId, originSessionEpoch, outputs[i]));
        }

        /// <summary>发送意图入 Outbound（快照/响应；宿主解析会话、编码并经 Transport 发送）。</summary>
        private void EnqueueSend(RoomInstance room, int connectionId, long sessionEpoch,
            PacketType type, IMessage message, bool reliable)
        {
            if (connectionId == RoomOutboundEnvelope.NoConnectionId) return;   // 未绑定连接：没有发送对象
            EnqueueOutbound(room, RoomOutboundEnvelope.FromSend(room.RoomId, room.NextOutboundSequence(),
                new RoomSendItem(connectionId, sessionEpoch, type, message, reliable)));
        }

        /// <summary>
        /// 回传入盒（有界，满/关闭显式计数不静默丢）。快照的发送记账已在管线内前推——
        /// 入盒被拒的帧按"已发出"计（背压偏保守：宁可多压不放松）。
        /// </summary>
        private static void EnqueueOutbound(RoomInstance room, in RoomOutboundEnvelope envelope)
        {
            if (room.Mailbox.EnqueueOutbound(envelope) != RoomMailboxEnqueueResult.Accepted)
                room.NoteOutboundRejected();
        }

        /// <summary>绑定广播席位槽到连接（进房/换绑；代次随命令信封）。</summary>
        private static void BindSeat(RoomInstance room, int playerId, int connectionId,
            long sessionEpoch, bool resetAccount)
        {
            RoomBroadcastSeat view = room.WorkerSeats[playerId];
            view.ConnectionId = connectionId;
            view.SessionEpoch = sessionEpoch;
            view.Disconnected = false;
            if (resetAccount) view.Broadcast.Reset();
        }

        /// <summary>从 Runtime 席位刷新广播席位槽的连接事实（断线等；连接未变时代次保留）。</summary>
        private static void RefreshSeatFromRuntime(RoomInstance room, int playerId)
        {
            PlayerSession seat = room.Runtime.SeatOf(playerId);
            RoomBroadcastSeat view = room.WorkerSeats[playerId];
            view.ConnectionId = seat?.ConnectionId ?? RoomOutboundEnvelope.NoConnectionId;
            view.Disconnected = seat == null || seat.Phase == SeatPhase.Disconnected;
        }
    }
}
