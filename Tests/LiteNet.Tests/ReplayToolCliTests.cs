using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteNet;
using LiteSim;
using LiteSim.Tests;
using ReplayTool;
using RoomServer;
using RoomServer.Diagnostics;
using RoomServer.Runtime;
using RoomServer.Runtime.Diagnostics;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>CLI 端到端：真实记录文件 → 重放退出码 0；篡改 → 2；身份不符 → 3。</summary>
    public sealed class ReplayToolCliTests
    {
        private const string RoomId = "CliTest";
        private const string MatchId = "CliTest:match";
        private const long Seed = 20261021L;

        private static RoomConfig Config(SimMapData map) => new RoomConfig
        {
            Map = map,
            RoomId = RoomId,
            Seed = Seed,
            ExpectedPlayers = 2,
            Rules = MatchRules.Default,
        };

        private static RoomRunEnvelope BuildEnvelope(out SimMapData map)
        {
            map = TestMaps.Greybox();
            var recorder = new RoomRunRecorder(RoomId, MatchId, expectedPlayers: 2,
                maxCommands: 10_000, maxSteps: 10_000);
            var room = new RoomRuntime(Config(map), matchId: MatchId, recorder: recorder);
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            room.Execute(RoomCommand.StartEarly(0), outputs);
            for (int frame = 1; frame <= 60; frame++)
            {
                var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
                frames[0] = new SimInputFrame { MoveX = frame % 2 == 0 ? 1f : 0f };
                room.Execute(RoomCommand.ClientInput(0, new ClientInputBatch
                {
                    Frame = frame, AckSnapshot = frame - 1, ViewFrame = frame, Count = 1, Frames = frames,
                }), outputs);
                room.Execute(RoomCommand.Tick(frame * 16L), outputs);
            }
            RoomRunRecord record = room.TakeRunRecord();
            return RoomRunEnvelope.Create(record, BuildHash.Value, room.FixedConfig.Digest,
                gameplayDigestBase: 0, mapId: 0, mapDigest: SimMapDigest.Compute(map),
                config: RoomRunConfigDto.From(Config(map)),
                startedAtUtc: DateTime.UtcNow, endedAtUtc: DateTime.UtcNow, endReason: "test",
                serverLogTail: "log", clientMismatch: null, map: RoomRunMapDto.From(map));
        }

        private static int RunCli(string path) => ReplayTool.Program.Main(new[] { "replay", path, "--json", "--defaults" });

        [Fact]
        public void CLI_完整记录_退出码0()
        {
            string path = Path.Combine(Path.GetTempPath(), "replay-cli-" + Guid.NewGuid().ToString("N") + ".json.gz");
            try
            {
                RoomRunEnvelope envelope = BuildEnvelope(out _);
                File.WriteAllBytes(path, RoomRunCodec.Encode(envelope));
                Assert.Equal(0, RunCli(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void CLI_篡改输入_退出码2()
        {
            string path = Path.Combine(Path.GetTempPath(), "replay-cli-" + Guid.NewGuid().ToString("N") + ".json.gz");
            try
            {
                RoomRunEnvelope envelope = BuildEnvelope(out _);
                var input = envelope.Record.Events.First(e =>
                    e.Kind == RoomRunEventKind.Command && e.Command.Kind == RoomCommandKind.ClientInput
                    && e.Command.PlayerId == 0 && e.Command.InputFrame == 42);
                input.Command.InputFrames[0].MoveX = -1f;
                File.WriteAllBytes(path, RoomRunCodec.Encode(envelope));
                Assert.Equal(2, RunCli(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void CLI_仓库表记录_默认装载_退出码0()
        {
            string path = Path.Combine(Path.GetTempPath(), "replay-cli-" + Guid.NewGuid().ToString("N") + ".json.gz");
            try
            {
                SimMapData map = TestMaps.Greybox();
                ServerTableLoad load = ServerTableLoader.LoadFromRepo();
                var recorder = new RoomRunRecorder(RoomId, MatchId, expectedPlayers: 2,
                    maxCommands: 10_000, maxSteps: 10_000);
                var room = new RoomRuntime(Config(map), load.Combat.ToValues(), load.Weapons, MatchId,
                    testRules: null, gameplayDigestBase: load.Digest, recorder: recorder);
                var outputs = new List<RoomOutput>();
                room.Execute(RoomCommand.Join(1), outputs);
                room.Execute(RoomCommand.Join(2), outputs);
                room.Execute(RoomCommand.StartEarly(0), outputs);
                for (int frame = 1; frame <= 60; frame++)
                {
                    var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
                    frames[0] = new SimInputFrame { MoveX = frame % 2 == 0 ? 1f : 0f };
                    room.Execute(RoomCommand.ClientInput(0, new ClientInputBatch
                    {
                        Frame = frame, AckSnapshot = frame - 1, ViewFrame = frame, Count = 1, Frames = frames,
                    }), outputs);
                    room.Execute(RoomCommand.Tick(frame * 16L), outputs);
                }

                RoomRunRecord record = room.TakeRunRecord();
                RoomRunEnvelope envelope = RoomRunEnvelope.Create(record, BuildHash.Value,
                    room.FixedConfig.Digest, load.Digest, mapId: 0, mapDigest: SimMapDigest.Compute(map),
                    config: RoomRunConfigDto.From(Config(map)),
                    startedAtUtc: DateTime.UtcNow, endedAtUtc: DateTime.UtcNow, endReason: "test",
                    serverLogTail: "log", clientMismatch: null, map: RoomRunMapDto.From(map));
                File.WriteAllBytes(path, RoomRunCodec.Encode(envelope));

                // 不带 --defaults：CLI 走仓库真实表装载 + fixedConfigDigest 复算
                Assert.Equal(0, ReplayTool.Program.Main(new[] { "replay", path, "--json" }));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void CLI_构建身份不符_退出码3()
        {
            string path = Path.Combine(Path.GetTempPath(), "replay-cli-" + Guid.NewGuid().ToString("N") + ".json.gz");
            try
            {
                RoomRunEnvelope envelope = BuildEnvelope(out _);
                envelope.BuildHash = "not-the-current-build";
                File.WriteAllBytes(path, RoomRunCodec.Encode(envelope));
                Assert.Equal(3, RunCli(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
