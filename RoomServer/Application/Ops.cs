using System.Text;
using LiteSim;
using RoomServer.Runtime;

namespace RoomServer.Application
{
    /// <summary>
    /// Ops 指标（《M10实施指导》§2.5 登记项："输出形式 = 控制台周期打印（5s），指标集 = 帧号/房间数/快照均长/丢包率/和解率汇总"）。
    /// 计数由各处就地累加（闸门/房间/宿主），本类只做**汇总与格式化**（StringBuilder 复用）。
    /// 验收口径（§9 M10 行）："和解触发率全程可观测"= <see cref="MismatchReports"/> 与房间差分/回溯计数。
    /// 吞吐（本次打印与上次打印的差值）在未到打印点的查询里只返回**瞬时值**，不做窗口推算——
    /// 定位是运维观测行，不是精确计量器；精确差值由对跑脚本按相邻两行自行计算。
    /// </summary>
    public sealed class Ops
    {
        /// <summary>周期打印开关（对跑脚本可关；用例断言时不影响计数）。</summary>
        public bool PrintEnabled = true;
        public long LastPrintMs;

        public long InputPackets;
        public long AckObserved;
        public long MismatchReports;
        public int LastMismatchFrame = -1;
        public long Rejects;
        public long ReconnectsServed;

        // ---- Join 票据拒绝（§P0-6；《框架先行》§5-4）----
        /// <summary>票据拒绝总数（任何分类）。</summary>
        public long TicketRejected;
        /// <summary>签名不匹配（篡改）。</summary>
        public long TicketRejectedBadSignature;
        /// <summary>已过期。</summary>
        public long TicketRejectedExpired;
        /// <summary>重放（nonce 已消费）。</summary>
        public long TicketRejectedReplayed;
        /// <summary>未知 kid（密钥轮换后旧 kid 下线）。</summary>
        public long TicketRejectedUnknownKey;

        // ---- R1 会话容量与生命周期（§9.2）----
        /// <summary>会话容量上限拒绝的连接数（超限断开计数）。</summary>
        public long SessionsRejected;
        /// <summary>周期清理移除的会话数（累计）。</summary>
        public long SessionsCleaned;
        /// <summary>Match 状态迁移事件数（含幂等忽略与非法拒绝）。</summary>
        public long MatchStateChanges;
        /// <summary>SettlementReady 产出数。</summary>
        public long SettlementsReady;
        /// <summary>重连恢复完成数（SeatRestored：Restoring → Active；§9.3 步骤 6）。</summary>
        public long RestoresCompleted;

        // ---- R0 边界拒绝计数（《服务端总设计》§5 P0-3"统一拒绝并计数"）----
        /// <summary>坏包（未知类型/截断/proto 解析失败）拒绝计数。</summary>
        public long PacketRejects;
        /// <summary>超长入包（解析前长度上限）拒绝计数。</summary>
        public long PacketOversized;
        /// <summary>语义 ACK 违纪合计（回退/重复/超前/超窗——不释放水位，见 <see cref="Session.TryAcceptAck"/>）。</summary>
        public long AckRejected;
        /// <summary>ACK 回退/重复/负值拒绝计数。</summary>
        public long AckRejectedStale;
        /// <summary>ACK 超前（> 最近真实发送帧）拒绝计数。</summary>
        public long AckRejectedFuture;
        /// <summary>ACK 落在 ledger 窗外拒绝计数。</summary>
        public long AckRejectedEvicted;

        /// <summary>ACK 验证结果归类记账（会话只返回结果，计数归宿主）。</summary>
        public void CountAck(AckResult result)
        {
            switch (result)
            {
                case AckResult.RejectedStale: AckRejectedStale++; AckRejected++; break;
                case AckResult.RejectedFuture: AckRejectedFuture++; AckRejected++; break;
                case AckResult.RejectedEvicted: AckRejectedEvicted++; AckRejected++; break;
            }
        }

        private readonly StringBuilder _sb = new StringBuilder(512);

        /// <summary>周期汇总（帧号/房间/快照/输入/和解率/背压/回溯/节拍债——一行式，便于日志抓取）。</summary>
        public string Format(RoomRuntime room, SnapshotPipeline pipeline, SessionManager sessions, ServerLoop.LoopStats loop = null)
        {
            _sb.Clear();
            _sb.Append("[Ops] frame=").Append(room.AuthSim.Frame)
               .Append(" steps=").Append(room.StepsCount)
               .Append(" sessions=").Append(sessions.Count)
               .Append(" players=").Append(room.NextPlayerId)
               .Append(" | snap: sent=").Append(pipeline.SnapshotSent)
               .Append(" full=").Append(pipeline.SnapshotFullSent)
               .Append(" deltaSlots(last)=").Append(pipeline.Differ.LastDeltaCount)
               .Append(" | in: pkts=").Append(InputPackets)
               .Append(" acc=").Append(room.Gate.AcceptedCount)
               .Append(" drop(frame/dup/range/ack/btn)=")
               .Append(room.Gate.DroppedIllegalFrame).Append('/')
               .Append(room.Gate.DroppedDuplicateFrame).Append('/')
               .Append(room.Gate.DroppedOutOfRange).Append('/')
               .Append(room.Gate.DroppedAckSnapshot).Append('/')
               .Append(room.Gate.DroppedIllegalButtons)
               .Append(" | reconcile: reports=").Append(MismatchReports)
               .Append(" | lagcomp: fire=").Append(room.FireInputsProcessed)
               .Append(" comp=").Append(room.LagComp.CompensatedCount)
               .Append(" degr=").Append(room.LagComp.DegradedCount)
               .Append(" | bp: throttled=").Append(pipeline.BackpressureThrottled)
               .Append(" | match: phase=").Append(room.Phase)
               .Append(" transitions=").Append(MatchStateChanges)
               .Append(" settled=").Append(SettlementsReady)
               .Append(" restored=").Append(RestoresCompleted)
               .Append(" | edge: rejects=").Append(Rejects)
               .Append(" sessRej=").Append(SessionsRejected)
               .Append(" sessClean=").Append(SessionsCleaned)
               .Append(" pktBad=").Append(PacketRejects)
               .Append(" pktBig=").Append(PacketOversized)
               .Append(" ackBad=").Append(AckRejected)
               .Append("(stale=").Append(AckRejectedStale)
               .Append(" future=").Append(AckRejectedFuture)
               .Append(" evict=").Append(AckRejectedEvicted).Append(')')
               .Append(" tktBad=").Append(TicketRejected)
               .Append("(bad=").Append(TicketRejectedBadSignature)
               .Append(" exp=").Append(TicketRejectedExpired)
               .Append(" rply=").Append(TicketRejectedReplayed)
               .Append(" kid=").Append(TicketRejectedUnknownKey).Append(')');
            if (loop != null)
                _sb.Append(" | loop: ticks=").Append(loop.Ticks)
                   .Append(" debt=").Append(loop.DroppedTimeMs).Append("ms")
                   .Append(" abandoned=").Append(loop.CatchUpAbandoned);
            return _sb.ToString();
        }
    }
}
