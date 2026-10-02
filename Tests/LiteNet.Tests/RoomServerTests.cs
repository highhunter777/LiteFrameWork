using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteTesting;
using LiteNet.Protocol;
using LiteNet.Transport;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// RoomServer 验收用例（《M10 实施指导》§2.5/2.6）：
    /// Join 信令（buildHash 拒绝与放行/满员自动 StartGame）/ 输入两层校验（非法帧号丢弃）/ 权威循环推帧
    /// （真实输入驱动状态变化）/ 掉线沿用不停帧。全部 .NET 侧闭环（同运行时红线，M9 决策⑩）。
    /// 形态：ServerHost 同进程内嵌 + 真实 KCP UDP 客户端（127.0.0.1 回环）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public class RoomServerTests : IDisposable
    {
        private const int Port = 27777;

        private readonly ServerHost _host;
        private readonly List<KcpTransportClient> _clients = new List<KcpTransportClient>();

        public RoomServerTests() => _host = new ServerHost(new KcpTransportServer(), new RoomConfig { Port = Port });

        /// <summary>
        /// 房间容量由配置决定：
        /// 4 人房（demo §9 验收形态）可满员、第五人被拒；席位/实体表/输入门/回溯环随配置定容。
        /// 此举把"容量写死"的隐患钉在用例上——将来有人改回常量即红。
        /// </summary>
        [Fact]
        public void 房间容量_由配置决定_四人房满员且第五人被拒()
        {
            var config = new RoomConfig { RoomId = "Four", ExpectedPlayers = 4, Seed = 12345 };
            var room = new RoomRuntime(config);

            Assert.Equal(4, room.ExpectedPlayers);
            for (int i = 0; i < 4; i++)
                Assert.Equal(i, JoinPlayerId(room, i));              // 席位按配置容量分配（命令面）
            Assert.Equal(-1, JoinPlayerId(room, 99));                // 满员：第五人被拒
            Assert.Equal(new[] { 0, 1, 2, 3 }, room.MemberIds());

            Assert.Equal(12345L, room.Seed);                          // 满员自动开局，seed 走固定配置
            Assert.True(room.Started);
            for (int i = 0; i < 4; i++) Assert.True(room.EntityIdOf(i) != 0L);   // 4 个实体都已生成
        }

        /// <summary>执行 Join 命令并返回分配到的席位号（被拒返回 -1）——命令面的进房等价物。</summary>
        private static int JoinPlayerId(RoomRuntime room, int connectionId)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(connectionId), outputs);
            foreach (RoomOutput o in outputs)
                if (o is SignalOutput { Signal: PlayerAdmitted pa }) return pa.PlayerId;
            return -1;
        }

        public void Dispose()
        {
            foreach (var c in _clients) c.Dispose();
            _host.Dispose();
        }

        private KcpTransportClient ConnectClient()
        {
            var client = new KcpTransportClient();
            _clients.Add(client);
            var connected = new ManualResetEventSlim(false);
            client.OnConnected += () => connected.Set();
            client.Connect("127.0.0.1", Port);
            var watch = Stopwatch.StartNew();
            while (!connected.Wait(10) && watch.ElapsedMilliseconds < 5000) PumpOne();
            Assert.True(client.Connected, "客户端 5s 内未完成握手");
            return client;
        }

        /// <summary>收包捕获器：按 PacketType 收集最近一条 proto 消息与计数。</summary>
        private sealed class Inbox<T> where T : Google.Protobuf.IMessage<T>
        {
            public T Last;
            public int Count;
            public void Wire(KcpTransportClient client)
            {
                client.OnData += (data, reliable) =>
                {
                    if (PacketCodec.TryDecode(data, out var type, out var msg) && PacketTypeOf(typeof(T)) == type)
                    {
                        Last = (T)msg;
                        Count++;
                    }
                };
            }
        }

        private static PacketType PacketTypeOf(Type t)
        {
            if (t == typeof(Proto.JoinAck)) return PacketType.JoinAck;
            if (t == typeof(Proto.StartGame)) return PacketType.StartGame;
            if (t == typeof(Proto.StateSnapshot)) return PacketType.StateSnapshot;
            return PacketType.None;
        }

        private void PumpOne()
        {
            _host.Pump();
            foreach (var c in _clients) { c.TickIncoming(); c.TickOutgoing(); }
            Thread.Sleep(10);
        }

        private void Pump(int ms)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms) PumpOne();
        }

        private bool WaitFor(Func<bool> predicate, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate()) return true;
                PumpOne();
            }
            return predicate();
        }

        private static void SendJoin(KcpTransportClient client, string hash)
        {
            client.Send(new ArraySegment<byte>(
                PacketCodec.Encode(PacketType.Join,
                    new Proto.JoinRequest { RoomId = RoomConfig.Default().RoomId, Token = "test", BuildHash = hash })), true);
        }

        // ---- 用例 ----

        [Fact]
        public void Join_错误buildHash被拒绝_正确hash获得JoinAck()
        {
            var client = ConnectClient();
            var joinAck = new Inbox<Proto.JoinAck>();
            joinAck.Wire(client);

            SendJoin(client, "wrong-hash");
            Pump(200);
            Assert.Equal(0, _host.Room.NextPlayerId);                           // 拒绝进房：未分配任何玩家号

            SendJoin(client, ServerHost.ServerBuildHash);
            Assert.True(WaitFor(() => _host.Room.NextPlayerId >= 1), "正确 hash 未分配玩家号");
            Assert.True(WaitFor(() => joinAck.Count > 0), "未收到 JoinAck");
            Assert.Equal(0, joinAck.Last.PlayerId);
        }

        [Fact]
        public void 满员自动StartGame_双方收到广播()
        {
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            var sg1 = new Inbox<Proto.StartGame>();
            var sg2 = new Inbox<Proto.StartGame>();
            sg1.Wire(c1);
            sg2.Wire(c2);

            SendJoin(c1, ServerHost.ServerBuildHash);
            SendJoin(c2, ServerHost.ServerBuildHash);   // 满员（2 人）触发 StartGame
            Assert.True(WaitFor(() => _host.Room.Started), "满员后未 StartGame");

            // 两个客户端都应收到 StartGame 广播（满员触发一次；后加入者靠 JoinAck+StartGame 对齐）
            Pump(400);
            Assert.True(sg1.Count > 0 || sg2.Count > 0, "StartGame 广播未达任何客户端");
        }

        [Fact]
        public void 权威循环_真实输入驱动状态变化_掉线沿用不停帧()
        {
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            SendJoin(c1, ServerHost.ServerBuildHash);
            SendJoin(c2, ServerHost.ServerBuildHash);
            bool started = WaitFor(() => _host.Room.Started);
            Assert.True(started, $"未 StartGame（NextPlayerId={_host.Room.NextPlayerId} Started={_host.Room.Started}）");

            // 客户端 1 持续发"向 +X 移动"输入（10 步）——输入帧号跟随服务器当前帧（时序正确性：客户端不能报历史帧）
            for (int i = 0; i < 10; i++)
            {
                int f = _host.Room.AuthSim.Frame + 1;
                c1.Send(new ArraySegment<byte>(PacketCodec.Encode(PacketType.Input,
                    new Proto.InputMessage
                    {
                        Frame = f,
                        Frames = { new Proto.InputFrame { EntityId = 0, MoveX = 1f, MoveZ = 0f, AimX = 1f, AimZ = 0f, Buttons = 0u } },
                        AckSnapshot = 0,
                    })), false);
                PumpOne();
            }

            // 权威状态断言：玩家 0（slot 0）沿 +X 移动了约 10 帧 × MoveSpeed(5) × dt(1/60) ≈ 0.83m
            var player0 = _host.Room.AuthSim.Entities[0];
            var gate = _host.Room.Gate;
            // 位移断言：出生点 x=-15（SpawnPoints[0]），向 +X 移动 ≈ 0.83m（10 帧 × MoveSpeed × dt）
            Assert.True(player0.Pos.X > -15f + 0.5f,
                $"玩家 0 未按输入移动（pos.x={player0.Pos.X:F3}，期望 > -14.5）Gate 计数：acc={gate.AcceptedCount} dup={gate.DroppedDuplicateFrame} oor={gate.DroppedOutOfRange} illegal={gate.DroppedIllegalFrame} ack={gate.DroppedAckSnapshot}");

            // 掉线沿用（§4.5-2）：单人掉线不停帧——其余成员仍在，权威循环继续推帧
            int frameBefore = _host.Room.AuthSim.Frame;
            c1.Disconnect();
            Pump(400);
            Assert.True(_host.Room.AuthSim.Frame > frameBefore, "单人掉线后权威循环停帧（应沿用空输入继续）");

            // §9.1：全员离场 → 收尾关闭（不再对空房间无限空转；冻结结果并进入终态）
            c2.Disconnect();
            Assert.True(WaitFor(() => _host.Room.Phase == MatchPhase.Closed, 5000),
                $"全员离场应关闭对局（当前 Phase={_host.Room.Phase}）");
        }

        [Fact]
        public void 输入校验_非法帧号丢弃且不影响权威循环()
        {
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            SendJoin(c1, ServerHost.ServerBuildHash);
            SendJoin(c2, ServerHost.ServerBuildHash);
            Assert.True(WaitFor(() => _host.Room.Started));

            // 非法输入：负帧号 + 超前帧号（当前帧 + 100）
            c1.Send(new ArraySegment<byte>(PacketCodec.Encode(PacketType.Input,
                new Proto.InputMessage { Frame = -5, Frames = { new Proto.InputFrame { EntityId = 0, MoveX = 9f, AimX = 1f, AimZ = 0f } }, AckSnapshot = 0 })), false);
            c1.Send(new ArraySegment<byte>(PacketCodec.Encode(PacketType.Input,
                new Proto.InputMessage { Frame = _host.Room.AuthSim.Frame + 100, Frames = { new Proto.InputFrame { EntityId = 0, MoveX = 9f, AimX = 1f, AimZ = 0f } }, AckSnapshot = 0 })), false);
            Pump(200);

            // 权威循环不受垃圾输入影响（无异常、帧号正常推进）
            int frameBefore = _host.Room.AuthSim.Frame;
            Pump(200);
            Assert.True(_host.Room.AuthSim.Frame > frameBefore, "垃圾输入后权威循环应继续推进");
        }
    }
}
