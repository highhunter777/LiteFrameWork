using LiteNet.Protocol;
using LiteSync.Events;
using LiteSim;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 公共战斗事件面的 wire 出口（《状态同步专项设计》§5.2 CombatEvent 面；2026-10-18 批）。
    /// </summary>
    public sealed class CombatEventPipeline
    {
        private readonly ICombatEventSource _source;
        private readonly IBroadcastSeat[] _seats;

        /// <summary>已下发批次数。</summary>
        public long Sent;

        /// <summary>因超预算被裁掉的事件条数（<b>不是</b>整批丢弃——是留下新的、弃掉最旧的）。</summary>
        public long Trimmed;

        /// <summary>整批被跳过次数（理论上为 0：窗口容量已按最贵记录推算过预算）。</summary>
        public long Skipped;

        /// <summary>本批原始条数（Ops 观测：正常稳态应远小于 <see cref="CombatEventWindow.Capacity"/>）。</summary>
        public int LastBatchCount;

        /// <summary>本批 proto 载荷字节数（Ops 观测：与 <see cref="WireBudget.ProtoPayload"/> 的余量）。</summary>
        public int LastBatchBytes;

        /// <summary>发送出口（与 <see cref="SnapshotPipeline.SendTo"/> 同签名——装配方注入同一实现）。</summary>
        public System.Action<IBroadcastSeat, PacketType, Google.Protobuf.IMessage, bool> SendTo;

        /// <param name="source">事件窗口（只读端口——房间内核与协议层之间唯一的共享形状）。</param>
        /// <param name="seats">席位视图（**与 <see cref="SnapshotPipeline"/> 用同一份**：
        /// 宿主 owner 形态传 <c>Seats</c>、Worker 执行形态传 <c>WorkerSeats</c>。
        /// 两者必须是同一个数组实例——装配期定值，运行期不再改，避免"批发到错误席位表"这类静默错配）。</param>
        public CombatEventPipeline(ICombatEventSource source, IBroadcastSeat[] seats)
        {
            _source = source;
            _seats = seats ?? throw new System.ArgumentNullException(nameof(seats));
        }

        /// <summary>
        /// 到点下发：**每帧调一次**（在快照广播之后）。
        /// </summary>
        public void Publish(int frame, System.Func<int, bool> seatBroadcastable = null)
        {
            if (frame <= 0 || _seats.Length == 0) return;

            Proto.CombatEventBatch batch = CombatEventCodec.PackBatch(frame, _source, out int dropped);
            LastBatchCount = batch.Records.Count;
            LastBatchBytes = batch.CalculateSize();
            Trimmed += dropped;

            if (WireBudget.RequiresFallback(LastBatchBytes))
            {
                Skipped++;                                    // 单条就超预算：整批跳过并留痕（不静默）
                return;
            }

            for (int p = 0; p < _seats.Length; p++)
            {
                IBroadcastSeat seat = _seats[p];
                if (seat == null || seat.Disconnected) continue;
                if (seatBroadcastable != null && !seatBroadcastable(p)) continue;
                Sent++;
                if (SendTo != null) SendTo(seat, PacketType.CombatEvents, batch, false);
            }
        }

        /// <summary>
        /// 重连**不需要**专门的事件面补齐：客户端在收到 <c>ReconnectResponse</c> 时把公共事件面 <c>Reset()</c>（复位成"未初始化"）。
        /// </summary>
        public void ResetForReconnect()
        {
            // 窗口本身不动（事件是房间级事实，别的客户端还要收）；只留一个语义钩子，
            // 让装配方在重连路径上有显式调用点（未来若需按席位过滤再在此扩展）。
        }
    }
}
