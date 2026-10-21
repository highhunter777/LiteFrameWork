using System;
using System.IO;
using System.Linq;
using LiteSim;
using RoomServer.Diagnostics;
using RoomServer.Runtime;
using RoomServer.Runtime.Diagnostics;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 工具链 v2 第一刀：记录信封的版本化编解码与原子存储。
    /// 锁的是“落盘后还能读回来、字段不丢、保留上限生效、不留下半截 tmp”。
    /// </summary>
    public sealed class RoomRunCodecStoreTests
    {
        private static RoomRunRecord BuildRecord()
        {
            var recorder = new RoomRunRecorder("room", "room:match", expectedPlayers: 2,
                maxCommands: 1_000, maxSteps: 1_000);
            recorder.RecordSeed(12345UL);

            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame { MoveX = 0.5f, AimPointY = 1f };

            recorder.RecordCommand(RoomCommand.Join(1));
            recorder.RecordCommand(RoomCommand.ClientInput(0, new ClientInputBatch
            {
                Frame = 1,
                AckSnapshot = 0,
                ViewFrame = 1,
                Count = 1,
                Frames = frames,
            }));
            recorder.RecordCommand(RoomCommand.Tick(16));
            recorder.RecordStep(1, 0xDEADBEEFu);
            recorder.RecordTestRules(1, new MatchTestRules { NoDeath = true, HeadHitLineOverride = 1.5f });
            return recorder.Seal();
        }

        private static RoomRunEnvelope EnvelopeFor(RoomRunRecord record, int second = 0)
        {
            var config = new RoomConfig
            {
                RoomId = "room",
                ExpectedPlayers = 2,
                Seed = 12345L,
                MapId = 0,
                Rules = MatchRules.Default,
            };
            return RoomRunEnvelope.Create(
                record, buildHash: "build-A", fixedConfigDigest: 42u, gameplayDigestBase: 0u,
                mapId: 0, mapDigest: "map-A", config: RoomRunConfigDto.From(config),
                startedAtUtc: new DateTime(2026, 10, 21, 0, 0, second, DateTimeKind.Utc),
                endedAtUtc: new DateTime(2026, 10, 21, 0, 1, second, DateTimeKind.Utc),
                endReason: "test", serverLogTail: "server-log", clientMismatch: null);
        }

        [Fact]
        public void 编解码往返_命令输入checksum与规则事件保真()
        {
            var record = BuildRecord();
            byte[] bytes = RoomRunCodec.Encode(EnvelopeFor(record));
            var decoded = RoomRunCodec.Decode(bytes);

            Assert.Equal("build-A", decoded.BuildHash);
            Assert.Equal(42u, decoded.FixedConfigDigest);
            Assert.Equal("map-A", decoded.MapDigest);
            Assert.Equal(record.Seed, decoded.Record.Seed);
            Assert.Equal(record.Events.Count, decoded.Record.Events.Count);

            var inputEvent = decoded.Record.Events.First(e =>
                e.Kind == RoomRunEventKind.Command && e.Command.Kind == RoomCommandKind.ClientInput);
            Assert.Equal(1, inputEvent.Command.InputFrame);
            Assert.Equal(0.5f, inputEvent.Command.InputFrames[0].MoveX);

            var stepEvent = decoded.Record.Events.First(e => e.Kind == RoomRunEventKind.Step);
            Assert.Equal(1, stepEvent.Step.Frame);
            Assert.Equal(0xDEADBEEFu, stepEvent.Step.Checksum);

            var rulesEvent = decoded.Record.Events.First(e => e.Kind == RoomRunEventKind.TestRules);
            Assert.True(rulesEvent.TestRules.NoDeath);
            Assert.Equal(1.5f, rulesEvent.TestRules.HeadHitLineOverride);
        }

        [Fact]
        public void 存储_原子写与保留上限()
        {
            string dir = Path.Combine(Path.GetTempPath(), "litesim-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new RoomRunStore(dir, maxFiles: 2, maxTotalBytes: 10_000_000);
                for (int i = 0; i < 4; i++)
                {
                    var record = BuildRecord();
                    store.Save(EnvelopeFor(record, second: i));
                }

                var files = store.List();
                Assert.Equal(2, files.Count);
                Assert.All(files, f => Assert.True(File.Exists(f)));
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
