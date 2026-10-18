using System;
using System.Collections.Generic;

namespace RoomServer.Application
{
    /// <summary>
    /// 运维（GM）动作种类——**只复用既有房间命令**（《云端GM通道专项设计》§3）。
    /// 刻意不新增 <c>RoomCommandKind</c>：RoomCommand 在双编共享源集内，改它会让 BuildHash 变化
    /// ⇒ 客户端必须重打包，与"客户端零改动"冲突；故"转移房主"一类需要新命令的动作一律不做。
    /// </summary>
    public enum GmActionKind
    {
        /// <summary>不等满员强制开局（走既有 <c>RoomCommand.StartEarly</c>，发起席位取 owner）。</summary>
        StartEarly = 0,

        /// <summary>运维终止房间（走既有 <c>RoomCommand.Shutdown(Operator)</c>——与进程排空同口径）。</summary>
        Abort = 1,

        /// <summary>踢席位（走既有 <c>RoomCommand.Disconnect</c> + 传输层断连）。</summary>
        Kick = 2,

        /// <summary>立即回灌待提交结算（<c>ServerHost.FlushSettlementOutbox</c>）。</summary>
        FlushSettlement = 3,

        /// <summary>设置房间 GM 规则位（联机测试房批；1 = 免死 / 2 = 无限弹药 / 0 = 关闭）。
        /// **只对测试房（带模板建、不进大厅）生效**——普通房一律拒绝，防"给正式局开挂"。</summary>
        SetRules = 4,
    }

    /// <summary>
    /// 一个已过鉴权的运维动作（纯数据）。由 HTTP 线程构造入队，**在权威循环内执行**
    /// （<c>ServerHost.Pump</c>）——HTTP 线程不得直接改权威状态。
    /// </summary>
    public readonly struct GmAction
    {
        public GmActionKind Kind { get; }

        /// <summary>目标房间号（<see cref="GmActionKind.FlushSettlement"/> 不使用）。</summary>
        public string RoomId { get; }

        /// <summary>目标席位（仅 <see cref="GmActionKind.Kick"/> 使用）。</summary>
        public int PlayerId { get; }

        /// <summary>规则位掩码（仅 <see cref="GmActionKind.SetRules"/> 使用：1 = 免死 / 2 = 无限弹药 / 0 = 关闭）。</summary>
        public int Flags { get; }

        private GmAction(GmActionKind kind, string roomId, int playerId, int flags)
        {
            Kind = kind;
            RoomId = roomId;
            PlayerId = playerId;
            Flags = flags;
        }

        public static GmAction StartEarly(string roomId) => new GmAction(GmActionKind.StartEarly, roomId, -1, 0);
        public static GmAction Abort(string roomId) => new GmAction(GmActionKind.Abort, roomId, -1, 0);
        public static GmAction Kick(string roomId, int playerId) => new GmAction(GmActionKind.Kick, roomId, playerId, 0);
        public static GmAction FlushSettlement() => new GmAction(GmActionKind.FlushSettlement, null, -1, 0);
        public static GmAction SetRules(string roomId, int flags) => new GmAction(GmActionKind.SetRules, roomId, -1, flags);
    }

    /// <summary>
    /// 运维动作的**入队前拒绝**（房间不存在 / 无 owner / 席位不存在）——在 HTTP 线程即可判定的前置条件。
    /// 判定不碰权威状态（只读房间表与席位投影），故不必进权威循环。
    /// </summary>
    public readonly struct GmReject
    {
        public int Status { get; }
        public string Message { get; }

        private GmReject(int status, string message)
        {
            Status = status;
            Message = message;
        }

        public static GmReject NotFound(string message) => new GmReject(404, message);
        public static GmReject Conflict(string message) => new GmReject(409, message);
        public static GmReject BadRequest(string message) => new GmReject(400, message);
    }

    /// <summary>
    /// 运维动作队列（跨线程投递）——照抄 <see cref="PendingDisconnectQueue"/> 的"锁 + 列表 + Flush"范式：
    /// 生产端是 HTTP 线程（只入队、立即返回），消费端是权威循环（<c>Pump</c>），
    /// 这样动作永远在权威线程内落到房间上，不受 Mailbox 路由开关（同步/入盒）影响。
    /// </summary>
    public sealed class GmActionQueue
    {
        private readonly object _gate = new object();
        private readonly List<GmAction> _items = new List<GmAction>();

        /// <summary>待执行条数（观测用）。</summary>
        public int Pending
        {
            get { lock (_gate) return _items.Count; }
        }

        /// <summary>入队（HTTP 线程）。空 roomId 的动作由调用方校验后传入。</summary>
        public void Enqueue(in GmAction action)
        {
            lock (_gate) _items.Add(action);
        }

        /// <summary>取出全部待执行动作并清空队列（**只应由权威循环调用**）。无动作时返回空数组（零分配）。</summary>
        public GmAction[] TakeAll()
        {
            lock (_gate)
            {
                if (_items.Count == 0) return Array.Empty<GmAction>();
                GmAction[] taken = _items.ToArray();
                _items.Clear();
                return taken;
            }
        }
    }
}
