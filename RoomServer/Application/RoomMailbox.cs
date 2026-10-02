using System;
using System.Collections.Generic;

namespace RoomServer.Application
{
    /// <summary>Room mailbox 的三条逻辑队列。每条队列独立计数与容量。</summary>
    public enum RoomMailboxLane
    {
        Input = 0,
        Control = 1,
        Outbound = 2,
    }

    /// <summary>
    /// 入盒结果必须由调用方处理；有界队列满时绝不静默丢弃。
    /// </summary>
    public enum RoomMailboxEnqueueResult
    {
        Accepted = 0,
        RejectedFull = 1,
        Closed = 2,
    }

    /// <summary>三条队列当前元素数的原子快照。</summary>
    public readonly struct RoomMailboxCounts
    {
        public int Input { get; }
        public int Control { get; }
        public int Outbound { get; }
        public int Total => Input + Control + Outbound;

        internal RoomMailboxCounts(int input, int control, int outbound)
        {
            Input = input;
            Control = control;
            Outbound = outbound;
        }

        public override string ToString()
            => $"input={Input} control={Control} outbound={Outbound} total={Total}";
    }

    /// <summary>
    /// 从 RoomMailbox 取出的统一消息包装。
    ///
    /// <para>
    /// <see cref="RoomMailbox{TInput,TControl,TOutbound}.TryDequeue"/> 采用严格优先级：
    /// Control → Input → Outbound；同一 lane 内保持 FIFO。三个载荷属性只有对应 lane
    /// 有意义，其他属性为该类型的 default 值。
    /// </para>
    /// </summary>
    public readonly struct RoomMailboxMessage<TInput, TControl, TOutbound>
    {
        public RoomMailboxLane Lane { get; }
        public TInput Input { get; }
        public TControl Control { get; }
        public TOutbound Outbound { get; }

        private RoomMailboxMessage(RoomMailboxLane lane, TInput input, TControl control, TOutbound outbound)
        {
            Lane = lane;
            Input = input;
            Control = control;
            Outbound = outbound;
        }

        internal static RoomMailboxMessage<TInput, TControl, TOutbound> FromInput(TInput value)
            => new RoomMailboxMessage<TInput, TControl, TOutbound>(RoomMailboxLane.Input, value, default, default);

        internal static RoomMailboxMessage<TInput, TControl, TOutbound> FromControl(TControl value)
            => new RoomMailboxMessage<TInput, TControl, TOutbound>(RoomMailboxLane.Control, default, value, default);

        internal static RoomMailboxMessage<TInput, TControl, TOutbound> FromOutbound(TOutbound value)
            => new RoomMailboxMessage<TInput, TControl, TOutbound>(RoomMailboxLane.Outbound, default, default, value);
    }

    /// <summary>
    /// 每房间的有界 Mailbox 核心。
    ///
    /// <para>
    /// 三条 lane 物理隔离容量：输入包突发不会挤掉控制命令或出站；控制 lane 出队拥有最高
    /// 优先级。类本身只负责线程安全的排队，不启动线程、不等待、不接 Transport，因而可以在
    /// 纯 .NET 单测中独立验收。Worker 负责在自己的线程上消费并驱动 RoomRuntime。
    /// </para>
    ///
    /// <para>FIFO 语义是每条 lane 内 FIFO；不同 lane 之间按 Control、Input、Outbound 优先级出队。</para>
    /// </summary>
    public sealed class RoomMailbox<TInput, TControl, TOutbound> : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Queue<TInput> _inputs = new Queue<TInput>();
        private readonly Queue<TControl> _controls = new Queue<TControl>();
        private readonly Queue<TOutbound> _outbounds = new Queue<TOutbound>();
        private readonly int _inputCapacity;
        private readonly int _controlCapacity;
        private readonly int _outboundCapacity;
        private bool _closed;
        private long _acceptedCount;
        private long _dequeuedCount;
        private long _rejectedFullCount;

        public RoomMailbox(int inputCapacity, int controlCapacity, int outboundCapacity)
        {
            if (inputCapacity < 1) throw new ArgumentOutOfRangeException(nameof(inputCapacity), inputCapacity, "Input 容量必须为正");
            if (controlCapacity < 1) throw new ArgumentOutOfRangeException(nameof(controlCapacity), controlCapacity, "Control 容量必须为正");
            if (outboundCapacity < 1) throw new ArgumentOutOfRangeException(nameof(outboundCapacity), outboundCapacity, "Outbound 容量必须为正");
            _inputCapacity = inputCapacity;
            _controlCapacity = controlCapacity;
            _outboundCapacity = outboundCapacity;
        }

        public int InputCapacity => _inputCapacity;
        public int ControlCapacity => _controlCapacity;
        public int OutboundCapacity => _outboundCapacity;

        /// <summary>是否已停止接收新消息；已入队消息仍可排空。</summary>
        public bool IsClosed
        {
            get { lock (_gate) return _closed; }
        }

        /// <summary>总在队数（输入 + 控制 + 出站）。</summary>
        public int Count => Counts.Total;
        public int InputCount => Counts.Input;
        public int ControlCount => Counts.Control;
        public int OutboundCount => Counts.Outbound;

        /// <summary>出队后且已 Complete 时为 true。</summary>
        public bool IsDrained
        {
            get { lock (_gate) return _closed && _inputs.Count == 0 && _controls.Count == 0 && _outbounds.Count == 0; }
        }

