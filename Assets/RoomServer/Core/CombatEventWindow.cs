using System;
using LiteSim;

using LiteSync.Snapshot;
namespace RoomServer.Runtime
{
    /// <summary>
    /// 公共战斗事件窗口（房间级**纯数据**有界环；《状态同步专项设计》§5.2 CombatEvent 面）。
    /// </summary>
    public sealed class CombatEventWindow : ICombatEventSource
    {
        /// <summary>容量上界（条）。推算：最贵记录（Hit，带浮点位置）≈30B，24×30 ≈ 720B，
        /// 加槽位 + 帧头仍在不可靠预算（约 1158B proto）内——余量留给槽位与未来事件类型。</summary>
        public const int Capacity = 24;

        /// <summary>老化窗（帧）：1s @ <see cref="SimConfig.TickRate"/>。与 `SnapshotDiffer` 的关键状态
        /// 粘滞窗同口径——刻意复用同一时间常数，不发明第二个"多久算旧"。</summary>
        public const int StaleFrames = SimConfig.TickRate;

        private readonly CombatEventRecord[] _items = new CombatEventRecord[Capacity];
        private int _head;      // 最旧下标
        private int _count;

        /// <summary>淘汰累计（容量挤出 + 过期清理）——诊断面：>0 说明窗口被打满过，
        /// Ops 据此判断是否需要调大 Capacity 或缩短 StaleFrames。</summary>
        public int DroppedCount { get; private set; }

        /// <summary>当前条数。</summary>
        public int Count => _count;

        /// <summary>
        /// 记入一条战斗事件（上游产出点唯一入口）。
        /// </summary>
        public void Add(FrameEventKind kind, long a, long b, int frame, int value, in SimVector3 pos)
        {
            byte family = FamilyOf(kind);
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head + i) % _items.Length;
                ref CombatEventRecord existing = ref _items[idx];
                if (FamilyOf(existing.Kind) != family || existing.A != a || existing.B != b
                    || existing.Frame != frame) continue;      // lint-allow R3（整型键判等）

                // 聚合口径：值累加；同族内 Crit 视为"档位升级"（任一弹丸爆头即整条记为 Crit）。
                int mergedValue = existing.Value + value;
                FrameEventKind mergedKind = existing.Kind == FrameEventKind.Crit || kind == FrameEventKind.Crit
                    ? FrameEventKind.Crit
                    : existing.Kind;
                _items[idx] = new CombatEventRecord(mergedKind, existing.A, existing.B, existing.Frame,
                    mergedValue, in existing.Pos);
                return;
            }

            if (_count < _items.Length)
            {
                _items[(_head + _count) % _items.Length] = new CombatEventRecord(kind, a, b, frame, value, in pos);
                _count++;
                return;
            }

            // 满：淘汰最旧（老化之外的容量兜底；低频事件不会被高频事件挤掉，见类注释纪律 2）
            _items[_head] = new CombatEventRecord(kind, a, b, frame, value, in pos);
            _head = (_head + 1) % _items.Length;
            DroppedCount++;
        }

        /// <summary>聚合族：Hit 与 Crit 同族（伤害面）；其余各成一族。</summary>
        private static byte FamilyOf(FrameEventKind kind)
            => kind == FrameEventKind.Hit || kind == FrameEventKind.Crit ? (byte)1 : (byte)((int)kind + 2);

        /// <summary>清掉过期事件（`Frame &lt; frame - StaleFrames`）。**每个广播帧调用一次**——
        /// 惰性清理不可行：没有新事件时旧记录也要退场，否则窗口在停火后仍满载。
        /// 返回清掉的条数（诊断/Ops 用）。</summary>
        public int Prune(int frame)
        {
            int cutoff = frame - StaleFrames;
            int removed = 0;
            // 环内事件按帧号单调递增（Add 按权威帧顺序调用）——从头摘到第一条未过期即可停
            while (_count > 0 && _items[_head].Frame < cutoff)
            {
                _head = (_head + 1) % _items.Length;
                _count--;
                removed++;
            }
            DroppedCount += removed;
            return removed;
        }

        /// <summary>按下标取（0 = 最旧）。越界抛——调用方以 <see cref="Count"/> 为界。</summary>
        public CombatEventRecord Get(int index)
        {
            if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[(_head + index) % _items.Length];
        }

        /// <summary>清空（房间重置/离场）。不计入 <see cref="DroppedCount"/>——那是"被挤出"的语义。</summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }
}
