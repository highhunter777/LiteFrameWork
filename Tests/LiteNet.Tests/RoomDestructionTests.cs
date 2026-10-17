using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;
using LiteSim.Tests;

namespace LiteNet.Tests
{
    /// <summary>
    /// 终态房间销毁清理（《商业级通用服务端框架总设计》§42"drain 与房间销毁后清理归 R2"）。
    ///
    /// 语义：房间到达终态（Closed/Aborted）后，由宿主 <see cref="ServerHost.Pump"/> 销毁——
    /// 先应用该房间当轮输出（结算入盒等），再从房间表移除、容量归还；同房号此后可**重建**（新实例）。
    /// 销毁同时清理席位会话的路由字段，防止旧连接的迟到输入命中重建后的新房间席位。
    /// 终态两源：对局收场（Running 全员离场/时限/杀限 → Closed）；**未开局即全员离场 → Aborted**
    /// （无结果可结算——不收尾的未开局房会因 NextPlayerId 只增而永久占死房号与名额）。
    /// </summary>
    public sealed class RoomDestructionTests
    {
        private const string Config = @"{
            ""port"": 45201, ""max_rooms"": 1, ""audience"": """",
            ""default_template"": ""one"",
            ""rooms"": { ""one"": { ""expected_players"": 1 } }
        }";

        /// <summary>席位复用/等待超时用例的模板（2 人房 + 等待时限 200ms）。</summary>
        private const string WaitConfig = @"{
            ""port"": 45202, ""max_rooms"": 1, ""audience"": """",
            ""default_template"": ""wait"",
            ""rooms"": { ""wait"": { ""expected_players"": 2, ""waiting_timeout_ms"": 200 } }
        }";

        private static (ServerHost host, FakeRoomTransport t) NewHost()
        {
            var cfg = RoomServerConfig.Parse(Config);
            var t = new FakeRoomTransport();
            return (new ServerHost(t, null, null, null, cfg, map: TestMaps.Greybox()), t);
        }

        private static (ServerHost host, FakeRoomTransport t) NewWaitHost()
        {
            var cfg = RoomServerConfig.Parse(WaitConfig);
            var t = new FakeRoomTransport();
            return (new ServerHost(t, null, null, null, cfg, map: TestMaps.Greybox()), t);
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
            h.t.StartMatchAsOwner(1);                         // 房主开局（满员不再自动开局）
            Assert.True(first.Started);

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
            h.t.StartMatchAsOwner(2);                         // 新房主开局
            Assert.True(second.Started);
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
            h.t.StartMatchAsOwner(1);                         // 房主开局（满员不再自动开局）
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
            h.t.StartMatchAsOwner(2);                         // 新房主开局（满员不再自动开局）
            Assert.True(second.Started);

            long acceptedBefore = second.Gate.AcceptedCount;
            h.t.RaiseData(1, PacketCodec.Encode(PacketType.Input, new InputMessage
            {
                Frame = second.AuthSim.Frame + 1,
                AckSnapshot = -1,
                ViewFrame = 0,
                Frames = { new InputFrame { EntityId = 0, MoveX = 0.5f } },
            }));

            Assert.Equal(acceptedBefore, second.Gate.AcceptedCount);
        }

