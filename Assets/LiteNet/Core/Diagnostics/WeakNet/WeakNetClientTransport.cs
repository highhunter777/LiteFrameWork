#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using LiteNet.Transport;

namespace LiteNet.Diagnostics
{
    /// <summary>
    /// 客户端弱网注入装饰器：可靠帧只做固定延迟且严格保序；不可靠帧做延迟/抖动/丢包/重复/乱序。
    /// 只依赖窄口 IClientTransport，供开发/编辑器/开发包装配；release 不编译。
    /// </summary>
    public sealed class WeakNetClientTransport : IClientTransport, IClientTransportErrorSource
    {
        private const int UnreliableQueueLimit = 1024;
        private const int ReorderWindowMs = 16;

        private sealed class PendingPacket
        {
            public byte[] Data;
            public bool Reliable;
            public long DueMs;
            public long Seq;
        }

        private readonly IClientTransport _inner;
        private readonly Func<WeakNetProfile> _profileProvider;
        private readonly Func<long> _nowMs;
        private readonly List<PendingPacket> _outgoingReliable = new List<PendingPacket>(32);
        private readonly List<PendingPacket> _outgoingUnreliable = new List<PendingPacket>(256);
        private readonly List<PendingPacket> _incomingReliable = new List<PendingPacket>(32);
        private readonly List<PendingPacket> _incomingUnreliable = new List<PendingPacket>(256);

        private WeakNetRandom _upRandom;
        private WeakNetRandom _downRandom;
        private uint _activeSeed;
        private long _nextSeq;
        private long _lastReliableOutDue;
        private long _lastReliableInDue;
        private bool _disposed;
        private bool _dispatching;

        private int _rollCount;
        private int _dropped;
        private int _droppedUp;
        private int _droppedDown;
        private int _duplicated;
        private int _reordered;
        private int _overflow;
        private int _released;

