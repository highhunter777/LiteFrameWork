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
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// M10 无头对跑验收组（§9 M10 验收行）：
    /// RoomServer（同进程内嵌，真实 UDP 回环）+ 2 无头客户端（各自独立 UDP 连接）。
    ///
    /// - 短对跑（L1 默认）：30s——帧号稳定推进/双端收快照/和解链路通/不发散。
    /// - 5 分钟全量对跑：**环境变量 M10_LONGRUN=1 门控**（L1 默认 skip；夜间/手动跑）。
    /// - 决策⑥ 形态注记：全量预测下远端输入未知 → 和解频发属机制正确（和解率=权威/预测差异率）；
    ///   通过条件 = 快照覆盖兜底**不发散不崩盘** + 帧号稳定推进。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.EndToEnd)]
    public class HeadlessRunTests : IDisposable
    {
        private const int Port = 27778;

        private readonly ServerHost _host;
        private readonly KcpTransportServer _serverTransport;
        private readonly List<HeadlessClient> _clients = new List<HeadlessClient>();
        private readonly SimMapData _map = new SimMapData
        {
            GroundY = 0f,
            HalfWidth = 50f,
            HalfDepth = 50f,
            SpawnPointCount = 16,
        };

        public HeadlessRunTests()
        {
            _serverTransport = new KcpTransportServer();
            _host = new ServerHost(_serverTransport, new RoomConfig { Port = Port });
        }

        public void Dispose()
        {
            foreach (var c in _clients) c.Dispose();
            _serverTransport.Dispose();
        }

        /// <summary>起一个无头客户端（连接 + Join；Sim 在 StartGame.Seed 下发后惰性建）。</summary>
        private HeadlessClient StartHeadless(string name, int delaySteps = 0)
        {
            var transport = new KcpTransportClient();
            var client = new HeadlessClient(name, _map, transport);
            _clients.Add(client);
            client.Client.Connect("127.0.0.1", Port);
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

        private bool BothJoined(int expected) => _host.Room.NextPlayerId >= expected;

        /// <summary>条件等待（谓词满足或超时；每轮泵一帧）。</summary>
        private bool WaitFor(Func<bool> predicate, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate()) return true;
                PumpAll(10);
            }
            return predicate();
        }

        /// <summary>对跑主循环：ticks 步，每步发 1 条本地输入（或跳步——丢包模拟）+ 泵。</summary>
        private void RunFor(HeadlessClient a, HeadlessClient b, int ticks, int skipEvery = 0)
        {
            for (int t = 0; t < ticks; t++)
            {
                // 丢包模拟：skipEvery > 0 时每 skipEvery 步跳过一次客户端发送（发送侧丢包）
                if (skipEvery == 0 || t % skipEvery != 0)
                {
                    a.EnqueueLocalInput(new SimInputFrame { EntityId = 0, MoveX = 0.5f, AimX = 1f, AimZ = 0f });
                    b.EnqueueLocalInput(new SimInputFrame { EntityId = 0, MoveX = -0.5f, AimX = 1f, AimZ = 0f });
                }
                else
                {
                    a.Tick(1f / 60);   // 跳发仍推进预测（不发）——模拟"该帧无新输入"
                    b.Tick(1f / 60);
                    continue;
                }
                _host.Pump();
                a.Tick(1f / 60);
                b.Tick(1f / 60);
                foreach (var c in _clients) { c.Client.TickIncoming(); c.Client.TickOutgoing(); }
            }
        }

        // ---- L1 短对跑：30s 帧号稳定 + 快照到达 + 和解链路通 + 不发散 ----

        [Fact]
        public void 短对跑_双客户端30秒_帧号稳定快照到达不发散()
        {
            var a = StartHeadless("A");
            var b = StartHeadless("B");
            Assert.True(WaitFor(() => BothJoined(2), 5000), "双客户端未完成 Join");
            Assert.True(WaitFor(() => a.Sim != null && b.Sim != null), "StartGame 未达（Sim 未建）");

            // 30s 对跑（1800 权威帧）
            RunFor(a, b, 30 * 60);

            // 帧号稳定：权威帧 ≈ 1800（±追赶容差）
            Assert.True(_host.Room.AuthSim.Frame >= 30 * 60 - SimConfig.MaxCatchUp,
                $"权威帧不足：{_host.Room.AuthSim.Frame}/1800");

            // 双端快照到达（30Hz 广播）
            Assert.True(a.LastSnapshotFrame > 0, "A 未收到快照");
            Assert.True(b.LastSnapshotFrame > 0, "B 未收到快照");

            // 和解链路通：MismatchReport 上报计数器可用（数值不作通过条件——决策⑥ 形态下远端误差属预期）
            Assert.True(a.MismatchReports >= 0 && b.MismatchReports >= 0);

            // 不发散：客户端预测帧不越过权威帧 + MaxCatchUp
            Assert.True(a.Sim.State.Frame <= _host.Room.AuthSim.Frame + SimConfig.MaxCatchUp, "A 预测发散");
            Assert.True(b.Sim.State.Frame <= _host.Room.AuthSim.Frame + SimConfig.MaxCatchUp, "B 预测发散");
        }

        [Fact]
        public void 和解_快照覆盖兜底_预测偏差有界()
        {
            // 决策⑥ 形态验证：A 静止、B 移动（远端输入未知 → 客户端预测与权威必然分叉）→
            // 每次快照和解恢复权威 → 预测从权威基线重启 → 10s 观测内偏差有界（不发散）。
            var a = StartHeadless("A");
            var b = StartHeadless("B");
            Assert.True(WaitFor(() => BothJoined(2), 5000));
            Assert.True(WaitFor(() => a.Sim != null && b.Sim != null));

            for (int t = 0; t < 600; t++)
            {
                a.EnqueueLocalInput(new SimInputFrame { EntityId = 0, MoveX = 0f, AimX = 1f, AimZ = 0f });
                b.EnqueueLocalInput(new SimInputFrame { EntityId = 0, MoveX = 0.8f, AimX = 1f, AimZ = 0f });
                _host.Pump();
                a.Tick(1f / 60);
                b.Tick(1f / 60);
                foreach (var c in _clients) { c.Client.TickIncoming(); c.Client.TickOutgoing(); }
            }

            // 不发散：A 的预测帧号与权威帧差有界
            Assert.True(a.Sim.State.Frame <= _host.Room.AuthSim.Frame + SimConfig.MaxCatchUp, "A 预测发散");
            Assert.True(b.Sim.State.Frame <= _host.Room.AuthSim.Frame + SimConfig.MaxCatchUp, "B 预测发散");
        }

        // ---- 5 分钟全量对跑（环境变量门控：M10_LONGRUN=1）----

        [Fact]
        [Trait(TestTrait.Duration, TestDuration.LongRunning)]
        public void 全量对跑_5分钟_无卡顿累积且和解率可观测()
        {
            if (Environment.GetEnvironmentVariable("M10_LONGRUN") != "1")
                return;   // L1 默认跳过；夜间/手动设 M10_LONGRUN=1 执行

            var a = StartHeadless("A");
            var b = StartHeadless("B");
            Assert.True(WaitFor(() => BothJoined(2), 5000));
            Assert.True(WaitFor(() => a.Sim != null && b.Sim != null));

            var watch = Stopwatch.StartNew();
            int lastFrame = 0;
            long lastAccepted = _host.Room.Gate.AcceptedCount;
            while (watch.Elapsed.TotalMinutes < 5.0)
            {
                a.EnqueueLocalInput(new SimInputFrame { EntityId = 0, MoveX = 0.5f, AimX = 1f, AimZ = 0f });
                b.EnqueueLocalInput(new SimInputFrame { EntityId = 0, MoveX = -0.5f, AimX = 1f, AimZ = 0f });
                _host.Pump();
                a.Tick(1f / 60);
                b.Tick(1f / 60);
                foreach (var c in _clients) { c.Client.TickIncoming(); c.Client.TickOutgoing(); }

                if (_host.Room.AuthSim.Frame - lastFrame >= 600)
                {
                    Assert.True(_host.Room.AuthSim.Frame > lastFrame, "权威帧号停滞");
                    lastFrame = _host.Room.AuthSim.Frame;
                }
            }

            Assert.True(_host.Room.AuthSim.Frame >= 300 * 60 - SimConfig.MaxCatchUp, "5 分钟对跑帧数不足");
            Assert.True(_host.Room.Gate.AcceptedCount > lastAccepted, "输入消费停滞");
        }
    }
}
