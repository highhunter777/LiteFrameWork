using System.Collections.Generic;
using LiteTesting;
using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 优雅关闭／排空用例（《商业级通用服务端框架总设计》§12"优雅关闭"五步；《框架先行》§6 样例④
    /// "…**drain**"、§8 准入项「联机宿主闭环」"…和**关闭排空**通过"）。
    ///
    /// **设计五步与本实现的覆盖**：
    /// 1. readiness 置 false，Lobby 不再分配——本服务无 Lobby/实例注册，等价语义是本进程 `Draining`；
    /// 2. 停止接受新 Join，现有房间进入 drain；
    /// 3. 限时完成对局，超时归档 Aborted；
    /// 4. 刷新 Outbox/Archive 到持久介质——装配 ISettlementOutbox 时 SettlementReady 逐条 write-through 落盘，
    ///    DrainComplete 后 FlushSettlementOutbox 收口；
    /// 5. 停止 Worker/Transport/Host——`Dispose` 按 Worker → Transport → Outbox 顺序收尾。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class DrainTests
    {
        private const string Config = @"{
            ""port"": 46001, ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private static (ServerHost host, FakeRoomTransport t, RoomServerConfig cfg) NewHost()
        {
            var cfg = RoomServerConfig.Parse(Config);
            var t = new FakeRoomTransport();
            return (new ServerHost(t, null, null, null, cfg), t, cfg);
        }

        private static byte[] Join(string roomId) => PacketCodec.Encode(PacketType.Join,
            new JoinRequest { RoomId = roomId, Token = "tok", BuildHash = ServerHost.ServerBuildHash });

        // ---- 第 2 步：停止接受新 Join ----

        [Fact]
        public void 排空后_新进房一律拒绝_含已建房间()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));             // 排空前：正常进房
            Assert.NotNull(h.t.LastJoinAck(1));

            host.BeginDrain(10_000);

            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-A"));             // 排空后：同一房间也拒
            Assert.Null(h.t.LastJoinAck(2));
            Assert.Equal(1, host.Ops.RejectsWhileDraining);
            Assert.Equal(1, host.Ops.Rejects);            // Reject() 记的总数，不重复计
        }

        [Fact]
        public void 排空后_不再建新房间()
        {
            var h = NewHost();
            using var host = h.host;
            host.BeginDrain(10_000);

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Brand-New-Room"));

            Assert.Null(h.t.LastJoinAck(1));
            Assert.Equal(0, host.RoomCount);
            Assert.False(host.TryGetRoom("Brand-New-Room", out _));
        }

        [Fact]
        public void 排空不影响既有对局的输入与推帧()
        {
            // §12 第 2 步是"停止**新** Join"，不是冻结对局——已在局内的继续跑。
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1); h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(2, Join("Room-A"));             // 满员开局
            host.TryGetRoom("Room-A", out RoomRuntime room);
            Assert.True(room.Started);

            host.BeginDrain(10_000);
            int frameBefore = room.AuthSim.Frame;
            for (int i = 0; i < 5; i++) host.Pump();

            Assert.True(room.AuthSim.Frame > frameBefore, "排空期间对局应继续推进");
            Assert.False(room.Closed);
        }

        [Fact]
        public void 排空后重连_票据有效则正常恢复()
        {
            // 排空拒的是新进房；已在册席位凭一次性票据重连是**既有对局**的一部分，不受第 2 步影响。
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1); h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(2, Join("Room-A"));
            JoinAck ack = h.t.LastJoinAck(1);
            Assert.NotNull(ack);

            host.BeginDrain(10_000);
            h.t.RaiseDisconnected(1);
            h.t.RaiseConnected(3);
            h.t.RaiseData(3, PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = ack.ReconnectToken }));

            ReconnectResponse resp = (ReconnectResponse)h.t.Last(3, PacketType.ReconnectResponse);
            Assert.NotNull(resp);
            Assert.True(resp.Ok, $"排空期重连应放行，实际拒绝：{resp.Reason}");
        }

        // ---- 第 3 步：限时完成；超时强制关闭 ----

        [Fact]
        public void 排空期间对局自然收尾_顺利完成()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1); h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(2, Join("Room-A"));
            host.TryGetRoom("Room-A", out RoomRuntime room);

            host.BeginDrain(10_000);
            for (int i = 0; i < 10; i++) host.Pump();
            // 全员离场 → 自然收尾（§9.1）——这条路径**不是**排空超时
            h.t.RaiseDisconnected(1);
            h.t.RaiseDisconnected(2);
            for (int i = 0; i < 5; i++) host.Pump();

            Assert.True(room.Closed, "全员离场应收尾");
            Assert.True(host.DrainComplete, "所有房间终态 → 排空完成");
            Assert.Equal(0, host.Ops.RoomsDrainTimedOut);   // 自然收敛，非超时
        }

        [Fact]
        public void 排空超时_未收敛的房间被强制关闭并归类DrainTimeout()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1); h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(2, Join("Room-A"));             // 对局进行中，不会自己结束
            host.TryGetRoom("Room-A", out RoomRuntime room);

            // 截止时刻取"现在"——下一帧即超时
            host.BeginDrain(0);
            for (int i = 0; i < 3; i++) host.Pump();

            Assert.True(room.Closed, "超时应强制关闭");
            Assert.Equal(1, host.Ops.RoomsDrainTimedOut);
            Assert.True(host.DrainComplete);
        }

        [Fact]
        public void 排空超时关闭_按Running归宿结算而非Aborted()
        {
            // §9.1：Running 期 Shutdown → Finishing→Settling→Closed 并产结算；
            // 未开局才走 Aborted。排空超时不该被当成"异常中止"。
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1); h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(2, Join("Room-A"));
            host.TryGetRoom("Room-A", out RoomRuntime room);

            host.BeginDrain(0);
            for (int i = 0; i < 3; i++) host.Pump();

            Assert.Equal(MatchPhase.Closed, room.Phase);   // 不是 Aborted
            Assert.True(room.Closed);
        }

        [Fact]
        public void 未开局的房间排空超时_走Aborted()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));             // 只 1 人，未开局
            host.TryGetRoom("Room-A", out RoomRuntime room);
            Assert.False(room.Started);

            host.BeginDrain(0);
            for (int i = 0; i < 3; i++) host.Pump();

            Assert.Equal(MatchPhase.Aborted, room.Phase);
            Assert.True(room.Closed);
        }

        [Fact]
        public void 关服不影响已终态房间_重复关闭幂等()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            host.TryGetRoom("Room-A", out RoomRuntime room);
            room.Execute(RoomCommand.Shutdown(ShutdownReason.Operator), new List<RoomOutput>());
            Assert.True(room.Closed);

            host.BeginDrain(0);
            for (int i = 0; i < 3; i++) host.Pump();

            Assert.Equal(0, host.Ops.RoomsDrainTimedOut);  // 已终态不再被强制关闭
            Assert.True(host.DrainComplete);
        }

        // ---- 排空完成与幂等 ----

        [Fact]
        public void 排空完成_判据是所有在册房间都到终态()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1); h.t.RaiseConnected(2);
            h.t.RaiseConnected(3); h.t.RaiseConnected(4);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(2, Join("Room-A"));              // A 房满员开局
            h.t.RaiseData(3, Join("Room-B"));
            h.t.RaiseData(4, Join("Room-B"));              // B 房满员开局

            host.BeginDrain(0);
            Assert.False(host.DrainComplete, "两房都在跑 → 未完成");
            for (int i = 0; i < 3; i++) host.Pump();

            Assert.Equal(2, host.Ops.RoomsDrainTimedOut);
            Assert.True(host.DrainComplete);
        }

        [Fact]
        public void BeginDrain幂等_重复调用不延后已定期限()
        {
            var h = NewHost();
            using var host = h.host;
            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));

            host.BeginDrain(5_000);
            Assert.Equal(5_000, host.DrainDeadlineMs);

            host.BeginDrain(9_000);                        // 更晚 → 不生效
            Assert.Equal(5_000, host.DrainDeadlineMs);

            host.BeginDrain(1_000);                        // 更早 → 前移（排空期限只可提前）
            Assert.Equal(1_000, host.DrainDeadlineMs);
            Assert.True(host.Draining);
        }

        [Fact]
        public void 未排空时_DrainComplete为假()
        {
            var h = NewHost();
            using var host = h.host;
            Assert.False(host.Draining);
            Assert.False(host.DrainComplete);
            Assert.Equal(-1, host.DrainDeadlineMs);
        }

        [Fact]
        public void 排空后所有房间终态_无残留可推进房间()
        {
            var h = NewHost();
            using var host = h.host;
            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            host.TryGetRoom("Room-A", out RoomRuntime room);

            host.BeginDrain(0);
            for (int i = 0; i < 3; i++) host.Pump();
            Assert.True(host.DrainComplete);

            // 终态房间已销毁（§42）：房间表清零，且完成后继续 Pump 不得复活任何房间
            Assert.False(host.TryGetRoom("Room-A", out _));
            Assert.Equal(0, host.RoomCount);
            int frame = room.AuthSim.Frame;
            for (int i = 0; i < 5; i++) host.Pump();
            Assert.Equal(frame, room.AuthSim.Frame);
        }

        // ---- 第 4 步：刷新 Outbox 到持久介质 ----

        [Fact]
        public void 排空超时收尾_结算入盒落盘_第4步收口()
        {
            // 全链：满员开局 → 排空超时强制收尾 → SettlementReady → 本地持久 Outbox（真实临时文件）
            // → DrainComplete → FlushSettlementOutbox（§12 第 4 步）→ 介质可读、重启可重放。
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "litegame_drain_" + System.Guid.NewGuid().ToString("N"));
            string journal = System.IO.Path.Combine(dir, "settlements.journal");
            var cfg = RoomServerConfig.Parse(Config);
            var t = new FakeRoomTransport();
            using (var outbox = FileSettlementOutbox.Open(journal, 16))
            using (var host = new ServerHost(t, null, null, null, cfg, outbox))
            {
                host.Ops.PrintEnabled = false;

                t.RaiseConnected(1); t.RaiseConnected(2);
                t.RaiseData(1, Join("Room-A"));
                t.RaiseData(2, Join("Room-A"));
                host.TryGetRoom("Room-A", out RoomRuntime room);
                Assert.True(room.Started);

                host.BeginDrain(0);
                host.Pump();
                Assert.True(room.Closed);
                Assert.True(host.DrainComplete);

                // SettlementReady 即入盒（write-through——收口前已在介质上）
                Assert.Equal(1, host.Ops.SettlementsReady);
                Assert.Equal(1, host.Ops.SettlementsJournaled);
                Assert.Equal(1, host.SettlementOutboxPending);

                host.FlushSettlementOutbox();     // §12 第 4 步收口（幂等）
                host.FlushSettlementOutbox();
            }

            try
            {
                // 介质事实：文件在、内容含房间号
                Assert.True(System.IO.File.Exists(journal));
                Assert.Contains("Room-A", System.IO.File.ReadAllText(journal));

                // 重启恢复：新实例续接——待提交不丢
                using (var restarted = FileSettlementOutbox.Open(journal, 16))
                {
                    Assert.Equal(1, restarted.Count);
                    Assert.Equal(t.LastStartGame(1).MatchId, restarted.ListPending()[0].MatchId);
                }
            }
            finally
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
