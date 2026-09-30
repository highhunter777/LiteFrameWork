using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;

namespace RoomServer
{
    /// <summary>
    /// 固定数量的房间工作线程池。每个 roomId 始终映射到同一个 mailbox，
    /// 因而同一房间内的工作项不会并发执行；mailbox 有界，过载时由调用方处理拒绝。
    /// </summary>
    public sealed class RoomWorkerPool : IDisposable
    {
        private sealed class WorkItem
        {
            public string RoomId;
            public Action Callback;
        }

        private sealed class Worker
        {
            public readonly BlockingCollection<WorkItem> Mailbox;
            public readonly Thread Thread;
            public bool Started;

            public Worker(int capacity, Action<WorkItem> run, int index)
            {
                Mailbox = new BlockingCollection<WorkItem>(capacity);
                Thread = new Thread(() =>
                {
                    foreach (var item in Mailbox.GetConsumingEnumerable()) run(item);
                })
                {
                    IsBackground = true,
                    Name = "RoomServer.Worker." + index
                };
            }
        }

        private readonly Worker[] _workers;
        private readonly Action<Exception> _onWorkFailure;
        private readonly object _lifecycleGate = new object();
        private int _started;
        private int _stopped;
        private bool _discardPending;

        public int WorkerCount { get { return _workers.Length; } }
        public int MailboxCapacity { get; }
        public bool IsStarted { get { return Volatile.Read(ref _started) != 0; } }
        public bool IsStopped { get { return Volatile.Read(ref _stopped) != 0; } }

        public RoomWorkerPool(int workerCount = 1, int mailboxCapacity = 1024, Action<Exception> onWorkFailure = null)
        {
            if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
            if (mailboxCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(mailboxCapacity));
            MailboxCapacity = mailboxCapacity;
            _onWorkFailure = onWorkFailure;
            _workers = new Worker[workerCount];
            for (var i = 0; i < workerCount; i++)
                _workers[i] = new Worker(mailboxCapacity, Execute, i);
        }

        /// <summary>启动固定工作线程。重复调用没有副作用。</summary>
        public void Start()
        {
            Exception startFailure = null;
            lock (_lifecycleGate)
            {
                if (_stopped != 0) throw new InvalidOperationException("Worker pool has already stopped.");
                if (_started != 0) return;
                try
                {
                    foreach (var worker in _workers)
                    {
                        worker.Thread.Start();
                        worker.Started = true;
                    }
                    Volatile.Write(ref _started, 1);
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref _stopped, 1);
                    foreach (var worker in _workers) worker.Mailbox.CompleteAdding();
                    startFailure = ex;
                }
            }

            if (startFailure != null)
            {
                foreach (var worker in _workers)
                {
                    if (worker.Started) worker.Thread.Join();
                }
                ExceptionDispatchInfo.Capture(startFailure).Throw();
            }
        }

        /// <summary>
        /// 将工作项路由到 hash(roomId) % WorkerCount 对应 mailbox。
        /// 未启动、已停止、mailbox 已满时返回 false。
        /// </summary>
        public bool TryEnqueue(string roomId, Action callback)
        {
            if (string.IsNullOrWhiteSpace(roomId)) throw new ArgumentException("roomId is required.", nameof(roomId));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            lock (_lifecycleGate)
            {
                if (_started == 0 || _stopped != 0) return false;
                var worker = _workers[GetWorkerIndex(roomId)];
                return worker.Mailbox.TryAdd(new WorkItem { RoomId = roomId, Callback = callback });
            }
        }

        /// <summary>获取稳定路由索引。使用固定 FNV-1a，避免 string.GetHashCode 的进程随机化。</summary>
        public int GetWorkerIndex(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) throw new ArgumentException("roomId is required.", nameof(roomId));
            unchecked
            {
                uint hash = 2166136261u;
                var bytes = Encoding.UTF8.GetBytes(roomId);
                for (var i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 16777619u;
                }
                return (int)(hash % (uint)WorkerCount);
            }
        }

        /// <summary>
        /// 停止接收并等待工作线程退出。drain=true 会执行已接受项；false 会丢弃 mailbox 中尚未执行的项。
        /// 超时后可再次调用以继续等待。从工作线程内调用时只发出停止信号，由外部调用方等待退出。
        /// </summary>
        public void Stop(bool drain = true, TimeSpan? timeout = null)
        {
            var wait = timeout ?? Timeout.InfiniteTimeSpan;
            if (wait < TimeSpan.Zero && wait != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            lock (_lifecycleGate)
            {
                if (_stopped == 0)
                {
                    Volatile.Write(ref _stopped, 1);
                    _discardPending = !drain;
                    foreach (var worker in _workers)
                    {
                        worker.Mailbox.CompleteAdding();
                        if (!drain)
                            while (worker.Mailbox.TryTake(out _)) { }
                    }
                }
            }

            // A worker cannot join itself. It also must not wait for another worker that may
            // be stopping concurrently, since their callbacks could otherwise join each other.
            foreach (var worker in _workers)
                if (worker.Thread == Thread.CurrentThread) return;

            var timer = wait == Timeout.InfiniteTimeSpan ? null : Stopwatch.StartNew();
            foreach (var worker in _workers)
            {
                if (!worker.Started) continue;
                var remaining = Timeout.Infinite;
                if (timer != null)
                {
                    var left = wait - timer.Elapsed;
                    remaining = left <= TimeSpan.Zero ? 0 : (int)Math.Min(int.MaxValue, Math.Ceiling(left.TotalMilliseconds));
                }
                if (!worker.Thread.Join(remaining)) throw new TimeoutException("Worker pool did not stop within the requested timeout.");
            }
        }

        public int PendingCount
        {
            get
            {
                var count = 0;
                foreach (var worker in _workers) count += worker.Mailbox.Count;
                return count;
            }
        }

        private void Execute(WorkItem item)
        {
            lock (_lifecycleGate)
                if (_discardPending) return;

            try { item.Callback(); }
            catch (Exception ex)
            {
                if (_onWorkFailure != null)
                {
                    try { _onWorkFailure(ex); }
                    catch { /* 监控回调不能杀死房间工作线程。 */ }
                }
            }
        }

        public void Dispose() { Stop(drain: true); }
    }
}
