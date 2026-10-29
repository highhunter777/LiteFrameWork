using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 公共战斗事件的 **Sim 记录 ↔ wire** 单向映射单源（《状态同步专项设计》§5.2 CombatEvent 面）。
    ///
    /// **为什么单列**：wire 映射是协议层职责——房间内核（<c>RoomServer.Runtime</c>）只见
    /// <see cref="CombatEventRecord"/> 纯数据（R11 纯度：禁 proto），客户端也不自己手抄字段。
    /// 服务端广播、重连响应、本地服三条路都走本件，字段清单只有这一份。
    ///
    /// **泛化保证**：<c>kind</c> 是判别字段——新增一种事件只加一个 <see cref="FrameEventKind"/> 值，
    /// 本类与 proto 都不改（未知 kind 走 <see cref="TryFromProto"/> 的 false 分支丢弃，
    /// 不会解析错位）。这是它相对旧"每型一个 message"的核心收益。
    /// </summary>
    public static class CombatEventCodec
    {
        public static Proto.CombatEventEntry ToProto(in CombatEventRecord e)
        {
            return new Proto.CombatEventEntry
            {
                Kind = (int)e.Kind,
                A = e.A,
                B = e.B,
                Frame = e.Frame,
                Value = e.Value,
                X = e.Pos.X,
                Y = e.Pos.Y,
                Z = e.Pos.Z,
            };
        }

        /// <summary>wire → Sim 记录。未知 kind（未来端/坏包）返回 false 由调用方丢弃——
        /// 不猜、不改写，避免"看不懂的事件被当成 Hit 播出去"。</summary>
        public static bool TryFromProto(Proto.CombatEventEntry r, out CombatEventRecord e)
        {
            e = default;
            if (r == null) return false;
            if (!IsKnownKind(r.Kind)) return false;
            e = new CombatEventRecord((FrameEventKind)r.Kind, r.A, r.B, r.Frame, r.Value,
                new SimVector3(r.X, r.Y, r.Z));
            return true;
        }

        /// <summary>该 kind 是否为当前端认识的词汇（<see cref="FrameEventKind"/> 全域）。</summary>
        public static bool IsKnownKind(int kind)
            => kind >= (int)FrameEventKind.Fire && kind <= (int)FrameEventKind.ObstacleHit;

        /// <summary>
        /// 窗口 → 事件批（**全窗口**，升序）。装不下预算时**留新弃旧**并回报被裁条数
        /// （<paramref name="dropped"/>），由调用方计数并降档——**绝不静默丢整包**
        /// （旧实现的教训：kcp2k 丢弃时只有一行 stderr，监控上完全隐形）。
        ///
        /// 裁剪方向的理由：表现面要的是"刚刚发生的事"；最旧的记录客户端多半已消费过。
        /// 窗口上界 24 条，线性裁剪足够（不引二分）。
        /// </summary>
        public static Proto.CombatEventBatch PackBatch(int frame, ICombatEventSource window, out int dropped)
        {
            dropped = 0;
            var batch = new Proto.CombatEventBatch { Frame = frame };
            if (window == null || window.Count == 0) return batch;

            for (int i = 0; i < window.Count; i++)
            {
                CombatEventRecord record = window.Get(i);      // readonly struct → 先取局部再 in 传
                batch.Records.Add(ToProto(in record));
            }

            while (batch.CalculateSize() > WireBudget.ProtoPayload && batch.Records.Count > 0)
            {
                batch.Records.RemoveAt(0);      // 弃最旧
                dropped++;
            }

            return batch;
        }
    }
}
