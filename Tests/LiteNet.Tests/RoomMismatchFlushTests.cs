using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LiteSim;
using LiteSim.Tests;
using RoomServer;
using RoomServer.Diagnostics;
using RoomServer.Runtime;
using RoomServer.Runtime.Diagnostics;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// T0-2b：mismatch 即时 flush——房间线程封印快照（录制继续）、冷却判定、
    /// 后台线程组装信封落盘（EndReason=mismatch-flush）、终态记录独立兜底。
    /// </summary>
    public sealed class RoomMismatchFlushTests
    {
        private const string RoomId = "FlushTest";

        private static RoomConfig Config() => new RoomConfig
        {
            Map = TestMaps.Greybox(),
            RoomId = RoomId,
            Seed = 20261023L,
            ExpectedPlayers = 2,
            MatchTimeLimitMs = 0,
            WaitingTimeoutMs = 0,
            Rules = MatchRules.Default,
        };

        private static void Start(RoomRuntime room)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            room.Execute(RoomCommand.StartEarly(0), outputs);
        }

        private static void StepFrames(RoomRuntime room, int from, int to)
        {
            var outputs = new List<RoomOutput>();
            for (int frame = from; frame <= to; frame++)
            {
                var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
                frames[0] = new SimInputFrame { MoveX = frame % 2 == 0 ? 1f : 0f };
                room.Execute(RoomCommand.ClientInput(0, new ClientInputBatch
                {
                    Frame = frame, AckSnapshot = frame - 1, ViewFrame = frame, Count = 1, Frames = frames,
                }), outputs);
                room.Execute(RoomCommand.Tick(frame * 16L), outputs);
            }
        }

        [Fact]
        public void 快照请求_房间线程下一帧消费_录制继续()
        {
            var snapshots = new List<RoomRunRecord>();
            var recorder = new RoomRunRecorder(RoomId, RoomId + ":m", expectedPlayers: 2,
                onSnapshot: snapshots.Add);
            var room = new RoomRuntime(Config(), matchId: RoomId + ":m", recorder: recorder);

            Start(room);
            StepFrames(room, 1, 10);
            Assert.Empty(snapshots);

            recorder.RequestSnapshot();        // 传输线程姿态：置位即返回，不等房间
            StepFrames(room, 11, 20);          // 下一 StepFrame 末消费

            Assert.Single(snapshots);
            Assert.Equal(11, snapshots[0].StepCount);   // 消费点 = 第 11 帧步进之后
            Assert.Equal(RoomId, snapshots[0].RoomId);

            RoomRunRecord final = room.TakeRunRecord();
            Assert.Equal(20, final.StepCount);          // 快照不影响录制继续
            Assert.NotSame(final, snapshots[0]);
        }

        [Fact]
        public void 快照请求_无回调时为无操作()
        {
            var recorder = new RoomRunRecorder(RoomId, RoomId + ":m", expectedPlayers: 2);
            recorder.RequestSnapshot();
            recorder.PollSnapshot();   // 不抛、不炸
            Assert.True(true);
        }

        [Fact]
        public void mismatch即时flush_信封后台落盘_终态记录独立()
        {
            string dir = Path.Combine(Path.GetTempPath(), "diag-flush-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new RoomDiagnosticsOptions(enabled: true, directory: dir, maxFiles: 10,
                    maxTotalMb: 16, maxCommands: 100_000, maxSteps: 100_000, logTailLines: 50,
                    worldRingFrames: 8);
                var service = new RoomDiagnosticsService(options, "build-flush");
                try
                {
                    var config = Config();
                    var inst = new RoomInstance(config, diagnostics: options, diagnosticsService: service);

                    Start(inst.Runtime);
                    StepFrames(inst.Runtime, 1, 12);

                    // 和解上报路径（传输线程姿态）：记录 → 冷却放行一次 → 房间上置位快照请求
                    service.NoteMismatch(RoomId, 12);
                    Assert.True(service.ShouldRequestFlush(RoomId));
                    Assert.False(service.ShouldRequestFlush(RoomId));   // 冷却期内不重复
                    inst.Runtime.RequestRecordSnapshot();
                    StepFrames(inst.Runtime, 13, 20);   // 第 13 帧末封印快照

                    Assert.True(WaitUntil(() => service.ListFiles().Count >= 1, TimeSpan.FromSeconds(2)),
                        "后台写线程未在时限内落盘");

                    string flushPath = service.ListFiles()[0];
                    RoomRunEnvelope flushEnvelope = RoomRunCodec.Decode(File.ReadAllBytes(flushPath));
                    Assert.Equal("mismatch-flush", flushEnvelope.EndReason);
                    Assert.Equal("build-flush", flushEnvelope.BuildHash);
                    Assert.Equal(13, flushEnvelope.Record.ToRecord().StepCount);
                    Assert.NotNull(flushEnvelope.ClientMismatch);
                    Assert.Equal(12, flushEnvelope.ClientMismatch.Frame);
                    Assert.Equal(1, flushEnvelope.ClientMismatch.Reports);

                    // 终态记录独立兜底：全量步数 + mismatch 摘要仍在（flush 只窥视不摘除）
                    string finalPath = service.TryWrite(RoomId, inst.Runtime, inst.Config,
                        0u, inst.CreatedAtUtc, "test-end");
                    Assert.NotNull(finalPath);
                    RoomRunEnvelope finalEnvelope = RoomRunCodec.Decode(File.ReadAllBytes(finalPath));
                    Assert.Equal("test-end", finalEnvelope.EndReason);
                    Assert.Equal(20, finalEnvelope.Record.ToRecord().StepCount);
                    Assert.NotNull(finalEnvelope.ClientMismatch);
                }
                finally
                {
                    service.Dispose();
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(25);
            }
            return condition();
        }
    }
}
