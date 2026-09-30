using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteTesting;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 包④ 全链路联合记录（《框架先行》§6 样例④"两个真实客户端 → 测试房间 → 移动/快照 →
    /// 断线重连 → **离场**"；§8 准入项「联机宿主闭环」"入房、同步、重连、**离场**、两房间隔离和
    /// 关闭排空通过"，证据类型"**纯运行时与真实传输/客户端联合记录**"）。
    ///
    /// 与邻近三组的分工：
    /// - <see cref="HeadlessRunTests"/>——双客户端跑**完整链路**（含重连），但**全在同一房间**，且只有到 M10 短对跑；
    /// - <see cref="MultiRoomTransportTests"/>——**真实传输**，但只到"进房＋隔离＋排空拒绝"，没跑链路；
    /// - 本组 = 两者的交集：**两房并行 + 每房跑完整链路（入房→移动/快照→断线重连→离场）**。
    ///
    /// **不跑结算**：完整结算信封/归档归 R3（§42），框架期样例④终点是"离场"。此处如实标注，
    /// 不以"跑到结算"冒充框架期要求。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.EndToEnd)]
    public sealed class TwoRoomFullChainTests : IDisposable
    {
        private const int Port = 28991;

        /// <summary>2 人房模板 + 可配容量；对局时限 30s（够跑完整链路，且不会提前收尾）。</summary>
        private static readonly string Config = @"{
            ""port"": " + Port + @", ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""two"",
            ""combat"": [ { ""id"":1, ""move_speed"":5, ""gravity"":-20, ""hitscan_range"":100,
                           ""hitscan_radius"":0.5, ""hitscan_height"":2, ""base_damage"":25,
                           ""damage_spread"":1, ""entity_hp"":100 } ],
            ""rooms"": { ""two"": { ""expected_players"": 2, ""match_time_limit_ms"": 30000 } }
        }";

        private static readonly SimMapData Map = new SimMapData
        {
            GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f, SpawnPointCount = 16,
        };

        private readonly ServerHost _host;
        private readonly KcpTransportServer _serverTransport;
        private readonly List<HeadlessClient> _clients = new List<HeadlessClient>();

        public TwoRoomFullChainTests()
        {
            var cfg = RoomServerConfig.Parse(Config);
            _serverTransport = new KcpTransportServer();
            _host = new ServerHost(_serverTransport, null, null, null, cfg);
        }

        public void Dispose()
        {
            foreach (var c in _clients) c.Dispose();
            // ServerHost owns Worker Pool + Transport；先走宿主收尾，避免配置驱动的固定池遗留后台线程。
            _host.Dispose();
        }

        private HeadlessClient StartIn(string name, string roomId)
        {
            var client = new HeadlessClient(name, Map, new KcpTransportClient(), roomId, Port);
            _clients.Add(client);
            return client;
        }

        private void PumpAll(int ms)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms)
            {
                _host.Pump();
                foreach (var c in _clients) { c.Client.TickIncoming(); c.Client.TickOutgoing(); }
                Thread.Sleep(2);
            }
        }

        private bool WaitFor(Func<bool> predicate, int timeoutMs = 8000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate()) return true;
                PumpAll(10);
            }
            return predicate();
        }

        /// <summary>对跑若干帧：每步两端各发一条本地输入并泵一帧。</summary>
        private void RunFor(int ticks, HeadlessClient a, float ax, HeadlessClient b, float bx)
        {
            for (int t = 0; t < ticks; t++)
            {
                a.EnqueueLocalInput(new SimInputFrame { EntityId = 0, MoveX = ax, AimX = 1f, AimZ = 0f });
                b.EnqueueLocalInput(new SimInputFrame { EntityId = 0, MoveX = bx, AimX = 1f, AimZ = 0f });
                _host.Pump();
                a.Tick(1f / 60);
                b.Tick(1f / 60);
                foreach (var c in _clients) { c.Client.TickIncoming(); c.Client.TickOutgoing(); }
            }
        }

        /// <summary>让四个客户端都完成 Join 且各自房间开局。</summary>
        private (HeadlessClient a1, HeadlessClient a2, HeadlessClient b1, HeadlessClient b2) StartBothRooms()
        {
            var a1 = StartIn("A1", "Room-A");
            var a2 = StartIn("A2", "Room-A");
            var b1 = StartIn("B1", "Room-B");
            var b2 = StartIn("B2", "Room-B");

            Assert.True(WaitFor(() => a1.Sim != null && a2.Sim != null && b1.Sim != null && b2.Sim != null),
                "四端未全部收到 StartGame（两房应各自满员开局）");
            return (a1, a2, b1, b2);
        }

        // ---- 用例 ----

        [Fact]
        public void 两房并行_四客户端各跑完整链路_入房到离场()
        {
            var (a1, a2, b1, b2) = StartBothRooms();
            Assert.True(_host.TryGetRoom("Room-A", out RoomRuntime roomA));
            Assert.True(_host.TryGetRoom("Room-B", out RoomRuntime roomB));
            Assert.True(roomA.Started && roomB.Started, "两房都应满员开局");

            // ① 同步：两房并行对跑 5s
            RunFor(300, a1, 0.5f, a2, -0.5f);
            RunFor(300, b1, 0.4f, b2, -0.4f);

            Assert.True(a1.LastSnapshotFrame > 0 && a2.LastSnapshotFrame > 0, "A 房两端应收快照");
            Assert.True(b1.LastSnapshotFrame > 0 && b2.LastSnapshotFrame > 0, "B 房两端应收快照");
            Assert.True(roomA.AuthSim.Frame > 0, "A 房应持续推进");
            Assert.True(roomB.AuthSim.Frame > 0, "B 房应持续推进");

            // ② 重连：A 房一端断线重连（B 房不受影响，继续跑）
            int b1FrameAtDrop = b1.LastSnapshotFrame;
            a1.Client.Disconnect();
            Assert.True(WaitFor(() => a1.Client.Phase == ClientSessionPhase.SuspectedLost, 8000), "A1 未感知断线");
            Assert.True(WaitFor(() => roomA.SeatOf(0).Phase == SeatPhase.Disconnected, 8000), "服务器未观察到 A1 断线");
            Assert.True(WaitFor(() => b1.LastSnapshotFrame > b1FrameAtDrop, 8000), "B 房不应因 A 房间断线中断");

            Assert.True(a1.Client.BeginReconnect(), "重连请求未被接受");
            Assert.True(WaitFor(() => a1.Client.Phase == ClientSessionPhase.Connected, 15000), "A1 重连未完成");
            Assert.True(WaitFor(() => roomA.SeatOf(0).Phase == SeatPhase.Active, 8000), "A1 席位未回 Active");
            Assert.Equal(0, a1.Client.PlayerId);   // 原席位不变

            // 重连后 A 房继续同步
            int a1FrameAtRestore = a1.LastSnapshotFrame;
            RunFor(120, a1, 0.5f, a2, -0.5f);
            Assert.True(WaitFor(() => a1.LastSnapshotFrame > a1FrameAtRestore, 8000), "重连后 A1 未继续收快照");

            // ③ 两房仍互相独立：席位与帧号都不串
            Assert.Equal(2, roomA.NextPlayerId);
            Assert.Equal(2, roomB.NextPlayerId);
            Assert.NotSame(roomA.AuthSim, roomB.AuthSim);

            // ④ 离场：A 房全员离场 → 该房收尾；B 房不受影响
            int b2FrameAtLeave = b2.LastSnapshotFrame;
            a1.Client.Disconnect();
            a2.Client.Disconnect();
            PumpAll(400);

            Assert.True(WaitFor(() => roomA.Phase == MatchPhase.Closed, 8000),
                $"A 房全员离场应收尾（当前 {roomA.Phase}）");
            Assert.Equal(MatchPhase.Running, roomB.Phase);                    // B 房照常
            Assert.True(WaitFor(() => b2.LastSnapshotFrame > b2FrameAtLeave, 8000), "B 房广播不应受 A 房收尾影响");

            // ⑤ B 房也离场 → 收尾
            b1.Client.Disconnect();
            b2.Client.Disconnect();
            PumpAll(400);
            Assert.True(WaitFor(() => roomB.Phase == MatchPhase.Closed, 8000),
                $"B 房全员离场应收尾（当前 {roomB.Phase}）");
        }

        [Fact]
        public void 两房并行_一端离场另一房对局不受影响()
        {
            var (a1, a2, b1, b2) = StartBothRooms();
            _host.TryGetRoom("Room-A", out RoomRuntime roomA);
            _host.TryGetRoom("Room-B", out RoomRuntime roomB);

            RunFor(180, a1, 0.5f, a2, -0.5f);
            int aFrameBefore = roomA.AuthSim.Frame;
            int bFrameBefore = roomB.AuthSim.Frame;

            // A 房只走一人 → 对局继续进行（掉线不停帧，非终态）
            a1.Client.Disconnect();
            PumpAll(300);

            Assert.Equal(MatchPhase.Running, roomA.Phase);
            Assert.True(roomA.AuthSim.Frame > aFrameBefore, "A 房单人离场后应继续推帧");
            Assert.True(roomB.AuthSim.Frame > bFrameBefore, "B 房应不受影响");

            // A 房留者仍能收快照（广播对剩余席位照常）
            int a2Frame = a2.LastSnapshotFrame;
            RunFor(60, a2, -0.5f, b1, 0.4f);
            Assert.True(WaitFor(() => a2.LastSnapshotFrame > a2Frame, 8000), "A 房剩余席位应继续收快照");
        }

        [Fact]
        public void 两房并行_排空期间既有对局继续_新进房被拒()
        {
            var (a1, a2, b1, b2) = StartBothRooms();
            _host.TryGetRoom("Room-A", out RoomRuntime roomA);
            _host.TryGetRoom("Room-B", out RoomRuntime roomB);
            RunFor(120, a1, 0.5f, a2, -0.5f);

            _host.BeginDrain(_host.CurrentMs + 30_000);

            int aFrame = roomA.AuthSim.Frame;
            int bFrame = roomB.AuthSim.Frame;
            RunFor(120, b1, 0.4f, b2, -0.4f);
            PumpAll(200);

            // 排空不冻结既有对局（§12 第 2 步是"停止**新** Join"）
            Assert.True(roomA.AuthSim.Frame > aFrame, "排空期间 A 房对局应继续");
            Assert.True(roomB.AuthSim.Frame > bFrame, "排空期间 B 房对局应继续");

            // 新客户端进房被拒
            var late = StartIn("Late", "Room-C");
            PumpAll(500);
            Assert.Null(late.Sim);
            Assert.False(_host.TryGetRoom("Room-C", out _), "排空后不应建出新房间");
            Assert.True(_host.Ops.RejectsWhileDraining >= 1);
        }
    }
}
