using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 终态房间销毁清理（《商业级通用服务端框架总设计》§42"drain 与房间销毁后清理归 R2"）。
    ///
    /// 语义：房间到达终态（Closed/Aborted）后，由宿主 <see cref="ServerHost.Pump"/> 销毁——
    /// 先应用该房间当轮输出（结算入盒等），再从房间表移除、容量归还；同房号此后可**重建**（新实例）。
    /// 销毁同时清理席位会话的路由字段，防止旧连接的迟到输入命中重建后的新房间席位。
    /// </summary>
    public sealed class RoomDestructionTests
    {
        private const string Config = @"{
            ""port"": 45201, ""max_rooms"": 1, ""audience"": """",
            ""default_template"": ""one"",
            ""rooms"": { ""one"": { ""expected_players"": 1 } }
        }";

        private static (ServerHost host, FakeRoomTransport t) NewHost()
        {
            var cfg = RoomServerConfig.Parse(Config);
            var t = new FakeRoomTransport();
            return (new ServerHost(t, null, null, null, cfg), t);
        }

        private static byte[] Join(string roomId) => PacketCodec.Encode(PacketType.Join,
            new JoinRequest { RoomId = roomId, Token = "tok", BuildHash = ServerHost.ServerBuildHash });

        [Fact]
        public void 终态房间_Pump销毁_容量归还且同房号可重建()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime first));
            Assert.True(first.Started);                       // 1 人房满员即开局

            h.t.RaiseData(1, PacketCodec.Encode(PacketType.Leave, new Proto.Leave()));
            Assert.True(first.Closed, "全员离场应收尾到终态");
            Assert.Equal(1, host.Ops.SettlementsReady);       // 销毁前输出已应用（结算事实不丢）

            host.Pump();

            Assert.Equal(0, host.RoomCount);                  // 终态房间已从房间表移除
            Assert.False(host.TryGetRoom("Room-A", out _));
            Assert.Equal(1, host.Ops.RoomsDestroyed);

            // 容量 1 格：销毁后同房号重建必须成功（容量未销毁时会在此被上限拒掉）
            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-A"));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime second));
            Assert.NotSame(first, second);                    // 全新实例（不是复用旧房间状态）
            Assert.NotSame(first.AuthSim, second.AuthSim);    // 权威状态也是新的
            Assert.True(second.Started);                      // 1 人房：新房间满员即开局
            Assert.Equal(MatchPhase.Running, second.Phase);   // 与旧房间的终态无关
            Assert.Equal(1, host.RoomCount);
        }

        [Fact]
        public void 销毁后_旧会话路由字段被清理_迟到输入不串新房间席位()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(1, PacketCodec.Encode(PacketType.Leave, new Proto.Leave()));
            host.Pump();                                      // 销毁终态房间

            // 旧连接仍活着：会话已不携带房间/席位——重新进房必须再次走准入与验票。
            Assert.True(host.Sessions.TryGet(1, out Session old));
            Assert.Null(old.RoomId);
            Assert.Equal(-1, old.PlayerId);

            // 同房号重建，另一个人进房；旧连接的迟到输入必须被丢弃（不得驱动新席位）。
            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-A"));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime second));
            Assert.True(second.Started);

            long acceptedBefore = second.Gate.AcceptedCount;
            h.t.RaiseData(1, PacketCodec.Encode(PacketType.Input, new InputMessage
            {
                Frame = second.AuthSim.Frame + 1,
                AckSnapshot = -1,
                ViewFrame = 0,
                Frames = { new InputFrame { EntityId = 0, MoveX = 0.5f, AimX = 1f } },
            }));

            Assert.Equal(acceptedBefore, second.Gate.AcceptedCount);
        }

        [Fact]
        public void 未终态房间_不销毁()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime room));
            Assert.True(room.Started);

            host.Pump();

            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime same));
            Assert.Same(room, same);
            Assert.Equal(0, host.Ops.RoomsDestroyed);
            Assert.Equal(1, host.RoomCount);
        }
    }
}