        [Fact]
        public void 终态房间_销毁前_上报面不报终态明细()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime room));
            h.t.StartMatchAsOwner(1);                         // 房主开局（满员不再自动开局）

            // 非终态在册：上报面含该房间（正证——过滤不是把明细整个抹掉）
            ServerHost.RoomSummary[] running = host.RoomSummaries();
            Assert.Single(running);
            Assert.Equal("Room-A", running[0].RoomId);
            Assert.Equal((int)MatchPhase.Running, running[0].Phase);

            // 终态后、销毁前（销毁在随后的 Pump）：房间仍在册，但上报面必须不报——
            // 契约"终态房间（已销毁）不报"；不过滤会把 phase=6/7 送 Meta 相位闸（整次注册拒绝）。
            h.t.RaiseData(1, PacketCodec.Encode(PacketType.Leave, new Proto.Leave()));
            Assert.True(room.Closed, "全员离场应收尾到终态");
            Assert.Equal(1, host.RoomCount);                  // 尚未销毁：仍在房间表
            Assert.Empty(host.RoomSummaries());               // 但上报面已过滤（本用例的修复点）

            host.Pump();
            Assert.Equal(0, host.RoomCount);
            Assert.Empty(host.RoomSummaries());
        }

        [Fact]
        public void 未终态房间_不销毁()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime room));
            h.t.StartMatchAsOwner(1);                         // 房主开局（满员不再自动开局）
            Assert.True(room.Started);

            host.Pump();

            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime same));
            Assert.Same(room, same);
            Assert.Equal(0, host.Ops.RoomsDestroyed);
            Assert.Equal(1, host.RoomCount);
        }

        [Fact]
        public void 开局前全员离场_房间收尾销毁_同房号可重新进房()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime idle));
            Assert.Equal(MatchPhase.WaitingForPlayers, idle.Phase);      // 未开局：等房主点开局

            h.t.RaiseData(1, PacketCodec.Encode(PacketType.Leave, new Proto.Leave()));
            Assert.True(idle.Closed, "开局前全员离场应收尾（未开局房不得永驻）");
            Assert.Equal(MatchPhase.Aborted, idle.Phase);
            Assert.Equal(0, host.Ops.SettlementsReady);                  // 未开局无结果可结算

            host.Pump();
            Assert.Equal(0, host.RoomCount);                             // 终态房间已从房间表移除
            Assert.Equal(1, host.Ops.RoomsDestroyed);

            // 会话路由已清 → 同房号可重新进房（拿到全新席位）。未收尾时这张 1 人房会因
            // NextPlayerId 只增不减而恒答"房间已满"——此断言即是"房间不再被幽灵席位占死"的判据。
            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-A"));
            Assert.NotNull(h.t.LastJoinAck(2));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime second));
            Assert.NotSame(idle, second);                                // 全新实例（不是复用旧房间状态）
            Assert.Equal(1, host.RoomCount);
        }

        [Fact]
        public void 未开局离场释放席位_新玩家复用空位_容量与上报口径不泄漏()
        {
            var h = NewWaitHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(2, Join("Room-A"));
            Assert.NotNull(h.t.LastJoinAck(1));
            Assert.NotNull(h.t.LastJoinAck(2));                          // 坐满（2/2）
            Assert.Equal(2, host.RoomSummaries()[0].Players);

            h.t.RaiseData(2, PacketCodec.Encode(PacketType.Leave, new Proto.Leave()));   // 未开局离场 → 释放
            host.Pump();

            var summary = host.RoomSummaries()[0];
            Assert.Equal(1, summary.Players);                            // 上报口径 = 在册席位（不虚报已离场者）
            Assert.Equal(1, host.OccupiedPlayerCount);                    // 会话路由已随席位释放复位

            h.t.RaiseConnected(3);
            h.t.RaiseData(3, Join("Room-A"));                            // 空位复用：不再"房间已满"
            var ack3 = h.t.LastJoinAck(3);
            Assert.NotNull(ack3);
            Assert.Equal(1, ack3.PlayerId);                              // 复用释放的 1 号位
            Assert.False(ack3.IsOwner);                                   // owner 仍在原房主（p0）
            Assert.Equal(1, host.RoomCount);                              // 房间全程未收尾（还有人在座）
        }

        [Fact]
        public void 未开局等待超时_中止并通知在座客户端_房间销毁()
        {
            var h = NewWaitHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            Assert.NotNull(h.t.LastJoinAck(1));
            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime room));
            Assert.Equal(MatchPhase.WaitingForPlayers, room.Phase);

            // 推泵越过等待时限（宿主单调时钟；模板 200ms）——超时 → Abort → 在座者收到终局信令
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!room.Closed && watch.ElapsedMilliseconds < 5000)
            {
                host.Pump();
                System.Threading.Thread.Sleep(10);
            }
            Assert.True(room.Closed, "等待超时应收尾到终态");
            Assert.Equal(MatchPhase.Aborted, room.Phase);

            var ended = (MatchEnded)h.t.Last(1, PacketType.MatchEnded);   // 问题2：复用 MatchEnded 的散场信令
            Assert.NotNull(ended);
            Assert.Equal((int)ShutdownReason.WaitingTimeout, ended.EndReason);
            Assert.Empty(ended.Players);                                  // 未开局无结果可结算
            Assert.Equal(0, host.Ops.SettlementsReady);

            host.Pump();
            Assert.Equal(0, host.RoomCount);                              // 终态房间已销毁
        }
    }
}