        public WeakNetClientTransport(IClientTransport inner, Func<WeakNetProfile> profileProvider, Func<long> nowMs)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _profileProvider = profileProvider ?? throw new ArgumentNullException(nameof(profileProvider));
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));

            _inner.OnConnected += HandleInnerConnected;
            _inner.OnData += HandleInnerData;
            _inner.OnDisconnected += HandleInnerDisconnected;
            if (_inner is KcpTransportClient kcp) kcp.OnError += HandleInnerError;
            if (_inner is SecureEnvelopeClientTransport secure) secure.OnError += HandleInnerError;
            EnsureRandom(CurrentProfile());
        }

        public bool Connected => _inner.Connected;

        public event Action OnConnected;
        public event Action<ArraySegment<byte>, bool> OnData;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        public WeakNetStats Stats => new WeakNetStats(
            _activeSeed, _rollCount,
            _outgoingReliable.Count + _outgoingUnreliable.Count + _incomingReliable.Count + _incomingUnreliable.Count,
            _dropped, _droppedUp, _droppedDown, _duplicated, _reordered, _overflow, _released);

        public void Connect(string address, int port)
        {
            ClearQueues();
            _inner.Connect(address, port);
        }

        public void Disconnect()
        {
            ClearQueues();
            _inner.Disconnect();
        }

        public void TickIncoming()
        {
            _inner.TickIncoming();
            ReleaseDue(_incomingReliable, incoming: true);
            ReleaseDue(_incomingUnreliable, incoming: true);
        }

        public void TickOutgoing()
        {
            ReleaseDue(_outgoingReliable, incoming: false);
            ReleaseDue(_outgoingUnreliable, incoming: false);
            _inner.TickOutgoing();
        }

        public void Send(ArraySegment<byte> data, bool reliable)
        {
            if (_disposed) return;
            WeakNetProfile profile = CurrentProfile();
            if (!profile.Enabled)
            {
                FlushOutgoing();
                _inner.Send(data, reliable);
                return;
            }

            EnsureRandom(profile);
            long now = _nowMs();
            byte[] copy = Copy(data);
            if (reliable)
            {
                long due = Math.Max(_lastReliableOutDue, now + Math.Max(0, profile.LatencyMs));
                _lastReliableOutDue = due;
                _outgoingReliable.Add(new PendingPacket { Data = copy, Reliable = true, DueMs = due, Seq = _nextSeq++ });
                return;
            }

            if (Roll(profile.LossPermille, _upRandom))
            {
                _dropped++;
                _droppedUp++;
                return;
            }
            bool duplicate = Roll(profile.DuplicatePermille, _upRandom);
            bool reorder = Roll(profile.ReorderPermille, _upRandom);
            if (duplicate) _duplicated++;
            if (reorder) _reordered++;

            long dueBase = now + DelayFor(profile, _upRandom, reorder);
            _outgoingUnreliable.Add(new PendingPacket { Data = copy, Reliable = false, DueMs = dueBase, Seq = _nextSeq++ });
            if (duplicate)
            {
                _outgoingUnreliable.Add(new PendingPacket { Data = copy, Reliable = false, DueMs = dueBase + 1, Seq = _nextSeq++ });
            }
            TrimUnreliable(_outgoingUnreliable);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearQueues();
            _inner.OnConnected -= HandleInnerConnected;
            _inner.OnData -= HandleInnerData;
            _inner.OnDisconnected -= HandleInnerDisconnected;
            if (_inner is KcpTransportClient kcp) kcp.OnError -= HandleInnerError;
            if (_inner is SecureEnvelopeClientTransport secure) secure.OnError -= HandleInnerError;
            _inner.Dispose();
        }

        private WeakNetProfile CurrentProfile()
        {
            WeakNetProfile profile = _profileProvider();
            return profile.IsValid ? profile : WeakNetProfile.None;
        }

        private void EnsureRandom(WeakNetProfile profile)
        {
            if (_upRandom != null && (profile.Seed == 0 || profile.Seed == _activeSeed)) return;
            uint seed = profile.Seed == 0 ? (uint)Environment.TickCount : profile.Seed;
            _activeSeed = seed;
            _upRandom = new WeakNetRandom(seed);
            _downRandom = new WeakNetRandom(seed ^ 0x85EBCA6Bu);
        }

        private bool Roll(int permille, WeakNetRandom random)
        {
            if (permille <= 0) return false;
            _rollCount++;
            return random.NextRange(0, 1000) < permille;
        }

        private int DelayFor(WeakNetProfile profile, WeakNetRandom random, bool reorder)
        {
            int jitter = 0;
            if (profile.JitterMs > 0)
            {
                _rollCount++;
                jitter = random.NextRange(-profile.JitterMs, profile.JitterMs + 1);
            }
            int delay = Math.Max(0, profile.LatencyMs + jitter);
            if (reorder)
            {
                _rollCount++;
                delay += 1 + random.NextRange(0, ReorderWindowMs);
            }
            return delay;
        }

        private void TrimUnreliable(List<PendingPacket> queue)
        {
            while (queue.Count > UnreliableQueueLimit)
            {
                int oldest = 0;
                for (int i = 1; i < queue.Count; i++)
                {
                    if (queue[i].DueMs < queue[oldest].DueMs) oldest = i;
                }
                queue.RemoveAt(oldest);
                _overflow++;
            }
        }

        private void ReleaseDue(List<PendingPacket> queue, bool incoming)
        {
            if (queue.Count == 0 || _dispatching) return;
            _dispatching = true;
            try
            {
                long now = _nowMs();
                queue.Sort((a, b) => a.DueMs != b.DueMs ? a.DueMs.CompareTo(b.DueMs) : a.Seq.CompareTo(b.Seq));
                int released = 0;
                while (released < queue.Count && queue[released].DueMs <= now) released++;
                if (released == 0) return;
                var batch = queue.GetRange(0, released);
                queue.RemoveRange(0, released);
                foreach (PendingPacket packet in batch)
                {
                    Dispatch(packet, incoming);
                }
            }
            finally
            {
                _dispatching = false;
            }
        }

        private void FlushOutgoing()
        {
            FlushAll(_outgoingReliable, incoming: false);
            FlushAll(_outgoingUnreliable, incoming: false);
        }

        private void FlushIncoming()
        {
            FlushAll(_incomingReliable, incoming: true);
            FlushAll(_incomingUnreliable, incoming: true);
        }

        private void FlushAll(List<PendingPacket> queue, bool incoming)
        {
            if (queue.Count == 0 || _dispatching) return;
            _dispatching = true;
            try
            {
                var batch = new List<PendingPacket>(queue);
                queue.Clear();
                foreach (PendingPacket packet in batch)
                {
                    Dispatch(packet, incoming);
                }
            }
            finally
            {
                _dispatching = false;
            }
        }

        private void Dispatch(PendingPacket packet, bool incoming)
        {
            if (incoming) OnData?.Invoke(new ArraySegment<byte>(packet.Data), packet.Reliable);
            else _inner.Send(new ArraySegment<byte>(packet.Data), packet.Reliable);
            _released++;
        }

        private void HandleInnerData(ArraySegment<byte> data, bool reliable)
        {
            if (_disposed) return;
            WeakNetProfile profile = CurrentProfile();
            if (!profile.Enabled)
            {
                FlushIncoming();
                OnData?.Invoke(data, reliable);
                return;
            }

            EnsureRandom(profile);
            long now = _nowMs();
            byte[] copy = Copy(data);
            if (reliable)
            {
                long due = Math.Max(_lastReliableInDue, now + Math.Max(0, profile.LatencyMs));
                _lastReliableInDue = due;
                _incomingReliable.Add(new PendingPacket { Data = copy, Reliable = true, DueMs = due, Seq = _nextSeq++ });
                return;
            }

            if (Roll(profile.LossPermille, _downRandom))
            {
                _dropped++;
                _droppedDown++;
                return;
            }
            bool duplicate = Roll(profile.DuplicatePermille, _downRandom);
            bool reorder = Roll(profile.ReorderPermille, _downRandom);
            if (duplicate) _duplicated++;
            if (reorder) _reordered++;
            long dueBase = now + DelayFor(profile, _downRandom, reorder);
            _incomingUnreliable.Add(new PendingPacket { Data = copy, Reliable = false, DueMs = dueBase, Seq = _nextSeq++ });
            if (duplicate)
            {
                _incomingUnreliable.Add(new PendingPacket { Data = copy, Reliable = false, DueMs = dueBase + 1, Seq = _nextSeq++ });
            }
            TrimUnreliable(_incomingUnreliable);
        }

        private void ClearQueues()
        {
            _outgoingReliable.Clear();
            _outgoingUnreliable.Clear();
            _incomingReliable.Clear();
            _incomingUnreliable.Clear();
            _lastReliableOutDue = 0;
            _lastReliableInDue = 0;
        }

        private static byte[] Copy(ArraySegment<byte> data)
        {
            byte[] copy = new byte[data.Count];
            if (data.Count > 0) Array.Copy(data.Array, data.Offset, copy, 0, data.Count);
            return copy;
        }

        private void HandleInnerConnected() => OnConnected?.Invoke();

        private void HandleInnerDisconnected()
        {
            ClearQueues();
            OnDisconnected?.Invoke();
        }

        private void HandleInnerError(string error) => OnError?.Invoke(error);
    }
}
#endif
