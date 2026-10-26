using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>优雅离场：客户端须主动发 PacketType.Leave（服务端据此立即释放席位，不等重连窗口）。</summary>
    public sealed class RoomClientLeaveTests
    {
        private sealed class RecordingTransport : IClientTransport
        {
            public bool Connected => true;
            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;
            public readonly List<(PacketType type, bool reliable)> Sent = new List<(PacketType, bool)>();

            public void Connect(string address, int port) => OnConnected?.Invoke();
            public void Disconnect() => OnDisconnected?.Invoke();
            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void Send(ArraySegment<byte> data, bool reliable)
            {
                Assert.True(PacketCodec.TryDecode(data, out PacketType type, out _));
                Sent.Add((type, reliable));
            }
            public void RaiseData(byte[] packet) => OnData?.Invoke(new ArraySegment<byte>(packet), true);
            public void Dispose() { }
        }

        private static RoomClient Joined(RecordingTransport t)
        {
            var c = new RoomClient(t);
            c.Connect("127.0.0.1", 17777);
            c.SendJoin("Room-A", "tok", "hash-1");
            t.RaiseData(PacketCodec.Encode(PacketType.JoinAck, new Proto.JoinAck { PlayerId = 1, IsOwner = true }));
            return c;
        }

        [Fact]
        public void 主动离场_发可靠Leave()
        {
            var t = new RecordingTransport();
            var c = Joined(t);
            t.Sent.Clear();

            c.SendLeave();

            var packet = Assert.Single(t.Sent);
            Assert.Equal(PacketType.Leave, packet.type);
            Assert.True(packet.reliable, "Leave 走可靠通道（服务端按控制面处理）");
        }

        [Fact]
        public void 未入座_不发Leave()
        {
            var t = new RecordingTransport();
            var c = new RoomClient(t);
            c.Connect("127.0.0.1", 17777);
            t.Sent.Clear();

            c.SendLeave();

            Assert.Empty(t.Sent);
        }

        [Fact]
        public void 释放后_不发Leave且不抛()
        {
            var t = new RecordingTransport();
            var c = Joined(t);
            c.Dispose();
            t.Sent.Clear();

            c.SendLeave();

            Assert.Empty(t.Sent);
        }
    }
}
