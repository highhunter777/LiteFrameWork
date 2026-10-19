using LiteNet.Protocol;
using LiteSim;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 公共战斗事件面的 wire 出口（《状态同步专项设计》§5.2 CombatEvent 面；2026-10-18 批）。
    ///
    /// **职责**：把房间内核的 <see cref="CombatEventWindow"/> 打成 <see cref="Proto.CombatEventBatch"/>
    /// 并经独立包（<see cref="PacketType.CombatEvents"/>）下发。字段映射单源在
    /// <see cref="CombatEventCodec"/>（本类只管节拍、预算与降级，不碰字段）。
    ///
    /// **为什么独立成包**（事故结论，别再合回去）：三个旧窗口随每份快照全量携带，实测把快照顶到
    /// 1531B &gt; KCP 不可靠上限 1194B ⇒ kcp2k 静默丢弃整份快照 ⇒ 状态流中断。独立后：
    /// ① 状态面尺寸只随实体数变（可预测）；② 事件面有自己的预算与降级阶梯；③ 两条平面可独立演进。
    ///
    /// **每帧构建一次、发给全体席位**：事件批当前**不做 AOI 裁剪**（公共事件本就全房可见，且
    /// 裁剪会让"视野外发生的击杀在你进入视野时消失"）——同一份 payload 发给所有可播席位，
    /// 不按席位重建（与 <see cref="SnapshotPipeline"/> 的 per-seat 构建刻意不同，理由在带宽与语义）。
    ///
    /// **降级阶梯**（预算见 <see cref="WireBudget"/>，**永不静默丢**）：
    /// ① 超预算 → <see cref="CombatEventCodec.PackBatch"/> 留新弃旧裁到预算内（计入 <see cref="Trimmed"/>）；
    /// ② 仍装不下（单条就超——理论不达，容量 24 已按最贵记录推算）→ 整批跳过（计入 <see cref="Skipped"/>）。
    /// 两个计数进 Ops：长期非零说明 <see cref="CombatEventWindow.Capacity"/> 或老化窗要调。
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
        /// 到点下发：**每帧调一次**（在快照广播之后）。构建一次 → 发给全部可播席位。
        /// 席位表由构造注入（与 <see cref="SnapshotPipeline"/> 同一份数组——两种执行形态各自一致）。
        /// <paramref name="seatBroadcastable"/> 语义与 <see cref="SnapshotPipeline.BroadcastIfDue"/> 一致
        /// （null = 不抑制；ServerHost 传"席位 == Active"，重连恢复中的席位不投）。
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
        /// 重连**不需要**专门的事件面补齐：客户端在收到 <c>ReconnectResponse</c> 时把公共事件面
        /// <c>Reset()</c>（复位成"未初始化"），重连后的第一份周期事件批按"首份"处理——
        /// 只标记已见、不重播断线前的事件（与状态快照的首份语义一致）。
        /// 这样重连响应不必携带事件批：它走可靠通道且体积敏感，多带一个窗口只会让
        /// <c>ReconnectResponse</c> 也可能撞不可靠/消息上限。
        /// </summary>
        public void ResetForReconnect()
        {
            // 窗口本身不动（事件是房间级事实，别的客户端还要收）；只留一个语义钩子，
            // 让装配方在重连路径上有显式调用点（未来若需按席位过滤再在此扩展）。
        }
    }
}
