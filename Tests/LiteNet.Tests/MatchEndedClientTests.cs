using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    public sealed class MatchEndedClientTests
    {
        private sealed class Transport : IClientTransport
        {
            public bool Connected => true;
            public readonly List<PacketType> Sent = new List<PacketType>();
            public event Action OnConnected;
            public event Action OnDisconnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public void Connect(string address, int port) => OnConnected?.Invoke();
            public void Disconnect() => OnDisconnected?.Invoke();
            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void Dispose() { }
            public void Send(ArraySegment<byte> data, bool reliable)
            {
                Assert.True(PacketCodec.TryDecode(data, out PacketType type, out _));
                Sent.Add(type);
            }
            public void Deliver(PacketType type, Google.Protobuf.IMessage msg, bool reliable = true)
                => OnData?.Invoke(new ArraySegment<byte>(PacketCodec.Encode(type, msg)), reliable);
        }

        private static void Start(RoomClient client, Transport transport)
        {
            client.SendJoin("Rules", "token", "build");
            transport.Deliver(PacketType.JoinAck, new Proto.JoinAck { PlayerId = 0, ReconnectToken = "ticket" });
            transport.Deliver(PacketType.StartGame, new Proto.StartGame { Seed = 42 });
        }

        private static Proto.MatchEnded Result(string matchId = "Rules") => new Proto.MatchEnded
        {
            MatchId = matchId, FinalFrame = 123, GameplayEndReason = (int)MatchEndReason.KillLimit,
            WinnerEntityId = 65536L,
            Players = { new Proto.PlayerMatchResult { PlayerId = 0, EntityId = 65536L, Kills = 3, Deaths = 1 } },
        };

        [Fact]
        public void ReliableResultIsDeliveredOnceAndCannotBeMutatedBySubscribers()
        {
            var transport = new Transport();
            using var client = new RoomClient(transport);
            Start(client, transport);
            int count = 0;
            client.OnMatchEnded += msg => { count++; msg.Players[0].Kills = 999; };
            transport.Deliver(PacketType.MatchEnded, Result());
            transport.Deliver(PacketType.MatchEnded, Result());
            client.MatchResult.Players[0].Kills = 500;
            Assert.Equal(1, count);
            Assert.Equal(3, client.MatchResult.Players[0].Kills);
        }

        [Fact]
        public void WrongRoomAndUnreliableResultsAreIgnored()
        {
            var transport = new Transport();
            using var client = new RoomClient(transport);
            Start(client, transport);
            transport.Deliver(PacketType.MatchEnded, Result("other"));
            transport.Deliver(PacketType.MatchEnded, Result(), reliable: false);
            Assert.Null(client.MatchResult);
            transport.Deliver(PacketType.MatchEnded, Result());
            Assert.NotNull(client.MatchResult);
        }

        [Fact]
        public void FinishedMatchStopsInputSnapshotsAndReconnect()
        {
            var transport = new Transport();
            using var client = new RoomClient(transport);
            Start(client, transport);
            int snapshots = 0;
            client.OnSnapshot += _ => snapshots++;
            transport.Deliver(PacketType.MatchEnded, Result());
            client.SendInput(124, default, 120);
            transport.Deliver(PacketType.StateSnapshot, new Proto.StateSnapshot { Frame = 124 });
            transport.Disconnect();
            Assert.False(client.BeginReconnect());
            Assert.DoesNotContain(PacketType.Input, transport.Sent);
            Assert.Equal(0, snapshots);
        }

        [Fact]
        public void NewJoinClearsPreviousResultAndRequiresNewStart()
        {
            var transport = new Transport();
            using var client = new RoomClient(transport);
            Start(client, transport);
            transport.Deliver(PacketType.MatchEnded, Result());
            client.SendJoin("Rules", "token", "build");
            Assert.Null(client.MatchResult);
            transport.Deliver(PacketType.MatchEnded, Result());
            Assert.Null(client.MatchResult);
            transport.Deliver(PacketType.StartGame, new Proto.StartGame { Seed = 44 });
            transport.Deliver(PacketType.MatchEnded, Result());
            Assert.NotNull(client.MatchResult);
        }

        [Fact]
        public void PreviousMatchResultCannotEndNewMatchInSameRoom()
        {
            var transport = new Transport();
            using var client = new RoomClient(transport);
            Start(client, transport);
            transport.Deliver(PacketType.StartGame, new Proto.StartGame { Seed = 42, MatchId = "match-one" });
            transport.Deliver(PacketType.MatchEnded, Result("match-one"));
            client.SendJoin("Rules", "token", "build");
            transport.Deliver(PacketType.StartGame, new Proto.StartGame { Seed = 42, MatchId = "match-two" });
            transport.Deliver(PacketType.MatchEnded, Result("match-one"));
            Assert.Null(client.MatchResult);
            transport.Deliver(PacketType.MatchEnded, Result("match-two"));
            Assert.Equal("match-two", client.MatchResult.MatchId);
        }
    }
}
