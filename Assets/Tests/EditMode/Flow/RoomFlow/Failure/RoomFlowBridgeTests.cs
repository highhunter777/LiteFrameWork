using System;
using LiteNet.Proto;
using NUnit.Framework;
using LiteGame.Modules.Battle.Abstractions.Session;

using ProtoOwnerChanged = LiteNet.Proto.OwnerChanged;
namespace LiteGame.Tests.EditMode
{
    /// <summary>RoomBridge 会话装订：旧会话解绑后零回放、重复 Bind 先摘旧，投影只归当前会话。</summary>
    public sealed class RoomFlowBridgeTests
    {
        [Test]
        public void 旧会话解绑后事件不回到新投影()
        {
            var bridge = new RoomBridge();
            var old = new FakeRoomSession { IsOwner = true, ExpectedPlayers = 2, Seats = Seats("Old") };
            var fresh = new FakeRoomSession { IsOwner = false, ExpectedPlayers = 4, Seats = Seats("New") };
            int ownerEvents = 0;
            int seatEvents = 0;
            bridge.OwnerChanged += _ => ownerEvents++;
            bridge.SeatsChanged += _ => seatEvents++;

            bridge.BindSession(old);
            Assert.IsTrue(bridge.IsOwner, "装订即回放房主事实（前置）");
            Assert.AreEqual(2, bridge.ExpectedPlayers);
            bridge.UnbindSession();
            bridge.BindSession(fresh);
            Assert.IsFalse(bridge.IsOwner, "投影来自新会话（前置）");
            Assert.AreEqual(4, bridge.ExpectedPlayers);
            Assert.AreEqual("New", bridge.Seats[0].Name);
            int ownerBaseline = ownerEvents;
            int seatBaseline = seatEvents;

            old.IsOwner = true;
            old.RaiseOwnerChanged();
            old.RaiseSeats(Seats("Old-Late"));

            Assert.AreEqual(ownerBaseline, ownerEvents, "解绑后旧会话房主事件不得再触发投影");
            Assert.AreEqual(seatBaseline, seatEvents, "解绑后旧会话席位事件不得再触发投影");
            Assert.IsFalse(bridge.IsOwner, "旧事件不得改写新投影的房主事实");
            Assert.AreEqual("New", bridge.Seats[0].Name, "旧事件不得改写新投影的席位");
        }

        [Test]
        public void 重复Bind先摘旧_开局请求只归新会话()
        {
            var bridge = new RoomBridge();
            var old = new FakeRoomSession { IsOwner = true, Seats = Seats("Old") };
            var fresh = new FakeRoomSession { IsOwner = false, Seats = Seats("New") };
            int ownerEvents = 0;
            int seatEvents = 0;
            int deniedEvents = 0;
            bridge.OwnerChanged += _ => ownerEvents++;
            bridge.SeatsChanged += _ => seatEvents++;
            bridge.StartMatchDenied += _ => deniedEvents++;

            bridge.BindSession(old);
            bridge.BindSession(fresh);
            int ownerBaseline = ownerEvents;
            int seatBaseline = seatEvents;

            old.RaiseOwnerChanged();
            old.RaiseSeats(Seats("Old-Late"));
            old.RaiseStartDenied("old-denied");

            Assert.AreEqual(ownerBaseline, ownerEvents, "重复 Bind 后旧会话房主事件不得回放");
            Assert.AreEqual(seatBaseline, seatEvents, "重复 Bind 后旧会话席位事件不得回放");
            Assert.AreEqual(0, deniedEvents, "重复 Bind 后旧会话开局拒绝不得回放");
            Assert.AreEqual("New", bridge.Seats[0].Name);

            fresh.RaiseOwnerChanged();
            fresh.RaiseSeats(Seats("New-2"));
            fresh.RaiseStartDenied("fresh-denied");
            Assert.AreEqual(ownerBaseline + 1, ownerEvents, "当前会话事件正常投影");
            Assert.AreEqual(seatBaseline + 1, seatEvents);
            Assert.AreEqual(1, deniedEvents);

            bridge.RequestStartMatch();
            Assert.AreEqual(1, fresh.StartRequests, "开局请求只发到当前会话");
            Assert.AreEqual(0, old.StartRequests);
        }

        private static RoomSeats Seats(string name)
        {
            var seats = new RoomSeats();
            seats.Seats.Add(new SeatInfo { PlayerId = 1, DisplayName = name, Connected = true });
            return seats;
        }

        private sealed class FakeRoomSession : IRoomSessionFace
        {
            public bool IsOwner { get; set; }
            public int ExpectedPlayers { get; set; }
            public RoomSeats Seats { get; set; }
            public int StartRequests;

            public event Action<ProtoOwnerChanged> OwnerChanged;
            public event Action<RoomSeats> RoomSeatsChanged;
            public event Action<StartMatchResponse> StartMatchDenied;

            public void SendStartMatchRequest() => StartRequests++;

            public void RaiseOwnerChanged() => OwnerChanged?.Invoke(new ProtoOwnerChanged { PlayerId = 1 });
            public void RaiseSeats(RoomSeats seats) => RoomSeatsChanged?.Invoke(seats);

            public void RaiseStartDenied(string reason)
                => StartMatchDenied?.Invoke(new StartMatchResponse { Reason = reason });
        }
    }
}
