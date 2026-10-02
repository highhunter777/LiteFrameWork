using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>逻辑时轨标记：WorldClock 驱动——受暂停/时停/变速。</summary>
    public interface ILogicScheduler : IScheduler { }

    /// <summary>UI 时轨标记：UIClock 驱动——受暂停、不受时停。</summary>
    public interface IUIScheduler : IScheduler { }

    /// <summary>
    /// 调度器实现（契约 = <see cref="IScheduler"/>）：动作点/定时注册、到期触发、可取消。
    /// 决策落点：①推进量 = 注入时钟的 ScaledDelta（不自读 Time）；
    /// ③到期判定 = 注入时钟的 Now。回调安全：单个回调抛经 SafeCall 隔离，不炸调度器；
    /// 同帧到期按注册序 FIFO；回调内可再 Schedule/Cancel，新注册项下一 tick 才处理。
    /// 内部使用最小堆按到期时间排序，取消采用惰性删除，避免每帧 O(n) 扫描。
    /// </summary>
    public class GameScheduler : IScheduler
    {
        private sealed class ScheduledItem
        {
            public int Id;
            public float DueTime;
            public int ScheduledTick;
            public Action Callback;
            public bool Canceled;
        }

        private const float DueEpsilon = 1e-3f;

        private readonly IGameClock _clock;
        private readonly List<ScheduledItem> _heap = new List<ScheduledItem>(16);
        private readonly Dictionary<int, ScheduledItem> _itemsById = new Dictionary<int, ScheduledItem>(16);
        private int _nextId = 1;
        private int _tickSerial;
        private int _canceledPendingCleanup;

        public GameScheduler(IGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>注册定时动作（delay 用时钟域秒——受变速/暂停语义约束），返回取消 id。</summary>
        public int Schedule(float delaySeconds, Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (float.IsNaN(delaySeconds) || float.IsInfinity(delaySeconds))
                throw new ArgumentOutOfRangeException(nameof(delaySeconds), "delay 必须是有限数值");

            int id = AllocateId();
            var item = new ScheduledItem
            {
                Id = id,
                DueTime = _clock.Now + Math.Max(0f, delaySeconds),
                ScheduledTick = _tickSerial,
                Callback = callback,
            };

            // Schedule 当前 API 自动分配 Id；这里仍显式校验，防止 int 回绕后发生重复注册。
            if (!_itemsById.TryAdd(id, item))
                throw new InvalidOperationException($"调度 Id 重复注册: {id}");

            HeapPush(item);
            return id;
        }

        /// <summary>
        /// 取消指定调度。已取消但尚未从堆顶清理的项保持幂等；完全不存在的 Id 记录警告，便于定位调用方 bug。
        /// </summary>
        public void Cancel(int id)
        {
            if (!_itemsById.TryGetValue(id, out var item))
            {
                Log.Warning($"尝试取消不存在的调度 Id={id}", "Scheduler");
                return;
            }

            if (item.Canceled) return;
            item.Canceled = true;
            item.Callback = null;
            _canceledPendingCleanup++;
        }

        public void Tick(float realDelta)
        {
            float dt = _clock.ScaledDelta;                 // 冻结/变速单源：时钟层
            if (dt <= 0f) return;

            int currentTick = ++_tickSerial;
            float now = _clock.Now;
            List<ScheduledItem> due = null;
            List<ScheduledItem> deferred = null;

            // 先摘完本帧到期项，再执行回调。回调内新 Schedule 的 ScheduledTick 等于 currentTick，
            // 因此即使 delay=0 也会被延迟到下一 tick，保持原有回调安全语义。
            while (_heap.Count > 0 && _heap[0].DueTime <= now + DueEpsilon)
            {
                var item = HeapPop();
                if (item.Canceled)
                {
                    RemoveCanceledItem(item);
                    continue;
                }

                if (item.ScheduledTick >= currentTick)
                {
                    deferred = deferred ?? new List<ScheduledItem>(2);
                    deferred.Add(item);
                    continue;
                }

                _itemsById.Remove(item.Id);
                due = due ?? new List<ScheduledItem>(4);
                due.Add(item);
            }

            if (deferred != null)
                for (int i = 0; i < deferred.Count; i++) HeapPush(deferred[i]);

            if (due == null) return;
            for (int i = 0; i < due.Count; i++)
                SafeCall.Invoke(due[i].Callback, $"Schedule[{due[i].Id}]");
        }

        public string StatsName => "Scheduler";

        public void Snapshot(Dictionary<string, string> into)
        {
            into["待触发"] = (_itemsById.Count - _canceledPendingCleanup).ToString();
            into["已取消待清理"] = _canceledPendingCleanup.ToString();
        }

        private int AllocateId()
        {
            if (_itemsById.Count >= int.MaxValue - 1)
                throw new InvalidOperationException("调度器已达到最大并发任务数");

            int start = _nextId;
            int candidate = start;
            do
            {
                _nextId = candidate == int.MaxValue ? 1 : candidate + 1;
                if (!_itemsById.ContainsKey(candidate)) return candidate;
                candidate = _nextId;
            }
            while (candidate != start);

            throw new InvalidOperationException("无法分配唯一调度 Id");
        }

        private void RemoveCanceledItem(ScheduledItem item)
        {
            _itemsById.Remove(item.Id);
            _canceledPendingCleanup--;
        }

        private static bool IsEarlier(ScheduledItem left, ScheduledItem right)
        {
            if (left.DueTime < right.DueTime) return true;
            if (left.DueTime > right.DueTime) return false;
            return left.Id < right.Id;                    // 同刻按注册序 FIFO
        }

        private void HeapPush(ScheduledItem item)
        {
            _heap.Add(item);
            int index = _heap.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!IsEarlier(_heap[index], _heap[parent])) break;
                Swap(index, parent);
                index = parent;
            }
        }

        private ScheduledItem HeapPop()
        {
            var result = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            if (_heap.Count == 0) return result;

            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= _heap.Count) break;
                int right = left + 1;
                int child = right < _heap.Count && IsEarlier(_heap[right], _heap[left]) ? right : left;
                if (!IsEarlier(_heap[child], _heap[index])) break;
                Swap(index, child);
                index = child;
            }
            return result;
        }

        private void Swap(int left, int right)
        {
            var item = _heap[left];
            _heap[left] = _heap[right];
            _heap[right] = item;
        }
    }

    /// <summary>逻辑时轨调度器（WorldClock 域）。</summary>
    public sealed class LogicScheduler : GameScheduler, ILogicScheduler
    {
        public LogicScheduler(IGameClock clock) : base(clock) { }
    }

    /// <summary>UI 时轨调度器（UIClock 域）。</summary>
    public sealed class UIScheduler : GameScheduler, IUIScheduler
    {
        public UIScheduler(IGameClock clock) : base(clock) { }
    }
}
