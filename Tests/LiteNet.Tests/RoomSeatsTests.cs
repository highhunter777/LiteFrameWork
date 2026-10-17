using System;
using System.Collections.Generic;
using System.Linq;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim.Tests;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 席位实时面（席位实时面批）：房间内核对名单变化的产出（<see cref="SeatsChanged"/>——进房/断线/
    /// 恢复完成时全量快照）、协议往返（<c>RoomSeats</c>）、以及 <see cref="RoomClient"/> 的缓存与事件
    /// （房主位测试件同形态：全假传输喂编码包）。
    /// </summary>
    public sealed class RoomSeatsTests
    {
        private static List<RoomOutput> Run(RoomRuntime room, in RoomCommand cmd)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(cmd, outputs);
            return outputs;
        }

        private static SeatsChanged LastSeats(List<RoomOutput> outputs)
            => (SeatsChanged)outputs.OfType<SignalOutput>().Select(s => s.Signal).OfType<SeatsChanged>().Last();

        [Fact]
        public void 进房断线恢复_各广播全量名单快照()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 3, Seed = 7,
            });

            SeatsChanged s1 = LastSeats(Run(room, RoomCommand.Join(101)));
            Assert.Single(s1.Seats);
            Assert.Equal(0, s1.Seats[0].PlayerId);
            Assert.True(s1.Seats[0].Connected, "刚进房 = 在线");

            SeatsChanged s2 = LastSeats(Run(room, RoomCommand.Join(202)));
            Assert.Equal(2, s2.Seats.Length);
            Assert.True(s2.Seats[1].Connected);

            // 开局（重连语义只在对局中存在——App 未开局拒重连，未开局离场是"释放"而非"保留"）
            Run(room, RoomCommand.StartEarly(0));

            SeatsChanged s3 = LastSeats(Run(room, RoomCommand.Disconnect(101)));      // 对局中断线（席位保留）
            Assert.Equal(2, s3.Seats.Length);
            Assert.False(s3.Seats[0].Connected, "断线 = 离线（席位仍在册）");
            Assert.True(s3.Seats[1].Connected);

            var rebind = Run(room, RoomCommand.Rebind(0, 303));                       // 重连重绑 → 恢复中（离线态未变）
            Assert.Empty(rebind.OfType<SignalOutput>().Where(s => s.Signal is SeatsChanged));
            Assert.False(room.SeatOf(0).Phase == SeatPhase.Active, "恢复中 = 仍离线（名单不重发——变化才发）");

            SeatsChanged s4 = LastSeats(Run(room, RoomCommand.RestoreAck(0)));
            Assert.True(s4.Seats[0].Connected, "恢复完成 = 在线");
        }

        [Fact]
        public void 未开局离场_名单缩容而非保留离线态()
        {
            // 未开局离场即释放（空位复用）：名单从列表**消失**，不是"保留显示离线"——
            // 等待期席位不可恢复，保留只占容量（房间页等待人数随之回落）。
            var room = new RoomRuntime(new RoomConfig
            {
                Map = TestMaps.Greybox(), RoomId = "W", ExpectedPlayers = 3, Seed = 7,
            });

            Run(room, RoomCommand.Join(101));
            SeatsChanged s2 = LastSeats(Run(room, RoomCommand.Join(202)));
            Assert.Equal(2, s2.Seats.Length);

            SeatsChanged s3 = LastSeats(Run(room, RoomCommand.Disconnect(101)));      // 未开局离场 → 释放
            Assert.Single(s3.Seats);                                                 // 名单缩容
            Assert.Equal(1, s3.Seats[0].PlayerId);
            Assert.True(s3.Seats[0].Connected);
            Assert.Null(room.SeatOf(0));                                             // 席位已让出（可复用）
        }

        [Fact]
        public void 协议往返_RoomSeats编解码()
        {
            var sent = new LiteNet.Proto.RoomSeats();
            sent.Seats.Add(new LiteNet.Proto.SeatInfo { PlayerId = 0, DisplayName = "玩家1", Connected = true });
            sent.Seats.Add(new LiteNet.Proto.SeatInfo { PlayerId = 3, DisplayName = "玩家4", Connected = false });

            byte[] wire = PacketCodec.Encode(PacketType.RoomSeats, sent);
            Assert.True(PacketCodec.TryDecode(new ArraySegment<byte>(wire), out PacketType type, out var msg));
            Assert.Equal(PacketType.RoomSeats, type);

            var got = (LiteNet.Proto.RoomSeats)msg;
            Assert.Equal(2, got.Seats.Count);
            Assert.Equal(0, got.Seats[0].PlayerId);
            Assert.Equal("玩家1", got.Seats[0].DisplayName);
            Assert.True(got.Seats[0].Connected);
            Assert.Equal(3, got.Seats[1].PlayerId);
            Assert.False(got.Seats[1].Connected);
        }

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

        [Fact]
        public void 客户端_缓存最新快照并派发事件()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);
            c.Connect("127.0.0.1", 17777);
            Assert.Null(c.Seats);                                          // 未收到前为 null

            LiteNet.Proto.RoomSeats received = null;
            c.OnRoomSeats += seats => received = seats;

            var packet = new LiteNet.Proto.RoomSeats();
            packet.Seats.Add(new LiteNet.Proto.SeatInfo { PlayerId = 0, DisplayName = "玩家1", Connected = true });
            t.RaiseData(PacketCodec.Encode(PacketType.RoomSeats, packet));

            Assert.NotNull(received);
            Assert.NotNull(c.Seats);
            Assert.Single(c.Seats.Seats);
            Assert.Equal("玩家1", c.Seats.Seats[0].DisplayName);
            Assert.NotSame(c.Seats, c.Seats);                              // 读侧 Clone 防御（同 MatchResult 先例）

            var packet2 = new LiteNet.Proto.RoomSeats();                   // 全量重发整体替换（含在线态翻转）
            packet2.Seats.Add(new LiteNet.Proto.SeatInfo { PlayerId = 0, DisplayName = "玩家1", Connected = false });
            t.RaiseData(PacketCodec.Encode(PacketType.RoomSeats, packet2));
            Assert.False(c.Seats.Seats[0].Connected, "缓存跟随最新全量快照");
        }
    }
}
