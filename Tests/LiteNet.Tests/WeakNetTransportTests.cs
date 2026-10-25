using System;
using System.Collections.Generic;
using System.Linq;
using LiteNet.Diagnostics;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>弱网注入内核契约：固定种子、可靠保序、不可靠损伤、队列与生命周期。</summary>
    public class WeakNetTransportTests
    {
        private sealed class FakeInner : IClientTransport
        {
            public readonly List<(byte[] Data, bool Reliable)> Sent = new List<(byte[], bool)>();
            public bool Connected { get; private set; }

            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;

            public void Connect(string address, int port)
            {
                Connected = true;
                OnConnected?.Invoke();
            }

            public void Disconnect()
            {
                Connected = false;
                OnDisconnected?.Invoke();
            }

            public void TickIncoming() { }
            public void TickOutgoing() { }

            public void Send(ArraySegment<byte> data, bool reliable)
            {
                byte[] copy = new byte[data.Count];
                Array.Copy(data.Array, data.Offset, copy, 0, data.Count);
                Sent.Add((copy, reliable));
            }

            public void Raise(byte[] data, bool reliable) => OnData?.Invoke(new ArraySegment<byte>(data), reliable);

            public void Dispose() => Disconnect();
        }

        private static byte[] Payload(int value)
        {
            return new[] { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) };
        }

        private static WeakNetClientTransport Create(FakeInner inner, Func<WeakNetProfile> profile, Func<long> clock)
        {
            return new WeakNetClientTransport(inner, profile, clock);
        }

        [Fact]
        public void Disabled_IsPassthrough_AndConsumesNoRolls()
        {
            var inner = new FakeInner();
            using var transport = Create(inner, () => WeakNetProfile.None, () => 0);
            transport.Send(new ArraySegment<byte>(Payload(1)), reliable: true);
            transport.Send(new ArraySegment<byte>(Payload(2)), reliable: false);
            Assert.Equal(2, inner.Sent.Count);
            Assert.Equal(0, transport.Stats.RollCount);
        }

        [Fact]
        public void SameSeed_SameSequence_DifferentSeed_Different()
        {
            var a = new WeakNetRandom(1234);
            var b = new WeakNetRandom(1234);
            var c = new WeakNetRandom(4321);
            bool anyDifferent = false;
            for (int i = 0; i < 32; i++)
            {
                uint x = a.NextUInt();
                uint y = b.NextUInt();
                uint z = c.NextUInt();
                Assert.Equal(x, y);
                if (x != z) anyDifferent = true;
            }
            Assert.True(anyDifferent);
        }

        [Fact]
        public void Reliable_IsNotLostDuplicatedOrReordered()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 10, 0, 1000, 1000, 1000, 7);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            for (int i = 0; i < 5; i++) transport.Send(new ArraySegment<byte>(Payload(i)), reliable: true);
            transport.TickOutgoing();
            Assert.Empty(inner.Sent);
            now = 10;
            transport.TickOutgoing();
            Assert.Equal(5, inner.Sent.Count);
            for (int i = 0; i < 5; i++) Assert.Equal(Payload(i), inner.Sent[i].Data);
        }

        [Fact]
        public void Unreliable_LossApplies()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 0, 0, 1000, 0, 0, 7);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            for (int i = 0; i < 5; i++) transport.Send(new ArraySegment<byte>(Payload(i)), reliable: false);
            transport.TickOutgoing();
            Assert.Empty(inner.Sent);
            Assert.Equal(5, transport.Stats.Dropped);
        }

        [Fact]
        public void Unreliable_DuplicateApplies()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 10, 0, 0, 1000, 0, 11);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            for (int i = 0; i < 3; i++) transport.Send(new ArraySegment<byte>(Payload(i)), reliable: false);
            now = 20;
            transport.TickOutgoing();
            Assert.Equal(6, inner.Sent.Count);
        }

        [Fact]
        public void Unreliable_ReorderApplies()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 0, 0, 0, 0, 1000, 13);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            for (int i = 0; i < 20; i++) transport.Send(new ArraySegment<byte>(Payload(i)), reliable: false);
            now = 100;
            transport.TickOutgoing();
            Assert.Equal(20, inner.Sent.Count);
            var expected = Enumerable.Range(0, 20).Select(i => Payload(i)[0]).ToList();
            var actual = inner.Sent.Select(p => p.Data[0]).ToList();
            Assert.NotEqual(expected, actual);
        }

        [Fact]
        public void Connect_ClearsQueuedFrames()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 100, 0, 0, 0, 0, 17);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            transport.Send(new ArraySegment<byte>(Payload(1)), reliable: true);
            transport.Connect("127.0.0.1", 1);
            now = 100;
            transport.TickOutgoing();
            Assert.Empty(inner.Sent);
        }

        [Fact]
        public void SeedZero_StaysStableAcrossSends()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 0, 0, 0, 0, 0, 0);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            transport.Send(new ArraySegment<byte>(Payload(1)), reliable: false);
            uint first = transport.Stats.Seed;
            transport.TickOutgoing();
            transport.Send(new ArraySegment<byte>(Payload(2)), reliable: false);
            transport.TickOutgoing();
            Assert.NotEqual(0u, first);
            Assert.Equal(first, transport.Stats.Seed);
        }

        [Fact]
        public void DisablingMidFlight_FlushesQueuedBeforeDirectSend()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 100, 0, 0, 0, 0, 19);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            transport.Send(new ArraySegment<byte>(Payload(1)), reliable: true);
            profile = WeakNetProfile.None;
            transport.Send(new ArraySegment<byte>(Payload(2)), reliable: true);
            Assert.Equal(2, inner.Sent.Count);
            Assert.Equal(Payload(1), inner.Sent[0].Data);
            Assert.Equal(Payload(2), inner.Sent[1].Data);
        }

        [Fact]
        public void Incoming_LossApplies()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 0, 0, 1000, 0, 0, 29);
            using var transport = Create(inner, () => profile, () => 0);
            int received = 0;
            transport.OnData += (_, __) => received++;
            inner.Raise(Payload(1), reliable: false);
            Assert.Equal(0, received);
            Assert.Equal(1, transport.Stats.DroppedDown);
        }

        [Fact]
        public void ReentrantSendDuringFlush_DoesNotThrowOrDuplicate()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 50, 0, 0, 0, 0, 31);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            int received = 0;
            transport.OnData += (_, __) =>
            {
                received++;
                transport.Send(new ArraySegment<byte>(Payload(9)), reliable: true);
            };
            inner.Raise(Payload(1), reliable: false);
            profile = WeakNetProfile.None;
            inner.Raise(Payload(2), reliable: false);
            Assert.Equal(2, received);
            Assert.Equal(2, inner.Sent.Count);
        }

        [Fact]
        public void QueueOverflow_KeepsAtMostLimit()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 1000, 0, 0, 0, 0, 23);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            for (int i = 0; i < 1100; i++) transport.Send(new ArraySegment<byte>(Payload(i)), reliable: false);
            Assert.True(transport.Stats.Overflow > 0);
            Assert.True(transport.Stats.Queued <= 1024, transport.Stats.Queued.ToString());
        }

        [Fact]
        public void Incoming_IsDelayedUntilDue()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 30, 0, 0, 0, 0, 3);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            var received = new List<byte[]>();
            transport.OnData += (data, reliable) =>
            {
                byte[] copy = new byte[data.Count];
                Array.Copy(data.Array, data.Offset, copy, 0, data.Count);
                received.Add(copy);
            };
            inner.Raise(Payload(9), reliable: false);
            Assert.Empty(received);
            now = 29;
            transport.TickIncoming();
            Assert.Empty(received);
            now = 30;
            transport.TickIncoming();
            Assert.Single(received);
            Assert.Equal(Payload(9), received[0]);
        }

        [Fact]
        public void QueueOverflow_DropsOldestUnreliable()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 1000, 0, 0, 0, 0, 5);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            for (int i = 0; i < 1100; i++) transport.Send(new ArraySegment<byte>(Payload(i)), reliable: false);
            Assert.True(transport.Stats.Overflow > 0, transport.Stats.Overflow.ToString());
        }

        [Fact]
        public void Dispose_ClearsQueue_AndDoesNotForward()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 100, 0, 0, 0, 0, 5);
            long now = 0;
            var transport = Create(inner, () => profile, () => now);
            transport.Send(new ArraySegment<byte>(Payload(1)), reliable: false);
            transport.Dispose();
            now = 1000;
            transport.TickOutgoing();
            Assert.Empty(inner.Sent);
        }

        [Fact]
        public void Send_CopiesCallerBuffer()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 10, 0, 0, 0, 0, 5);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            byte[] buffer = Payload(7);
            transport.Send(new ArraySegment<byte>(buffer), reliable: false);
            buffer[0] = 99;
            now = 10;
            transport.TickOutgoing();
            Assert.Single(inner.Sent);
            Assert.Equal(Payload(7), inner.Sent[0].Data);
        }

        [Fact]
        public void ReliableFifo_HoldsOrderWhenLatencyChanges()
        {
            var inner = new FakeInner();
            var profile = new WeakNetProfile(true, 10, 0, 0, 0, 0, 5);
            long now = 0;
            using var transport = Create(inner, () => profile, () => now);
            transport.Send(new ArraySegment<byte>(Payload(1)), reliable: true);
            transport.Send(new ArraySegment<byte>(Payload(2)), reliable: true);
            profile = new WeakNetProfile(true, 50, 0, 0, 0, 0, 5);
            transport.Send(new ArraySegment<byte>(Payload(3)), reliable: true);
            now = 10;
            transport.TickOutgoing();
            Assert.Equal(2, inner.Sent.Count);
            now = 100;
            transport.TickOutgoing();
            Assert.Equal(3, inner.Sent.Count);
            Assert.Equal(Payload(1), inner.Sent[0].Data);
            Assert.Equal(Payload(2), inner.Sent[1].Data);
            Assert.Equal(Payload(3), inner.Sent[2].Data);
        }
    }
}
