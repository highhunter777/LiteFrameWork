using LiteNet.Protocol;
using LiteSync.Events;
using LiteSim;
using Proto = LiteNet.Proto;

namespace LiteSync.Events
{
    /// <summary>
    /// 公共战斗事件的 **Sim 记录 ↔ wire** 单向映射单源（《状态同步专项设计》§5.2 CombatEvent 面）。
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
        /// 窗口 → 事件批（**全窗口**，升序）。
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
