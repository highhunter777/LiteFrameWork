using Google.Protobuf;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Transport;
using RoomServer.Application;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>入站分发与短处理器依赖的宿主面（结构批 6B；构造器注入，非逐调用端口）。</summary>
    internal interface IInboundCoreHost
    {
        /// <summary>进入一包处理：刷新宿主单调时钟并返回本跳毫秒。</summary>
        long BeginInbound();

        int MaxInboundPacketBytes { get; }

        SessionManager Sessions { get; }

        RateLimiter RateLimit { get; }

        Ops Ops { get; }

        bool MailboxRouting { get; }

        IRoomGateway Rooms { get; }

        IInboundDiagnostics Diagnostics { get; }
    }

    /// <summary>Join 处理器依赖的宿主面。</summary>
    internal interface IInboundJoinHost
    {
        JoinAdmissionGate Admission { get; }

        Ops Ops { get; }

        bool Draining { get; }

        bool MailboxRouting { get; }

        IRoomGateway Rooms { get; }

        IInboundDiagnostics Diagnostics { get; }
    }

    /// <summary>重连处理器依赖的宿主面。</summary>
    internal interface IInboundReconnectHost
    {
        IRoomTransport Transport { get; }

        ReconnectService Reconnects { get; }

        SessionManager Sessions { get; }

        RateLimiter RateLimit { get; }

        Ops Ops { get; }

        bool MailboxRouting { get; }

        bool WorkerExecution { get; }

        IRoomGateway Rooms { get; }

        IInboundDiagnostics Diagnostics { get; }
    }

    /// <summary>房间编排面（宿主保留构造/销毁/提交/排空；入站处理器只发意图）。</summary>
    internal interface IRoomGateway
    {
        RoomInstance Find(string roomId);

        /// <param name="templateId">票据盖章的房间模板名（联机测试房批；空 = 走 default_template）。
        /// 非空且模板不存在 ⇒ 返回 null（拒绝建房，不静默回落）。</param>
        RoomRuntime Open(string roomId, int requestedMapId, int requestedExpectedPlayers, string templateId = null);

        bool RemoveEmpty(RoomInstance room);

        void Submit(RoomInstance room, in RoomCommand cmd, Session joinContext = null);

        bool TrySubmit(RoomInstance room, in RoomCommand cmd, Session joinContext = null);

        void EnqueuePendingDisconnect(RoomInstance room, Session session);

        void SchedulePump(RoomInstance room);

        void DrainMailbox(RoomInstance room);

        int MaxRooms { get; }

        int RoomCount { get; }
    }

    /// <summary>入站诊断出口（发送/进房拒绝/邮箱计数归宿主单点）。</summary>
    internal interface IInboundDiagnostics
    {
        void SendToSession(Session session, PacketType type, IMessage message, bool reliable);

        void RejectJoin(Session session, in Proto.JoinRequest join, string stage, string code, string reason);

        void NoteMailboxClosed();

        void NoteMailboxRejectedFull();

        /// <summary>和解上报（现场包用；房间号可空，空则忽略）。</summary>
        void NoteMismatch(string roomId, int frame);
    }
}
