using System;
using LiteNet.Diagnostics;
using LiteNet.Proto;
using RoomServer.Application;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// Join 入站族（结构批 6B 自 <see cref="ServerHost"/> 外提）：准入链 → 房间解析/创建 → 命令投递。
    /// 宿主保留房间构造/销毁与拒绝诊断（经 <see cref="IRoomGateway"/>/<see cref="IInboundDiagnostics"/>）。
    /// </summary>
    internal sealed class InboundJoinHandlers
    {
        private readonly IInboundJoinHost _host;

        /// <summary>票据拒绝分类计数（索引 = <see cref="JoinTicketRejection"/> 值）。
        /// 槽位数**由枚举推出**（枚举加分类时数组会静默截断/越界归零）。</summary>
        private readonly int[] _ticketRejections =
            new int[Enum.GetValues(typeof(JoinTicketRejection)).Length];

        public InboundJoinHandlers(IInboundJoinHost host)
        {
            _host = host;
        }

        /// <summary>
        /// Join 信令：token 非空 + **房间号一致** + buildHash 必须等于服务器版本（版本红线）
        /// → 投递 <see cref="RoomCommand.Join"/> → JoinAck（MatchStarted 由房主开局触发，见
        /// <see cref="InboundHandlers"/> 的 StartMatch 分支——满员不再自动开局）。
        /// 入场先过**分层限流**：IP 维度（本方法首段）+ 账号维度（验签通过后）。
        /// **安全能力现状**：装配 <see cref="IJoinTicketValidator"/> 后 token 走验签 + 六项绑定 + 重放窗口；
        /// 未装配时退回原型级非空校验（<see cref="Session.Principal"/> 为 null）。kcp2k V1.41 cookie
        /// 只解决 UDP 探测/放大防护，**不提供业务身份、机密性或完整性**——安全信封完成前本服务不得直接暴露公网。
        /// </summary>
        public void Handle(Session session, Proto.JoinRequest join, long now)
        {
            if (session.PlayerId >= 0 || session.JoinPending) return;         // 重复/排队中的 Join 忽略

            // 准入链（IP 频率 → 字段边界 → token → 版本 → 票据 → 账号频率）已独立为
            // JoinAdmissionGate：它是"消耗权威资源之前"的纯判定，不建房/不入队/不写 Session。
            // 排空红线留在宿主——它是**宿主状态**（§12 第 2 步"停止接受新进房"），不属于准入判据。
            //
            // 排空红线放在字段边界**之后**（原注释口径保留）：超长包已由入站层按 PacketRejects
            // 归类，不该在排空计数里再记一次。
            var decision = _host.Admission.Evaluate(in join, session.ConnectionId, now);
            if (!decision.Admitted)
            {
                // 票据拒绝带分类计数（Ops 周期行）；其余拒绝只进通用 Rejects。
                if (decision.TicketRejection.HasValue) CountTicketRejection(decision.TicketRejection.Value);
                if (_host.Draining && decision.Code == DiagCode.JoinRejectedAdmission) _host.Ops.NoteRejectWhileDraining();
                _host.Diagnostics.RejectJoin(session, in join, decision.Stage, decision.Code, decision.Reason);
                return;
            }

            if (_host.Draining)
            {
                _host.Ops.NoteRejectWhileDraining();      // Rejects 由 Reject() 记，此处只记排空专属分类
                _host.Diagnostics.RejectJoin(session, in join, DiagStage.Room, DiagCode.JoinRejectedAdmission, "服务排空中：不接受新进房");
                return;
            }

            // 已存在房间在入队前先做控制 lane admission 检查，避免队列满时消费
            // 一次性 Join nonce。新房间尚不存在，创建后 mailbox 为空，不会落入满拒分支。
            RoomInstance existingRoom = _host.Rooms.Find(join.RoomId);
            if (_host.MailboxRouting && existingRoom != null
                && (existingRoom.Mailbox.IsClosed
                    || existingRoom.Mailbox.ControlCount >= existingRoom.Mailbox.ControlCapacity))
            {
                if (existingRoom.Mailbox.IsClosed) _host.Diagnostics.NoteMailboxClosed();
                else _host.Diagnostics.NoteMailboxRejectedFull();
                _host.Diagnostics.RejectJoin(session, in join, DiagStage.Room, DiagCode.JoinRejectedAdmission, "房间控制队列已满");
                return;
            }

            JoinPrincipal validatedPrincipal = decision.Principal;

            // 房间解析/创建（§6"一个 roomId 只能映射一个独立 RoomActor"；重复 roomId 复用既有房间）
            // 多图批：票据盖章的请求行键（未装配验签器的原型形态 = null ⇒ 0）只作用于**新建房**；
            // 既有房间以**房事实**为准——请求非 0 且不一致 → 显式拒绝（不静默换图/静默忽略）。
            int requestedMapId = validatedPrincipal?.MapId ?? 0;
            // 开房参数批：票据盖章的请求期望人数——**只作用于新建房**（既有房间以房事实为准，
            // 见下方"请求值不参与既有房判定"注）；回执经 JoinAck.expected_players 交给客户端定容。
            int requestedExpectedPlayers = validatedPrincipal?.ExpectedPlayers ?? 0;
            RoomInstance room = existingRoom;
            bool createdRoom = false;
            if (room == null)
            {
                RoomRuntime created = _host.Rooms.Open(join.RoomId, requestedMapId, requestedExpectedPlayers);
                if (created == null)
                {
                    // 空房号 / 达容量上限 / 模板缺失 / 地图策略冲突或缺行——一律拒绝进房
                    // （不静默排队、不静默建成别的配置）
                    _host.Diagnostics.RejectJoin(session, in join, DiagStage.Room, DiagCode.JoinRejectedRoom,
                        string.IsNullOrEmpty(join.RoomId)
                            ? "房间号缺失"
                            : $"房间不可用：{join.RoomId}（在册 {_host.Rooms.RoomCount}/{_host.Rooms.MaxRooms}）");
                    return;
                }
                room = _host.Rooms.Find(join.RoomId);
                createdRoom = room != null;
            }
            else if (requestedMapId > 0 && room.Runtime != null && room.Runtime.MapId != requestedMapId)
            {
                _host.Diagnostics.RejectJoin(session, in join, DiagStage.Room, DiagCode.JoinRejectedRoom,
                    $"房间地图与票据请求不符：房 map_id={room.Runtime.MapId} 票据 map_id={requestedMapId}");
                return;
            }

            RoomCommand command = RoomCommand.Join(session.ConnectionId);
            if (!_host.MailboxRouting)
            {
                session.Principal = validatedPrincipal;
                session.BuildHash = join.BuildHash;
                _host.Rooms.Submit(room, command, joinContext: session);
                return;
            }

            JoinPrincipal previousPrincipal = session.Principal;
            string previousBuildHash = session.BuildHash;
            session.JoinPending = true;
            // 先完成队列准入，再写入可变 Session 身份字段；控制队列满不会吞票据/占房。
            if (!_host.Rooms.TrySubmit(room, command, joinContext: session))
            {
                session.JoinPending = false;
                session.Principal = previousPrincipal;
                session.BuildHash = previousBuildHash;
                if (createdRoom) _host.Rooms.RemoveEmpty(room);
                _host.Diagnostics.RejectJoin(session, in join, DiagStage.Room, DiagCode.JoinRejectedAdmission, "房间控制队列不可用");
            }
            else
            {
                session.Principal = validatedPrincipal;
                session.BuildHash = join.BuildHash;
            }
        }

        /// <summary>某拒绝分类的累计次数（Ops 观测；宿主公开面转发）。</summary>
        public int TicketRejections(JoinTicketRejection reason)
        {
            int i = (int)reason;
            return i >= 0 && i < _ticketRejections.Length ? _ticketRejections[i] : 0;
        }

        /// <summary>票据拒绝分类计数（同时进 Ops 周期行——越界分类并入 Miscellaneous）。</summary>
        private void CountTicketRejection(JoinTicketRejection reason)
        {
            int i = (int)reason;
            _ticketRejections[i >= 0 && i < _ticketRejections.Length ? i : 0]++;
            // 本数组按**全分类**计数（含 Malformed——供 ServerHost.TicketRejections 查询），
            // Ops 侧只观测四类 + 总数：映射单源在 Ops.NoteTicketRejection（原先散在这里的 switch，
            // 新增分类要同时改枚举/宿主 switch/Ops 三处）。
            _host.Ops.NoteTicketRejection(reason);
        }
    }
}