        public RoomMailboxCounts Counts
        {
            get
            {
                lock (_gate) return new RoomMailboxCounts(_inputs.Count, _controls.Count, _outbounds.Count);
            }
        }

        /// <summary>累计成功入队数（不含 Full/Closed）。</summary>
        public long AcceptedCount { get { lock (_gate) return _acceptedCount; } }

        /// <summary>累计成功出队数。</summary>
        public long DequeuedCount { get { lock (_gate) return _dequeuedCount; } }

        /// <summary>累计因 lane 已满而拒绝的入队数。</summary>
        public long RejectedFullCount { get { lock (_gate) return _rejectedFullCount; } }

        public RoomMailboxEnqueueResult EnqueueInput(TInput value)
        {
            lock (_gate)
            {
                if (_closed) return RoomMailboxEnqueueResult.Closed;
                if (_inputs.Count >= _inputCapacity) { _rejectedFullCount++; return RoomMailboxEnqueueResult.RejectedFull; }
                _inputs.Enqueue(value);
                _acceptedCount++;
                return RoomMailboxEnqueueResult.Accepted;
            }
        }

        public RoomMailboxEnqueueResult EnqueueControl(TControl value)
        {
            lock (_gate)
            {
                if (_closed) return RoomMailboxEnqueueResult.Closed;
                if (_controls.Count >= _controlCapacity) { _rejectedFullCount++; return RoomMailboxEnqueueResult.RejectedFull; }
                _controls.Enqueue(value);
                _acceptedCount++;
                return RoomMailboxEnqueueResult.Accepted;
            }
        }

        /// <summary>
        /// 只有在 control lane 仍有容量时才创建并入队载荷。
        ///
        /// 宿主侧 admission 需要先验证/构造一次性控制载荷，但不能在队列已满时
        /// 先执行副作用再发现拒绝。工厂在同一把锁内、确认有容量后调用；队列满或
        /// 已关闭时工厂不会执行。
        /// </summary>
        public RoomMailboxEnqueueResult TryEnqueueControl(Func<TControl> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (_gate)
            {
                if (_closed) return RoomMailboxEnqueueResult.Closed;
                if (_controls.Count >= _controlCapacity)
                {
                    _rejectedFullCount++;
                    return RoomMailboxEnqueueResult.RejectedFull;
                }

                TControl value = factory();
                _controls.Enqueue(value);
                _acceptedCount++;
                return RoomMailboxEnqueueResult.Accepted;
            }
        }

        /// <summary>
        /// 丢弃控制 lane 中满足条件的迟到/失效消息并返回条数。
        /// 仅供 Host owner 在连接断开时清理该连接的排队命令；不跨 lane、不阻塞等待。
        /// </summary>
        public int DiscardControls(Func<TControl, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            lock (_gate)
            {
                if (_controls.Count == 0) return 0;
                int removed = 0;
                int count = _controls.Count;
                for (int i = 0; i < count; i++)
                {
                    TControl value = _controls.Dequeue();
                    if (predicate(value)) removed++;
                    else _controls.Enqueue(value);
                }
                return removed;
            }
        }

        public RoomMailboxEnqueueResult EnqueueOutbound(TOutbound value)
        {
            lock (_gate)
            {
                if (_closed) return RoomMailboxEnqueueResult.Closed;
                if (_outbounds.Count >= _outboundCapacity) { _rejectedFullCount++; return RoomMailboxEnqueueResult.RejectedFull; }
                _outbounds.Enqueue(value);
                _acceptedCount++;
                return RoomMailboxEnqueueResult.Accepted;
            }
        }

        /// <summary>停止接收新消息；当前队列内容保留，供 Worker 继续排空。</summary>
        public void Complete()
        {
            lock (_gate) _closed = true;
        }

        /// <summary>严格按 Control → Input → Outbound 优先级出队；lane 内 FIFO。</summary>
        public bool TryDequeue(out RoomMailboxMessage<TInput, TControl, TOutbound> message)
        {
            lock (_gate)
            {
                if (_controls.Count != 0)
                {
                    message = RoomMailboxMessage<TInput, TControl, TOutbound>.FromControl(_controls.Dequeue());
                    _dequeuedCount++;
                    return true;
                }
                if (_inputs.Count != 0)
                {
                    message = RoomMailboxMessage<TInput, TControl, TOutbound>.FromInput(_inputs.Dequeue());
                    _dequeuedCount++;
                    return true;
                }
                if (_outbounds.Count != 0)
                {
                    message = RoomMailboxMessage<TInput, TControl, TOutbound>.FromOutbound(_outbounds.Dequeue());
                    _dequeuedCount++;
                    return true;
                }
                message = default;
                return false;
            }
        }

        public bool TryDequeueInput(out TInput value)
        {
            lock (_gate)
            {
                if (_inputs.Count == 0) { value = default; return false; }
                value = _inputs.Dequeue();
                _dequeuedCount++;
                return true;
            }
        }

        public bool TryDequeueControl(out TControl value)
        {
            lock (_gate)
            {
                if (_controls.Count == 0) { value = default; return false; }
                value = _controls.Dequeue();
                _dequeuedCount++;
                return true;
            }
        }

        public bool TryDequeueOutbound(out TOutbound value)
        {
            lock (_gate)
            {
                if (_outbounds.Count == 0) { value = default; return false; }
                value = _outbounds.Dequeue();
                _dequeuedCount++;
                return true;
            }
        }

        public void Dispose() => Complete();
    }
}
