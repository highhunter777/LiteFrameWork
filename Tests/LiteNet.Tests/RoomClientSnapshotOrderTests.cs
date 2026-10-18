using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 客户端快照帧号单调守卫（P0）：快照走 Unreliable，UDP 可乱序；旧包不得回退
    /// `LastSnapshotFrame`、不得再交给镜像/和解（否则血量/死亡/比分回退，插值窗口倒退）。
    /// </summary>
    public sealed class RoomClientSnapshotOrderTests
    {
        private sealed class FakeTransport : IClientTransport
        {
            public bool Connected => true;
            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;
            public void Connect(string address, int port) => OnConnected?.Invoke();
            public void Disconnect() => OnDisconnected?.Invoke();
            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void Send(ArraySegment<byte> data, bool reliable) { }
            public void Raise(byte[] packet) => OnData?.Invoke(new ArraySegment<byte>(packet), false);
            public void Dispose() { }
        }

        private static Proto.StateSnapshot Snapshot(int frame)
            => new Proto.StateSnapshot { Frame = frame, IsFull = false };

        [Fact]
        public void 旧快照与同帧重复包_不覆盖已应用帧()
        {
            var transport = new FakeTransport();
            using var client = new LiteNet.RoomClient(transport);
            int delivered = 0;
            client.OnSnapshot += _ => delivered++;

            transport.Raise(PacketCodec.Encode(PacketType.StateSnapshot, Snapshot(5)));
            transport.Raise(PacketCodec.Encode(PacketType.StateSnapshot, Snapshot(4)));   // 乱序旧包
            transport.Raise(PacketCodec.Encode(PacketType.StateSnapshot, Snapshot(5)));   // 同帧重复

            Assert.Equal(1, delivered);
            Assert.Equal(5, client.LastSnapshotFrame);
        }
    }
}
