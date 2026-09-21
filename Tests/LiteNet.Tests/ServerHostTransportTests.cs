using System;
using System.Collections.Generic;
using LiteTesting;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// **可替换性验证**（《状态同步实施方案》§4.6 第二刀；2026-09-19 收口）：
    /// `ServerHost` 现在只依赖窄端口 <see cref="IRoomTransport"/>——本用例用**假的传输实现**
    /// 跑通"装配 → 连接 → Join → 满员开局"全链路，证明换传输框架确实只需新写适配器 + 装配一行，
    /// 而不是嘴上说可替换（假件也能跑，才是真的解耦）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class ServerHostTransportTests
    {
        [Fact]
        public void 窄端口换实现_ServerHost零改动跑通装配到开局()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33333, RoomId = "Fake" });

            Assert.Equal(33333, t.StartedPort);                        // 构造即 Start（宿主接管传输）

            t.RaiseConnected(1);
            t.RaiseConnected(2);
            t.RaiseData(1, JoinPacket());
            JoinAck ack1 = t.LastJoinAck(1);
            Assert.NotNull(ack1);
            Assert.Equal(0, ack1.PlayerId);
            Assert.False(string.IsNullOrEmpty(ack1.ReconnectToken));   // E3 一次性票据照常发

            t.RaiseData(2, JoinPacket());
            JoinAck ack2 = t.LastJoinAck(2);
            Assert.NotNull(ack2);
            Assert.Equal(1, ack2.PlayerId);

            Assert.True(host.Room.Started, "满员应自动 StartGame（无 MatchMaker）");
            Assert.NotNull(t.LastStartGame(1));                        // 两名成员都收到 StartGame
            Assert.NotNull(t.LastStartGame(2));
            Assert.Equal(2, host.Sessions.Count);
        }

        [Fact]
        public void 窄端口换实现_版本红线与输入路由不受影响()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33334, RoomId = "Fake" });
            t.RaiseConnected(1);

            // buildHash 不符 → 拒绝（不发明文 JoinAck），走 Ops rejects 记账
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Fake", Token = "t", BuildHash = "deadbeef" }));
            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(1, host.Ops.Rejects);

            // 正确 hash → 放行；随后驱动 Pump 不应抛（假传输的 Tick 是 no-op，房间尚未 Start）
            t.RaiseData(1, JoinPacket());
            Assert.NotNull(t.LastJoinAck(1));
            host.Pump();
        }

        [Fact]
        public void 房间号不符或缺失_拒绝进房()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33336, RoomId = "Room-A" });
            t.RaiseConnected(1);

            // ① 请求了别的房间（--room 可配后，这不能再靠"恰好只有一个房间"蒙过去）
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Room-B", Token = "t", BuildHash = ServerHost.ServerBuildHash }));
            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(1, host.Ops.Rejects);
            Assert.Empty(host.Room.MemberIds());           // 连接存在但未占席位（拒绝的是进房，不是连接）

            // ② 房间号缺失
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "", Token = "t", BuildHash = ServerHost.ServerBuildHash }));
            Assert.Equal(2, host.Ops.Rejects);
            Assert.Null(t.LastJoinAck(1));

            // ③ 房间号正确 → 放行（红线只挡错房间，不挡正常进房）
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Room-A", Token = "t", BuildHash = ServerHost.ServerBuildHash }));
            Assert.NotNull(t.LastJoinAck(1));
            Assert.Equal(2, host.Ops.Rejects);             // 计数不再增长
        }

        [Fact]
        public void 宿主持有所有权_Dispose释放传输()
        {
            var t = new FakeRoomTransport();
            var host = new ServerHost(t, new RoomConfig { Port = 33335, RoomId = "Fake" });
            host.Dispose();
            Assert.True(t.Disposed, "ServerHost 接管传输所有权：Dispose 应释放它");
        }

        // ---- R0-A 边界（《商业级通用服务端框架总设计》§5 P0-3）----

        [Fact]
        public void 超长入包_解析前硬边界拒绝并计数_不崩溃()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33337, RoomId = "Fake" });
            t.RaiseConnected(1);

            // 超过 MaxInboundPacketBytes 的"Join"包（内容合法但体量越界）——解析前直接丢弃
            var oversized = new byte[ServerHost.MaxInboundPacketBytes + 1];
            oversized[0] = (byte)PacketType.Join;
            t.RaiseData(1, oversized);

            Assert.Equal(1, host.Ops.PacketOversized);
            Assert.Equal(0, host.Ops.Rejects);
            Assert.Null(t.LastJoinAck(1));                       // 未进房（且 proto 解析器从未见到这个包）

            // 边界内正常包照常处理
            t.RaiseData(1, JoinPacket());
            Assert.NotNull(t.LastJoinAck(1));
        }

        [Fact]
        public void Join字段超长_按UTF8字节上限拒绝()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33338, RoomId = "Room-A" });
            t.RaiseConnected(1);

            // token 超 256 UTF-8 字节（多字节字符——字节数 ≠ 字符数）
            string longToken = new string('年', ServerHost.MaxTokenBytes / 3 + 1);
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Room-A", Token = longToken, BuildHash = ServerHost.ServerBuildHash }));
            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(1, host.Ops.Rejects);

            // roomId 超 64 字节
            string longRoom = new string('r', ServerHost.MaxRoomIdBytes + 1);
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = longRoom, Token = "t", BuildHash = ServerHost.ServerBuildHash }));
            Assert.Equal(2, host.Ops.Rejects);

            // 合法边界值（恰好 ≤ 上限）放行
            string maxToken = new string('t', ServerHost.MaxTokenBytes);
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Room-A", Token = maxToken, BuildHash = ServerHost.ServerBuildHash }));
            Assert.NotNull(t.LastJoinAck(1));
            Assert.Equal(2, host.Ops.Rejects);                   // 不再增长
        }

        [Fact]
        public void 坏包计数_未知类型与随机字节不污染会话()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33339, RoomId = "Fake" });
            t.RaiseConnected(1);

            t.RaiseData(1, new byte[] { 200, 1, 2, 3 });          // 未定义 packet type
            t.RaiseData(1, new byte[] { (byte)PacketType.Join, 0xFF, 0xFF, 0xFF }); // proto 解析失败
            t.RaiseData(1, System.Array.Empty<byte>());           // 空包

            Assert.Equal(3, host.Ops.PacketRejects);
            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(0, host.Ops.Rejects);                    // 坏包是丢弃不是进房拒绝

            // 之后正常包照常处理（会话未被坏包污染）
            t.RaiseData(1, JoinPacket());
            Assert.NotNull(t.LastJoinAck(1));
        }

        private static byte[] JoinPacket()
        {
            return PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Fake", Token = "tok", BuildHash = ServerHost.ServerBuildHash });
        }

        /// <summary>
        /// 假传输：只实现窄端口（不碰 kcp2k）——同时验证"端口够不够用"（若 ServerHost 用到端口外的东西，
        /// 本类就编译不过，解耦度自证）。
        /// </summary>
        private sealed class FakeRoomTransport : IRoomTransport
        {
            private readonly List<(int conn, ArraySegment<byte> data, bool reliable)> _sent
                = new List<(int, ArraySegment<byte>, bool)>();

            public int StartedPort { get; private set; } = -1;
            public bool Disposed { get; private set; }

            public event Action<int, ArraySegment<byte>, bool> OnData;
            public event Action<int> OnConnected;
            public event Action<int> OnDisconnected;

            public void Start(int port) => StartedPort = port;
            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void Disconnect(int connectionId) { }
            public void Broadcast(ArraySegment<byte> data, bool reliable) { }
            public void Dispose() => Disposed = true;

            /// <summary>真实传输同步拷贝；假件同样拷贝（避免上层复用缓冲的假设在假件上假绿）。</summary>
            public void SendTo(int connectionId, ArraySegment<byte> data, bool reliable)
            {
                var copy = new byte[data.Count];
                Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
                _sent.Add((connectionId, new ArraySegment<byte>(copy), reliable));
            }

            public void RaiseConnected(int conn) => OnConnected?.Invoke(conn);
            public void RaiseData(int conn, byte[] packet, bool reliable = true)
                => OnData?.Invoke(conn, new ArraySegment<byte>(packet), reliable);

            public JoinAck LastJoinAck(int conn) => (JoinAck)LastOf(conn, PacketType.JoinAck);
            public StartGame LastStartGame(int conn) => (StartGame)LastOf(conn, PacketType.StartGame);

            private object LastOf(int conn, PacketType type)
            {
                for (int i = _sent.Count - 1; i >= 0; i--)
                {
                    if (_sent[i].conn != conn) continue;
                    if (!PacketCodec.TryDecode(_sent[i].data, out PacketType t, out var msg)) continue;
                    if (t == type) return msg;
                }
                return null;
            }
        }
    }
}
