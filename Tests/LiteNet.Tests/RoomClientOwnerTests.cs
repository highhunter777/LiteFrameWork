using System;
using LiteNet.Protocol;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房主位协议态（开房裁决 owner 位 + 房主迁移续批）：<see cref="RoomClient.IsOwner"/> 是
    /// UI"开始对局"按钮归属的**唯一事实源**——JoinAck.IsOwner 定初值（重连重进同源刷新——
    /// 迁移后旧主不夺回），OwnerChanged 广播按"新房主席位 == 本机席位"重算。
    /// 全假传输驱动（喂编码包——与重连测试件同形态）。
    /// </summary>
    public sealed class RoomClientOwnerTests
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
            public void RaiseData(byte[] packet) => OnData?.Invoke(new ArraySegment<byte>(packet), true);
            public void Dispose() { }
        }

        private static (RoomClient c, FakeTransport t) JoinedOwner(int playerId = 3)
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);
            c.Connect("127.0.0.1", 17777);
            c.SendJoin("Room-A", "tok", "hash-1");
            t.RaiseData(PacketCodec.Encode(PacketType.JoinAck,
                new Proto.JoinAck { PlayerId = playerId, IsOwner = true }));
            return (c, t);
        }

        [Fact]
        public void JoinAck携房主位_初值入IsOwner()
        {
            var (c, _) = JoinedOwner();
            Assert.True(c.IsOwner, "开房者（首入座者）owner 位随 JoinAck 下发");

            var t2 = new FakeTransport();
            var c2 = new RoomClient(t2);
            c2.Connect("127.0.0.1", 17777);
            c2.SendJoin("Room-A", "tok", "hash-1");
            t2.RaiseData(PacketCodec.Encode(PacketType.JoinAck,
                new Proto.JoinAck { PlayerId = 4, IsOwner = false }));
            Assert.False(c2.IsOwner, "非开房者 owner 位为 false");
        }

        [Fact]
        public void 房主迁移_命中本机即到位_失位即摘()
        {
            var (c, t) = JoinedOwner(playerId: 3);

            t.RaiseData(PacketCodec.Encode(PacketType.OwnerChanged, new Proto.OwnerChanged { PlayerId = 2 }));
            Assert.False(c.IsOwner, "迁移给他人（席位 2）：旧主失位");

            t.RaiseData(PacketCodec.Encode(PacketType.OwnerChanged, new Proto.OwnerChanged { PlayerId = 3 }));
            Assert.True(c.IsOwner, "迁移回本机（席位 3）：新主到位");
        }

        [Fact]
        public void 重进JoinAck同源刷新_迁移后旧主不夺回()
        {
            var (c, t) = JoinedOwner(playerId: 3);

            // 旧主失位（迁移给席位 1）后重连——服务器按"人在座才算房主"派发 JoinAck：IsOwner=false（不夺回）
            t.RaiseData(PacketCodec.Encode(PacketType.OwnerChanged, new Proto.OwnerChanged { PlayerId = 1 }));
            Assert.False(c.IsOwner, "失位");

            t.RaiseData(PacketCodec.Encode(PacketType.JoinAck,
                new Proto.JoinAck { PlayerId = 3, IsOwner = false }));        // 重连重新 Join 以 JoinAck 为准
            Assert.False(c.IsOwner, "迁移后旧主重进——owner 位以服务器下发为准，不夺回");
        }
    }
}
