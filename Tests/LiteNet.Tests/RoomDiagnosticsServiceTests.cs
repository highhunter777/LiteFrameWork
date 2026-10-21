using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// 服务端现场包：终态取记录 → 补身份/日志尾/mismatch → 原子落盘 → 可解码。
    /// 锁的是“有记录、有身份、有日志、能读回”，而不是只断言文件存在。
    /// </summary>
    public sealed class RoomDiagnosticsServiceTests
    {
        private const string RoomId = "DiagTest";
        private const string MatchId = "DiagTest:match";
        private const long Seed = 20261021L;

        private static RoomConfig Config() => new RoomConfig
        {
            Map = TestMaps.Greybox(),
            RoomId = RoomId,
            Seed = Seed,
            ExpectedPlayers = 2,
            Rules = MatchRules.Default,
        };

        private static RoomRuntime BuildRecordedRoom()
        {
            var recorder = new RoomRunRecorder(RoomId, MatchId, expectedPlayers: 2,
                maxCommands: 10_000, maxSteps: 10_000);
            var room = new RoomRuntime(Config(), matchId: MatchId, recorder: recorder);
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            room.Execute(RoomCommand.StartEarly(0), outputs);
            for (int frame = 1; frame <= 20; frame++)
            {
                var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
                frames[0] = new SimInputFrame { MoveX = 0.5f };
                room.Execute(RoomCommand.ClientInput(0, new ClientInputBatch
                {
                    Frame = frame, AckSnapshot = frame - 1, ViewFrame = frame, Count = 1, Frames = frames,
                }), outputs);
                room.Execute(RoomCommand.Tick(frame * 16L), outputs);
            }
            return room;
        }

        [Fact]
        public void 配置解析_diagnostics分区_绝对路径与上限()
        {
            const string json = @"{
                ""port"": 0, ""max_rooms"": 1, ""worker_count"": 1, ""mailbox_capacity"": 64,
                ""audience"": """", ""default_template"": ""one"",
                ""rooms"": { ""one"": { ""expected_players"": 2 } },
                ""diagnostics"": {
                    ""enabled"": true, ""directory"": ""C:/tmp/diag"",
                    ""max_files"": 7, ""max_total_mb"": 32,
                    ""max_commands"": 1000, ""max_steps"": 2000, ""log_tail_lines"": 50
                }
            }";

            RoomServerConfig cfg = RoomServerConfig.Parse(json, "<test>");
            Assert.True(cfg.Diagnostics.Enabled);
            Assert.Equal("C:/tmp/diag", cfg.Diagnostics.Directory);
            Assert.Equal(7, cfg.Diagnostics.MaxFiles);
            Assert.Equal(32L * 1024 * 1024, cfg.Diagnostics.MaxTotalBytes);
            Assert.Equal(1000, cfg.Diagnostics.MaxCommands);
            Assert.Equal(2000, cfg.Diagnostics.MaxSteps);
            Assert.Equal(50, cfg.Diagnostics.LogTailLines);
        }

        [Fact]
        public void 终态现场包_含身份记录日志尾与mismatch()
        {
            string dir = Path.Combine(Path.GetTempPath(), "room-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new RoomDiagnosticsOptions(enabled: true, directory: dir,
                    maxFiles: 5, maxTotalMb: 64, maxCommands: 10_000, maxSteps: 10_000, logTailLines: 50);
                var service = new RoomDiagnosticsService(options, "build-test");
                var room = BuildRecordedRoom();

                HostLog.Info("DiagTest", "diag-log-marker");
                service.NoteMismatch(RoomId, 3);
                string path = service.TryWrite(RoomId, room, Config(), gameplayDigestBase: 0,
                    startedAtUtc: new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
                    endReason: "Closed:External");

                Assert.NotNull(path);
                Assert.True(File.Exists(path));
                RoomRunEnvelope envelope = RoomRunCodec.Decode(File.ReadAllBytes(path));
                Assert.Equal("build-test", envelope.BuildHash);
                Assert.Equal(Seed, (long)envelope.Record.Seed);
                Assert.False(envelope.Record.Truncated);
                Assert.Equal(20, envelope.Record.Events.Count(e => e.Kind == RoomRunEventKind.Step));
                Assert.NotNull(envelope.Map);
                Assert.Equal(2, envelope.Config.ExpectedPlayers);
                Assert.Contains("diag-log-marker", envelope.ServerLogTail);
                Assert.NotNull(envelope.ClientMismatch);
                Assert.Equal(3, envelope.ClientMismatch.Frame);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void 保留上限_只留最新文件()
        {
            string dir = Path.Combine(Path.GetTempPath(), "room-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new RoomDiagnosticsOptions(enabled: true, directory: dir,
                    maxFiles: 1, maxTotalMb: 64, maxCommands: 10_000, maxSteps: 10_000, logTailLines: 50);
                var service = new RoomDiagnosticsService(options, "build-test");
                var room = BuildRecordedRoom();

                Assert.NotNull(service.TryWrite(RoomId, room, Config(), 0, DateTime.UtcNow, "Closed:External"));
                Assert.NotNull(service.TryWrite(RoomId, room, Config(), 0, DateTime.UtcNow.AddSeconds(1), "Closed:External"));

                string[] files = Directory.GetFiles(dir, "*.json.gz");
                Assert.Single(files);
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
