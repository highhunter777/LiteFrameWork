using System;
using LiteNet.Proto;
using LiteNet.Protocol;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 复用信封编码器（高频输入每包零分配）。
    /// 关键断言：**线格式与 `PacketCodec.Encode` 逐字节一致**（高频路径与常规路径不能各写一套格式），
    /// 且复用缓冲**不残留上一次内容**、**超长消息按需扩容量而不截断**。
    /// </summary>
    public sealed class PacketWriterTests
    {
        [Fact]
        public void 与PacketCodec_逐字节一致()
        {
            var msg = new InputMessage { Frame = 42, AckSnapshot = 7, ViewFrame = 40 };
            msg.Frames.Add(new InputFrame { EntityId = 3, MoveX = 1.5f, MoveZ = -2.25f, AimPointX = 0.5f, AimPointY = 1f, AimPointZ = -1f, Buttons = 1u });

            byte[] expected = PacketCodec.Encode(PacketType.Input, msg);

            using var w = new PacketWriter();
            ArraySegment<byte> actual = w.Write(PacketType.Input, msg);

            Assert.Equal(expected.Length, actual.Count);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], actual.Array[actual.Offset + i]);
        }

        [Fact]
        public void 复用_多次写入不残留上一次内容()
        {
            using var w = new PacketWriter();

            var big = new InputMessage { Frame = 9999 };
            for (int i = 0; i < 20; i++)
                big.Frames.Add(new InputFrame { EntityId = i, MoveX = i, MoveZ = i, AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f });
            w.Write(PacketType.Input, big);                       // 先写一条大的（把缓冲撑大）

            var small = new JoinRequest { RoomId = "r", Token = "t", BuildHash = "h" };
            ArraySegment<byte> seg = w.Write(PacketType.Join, small);

            // 小包的字节必须与独立编码一致——若残留尾部会被解析器当成多余载荷（长度语义也会错）
            byte[] expected = PacketCodec.Encode(PacketType.Join, small);
            Assert.Equal(expected.Length, seg.Count);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], seg.Array[seg.Offset + i]);

            Assert.True(PacketCodec.TryDecode(seg, out PacketType type, out var decoded));
            Assert.Equal(PacketType.Join, type);
            Assert.Equal("r", ((JoinRequest)decoded).RoomId);
        }

        [Fact]
        public void 扩容_超长消息不截断()
        {
            using var w = new PacketWriter(initialCapacity: 16);   // 故意很小
            int before = w.Capacity;

            var msg = new InputMessage { Frame = 1 };
            for (int i = 0; i < 64; i++)
                msg.Frames.Add(new InputFrame { EntityId = i, MoveX = i * 0.5f, MoveZ = -i });

            ArraySegment<byte> seg = w.Write(PacketType.Input, msg);

            Assert.True(w.Capacity > before, "缓冲未扩容——会截断消息");
            Assert.True(PacketCodec.TryDecode(seg, out PacketType type, out var decoded), "扩容后应能正常解码");
            Assert.Equal(PacketType.Input, type);
            Assert.Equal(64, ((InputMessage)decoded).Frames.Count);   // 一帧都没丢
        }

        [Fact]
        public void Dispose后写入_抛()
        {
            var w = new PacketWriter();
            w.Dispose();
            Assert.Throws<ObjectDisposedException>(() => w.Write(PacketType.Join, new JoinRequest()));
        }
    }
}
