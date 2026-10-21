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
                File.WriteAllBytes(path, RoomRunCodec.Encode(BuildRepoTableEnvelope(out _)));

                // 不带 --defaults：CLI 走仓库真实表装载 + fixedConfigDigest 复算
                Assert.Equal(0, ReplayTool.Program.Main(new[] { "replay", path, "--json" }));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void CLI_tables指定目录装载_退出码0()
        {
            string path = Path.Combine(Path.GetTempPath(), "replay-cli-" + Guid.NewGuid().ToString("N") + ".json.gz");
            try
            {
                File.WriteAllBytes(path, RoomRunCodec.Encode(BuildRepoTableEnvelope(out _)));

                // --tables 显式指定表目录：部署机/非仓库目录的装载入口（与默认仓库装载同源同摘要）
                Assert.Equal(0, ReplayTool.Program.Main(
                    new[] { "replay", path, "--json", "--tables", FindRepoTablesDir() }));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void CLI_tables目录缺失_退出码4()
        {
            string path = Path.Combine(Path.GetTempPath(), "replay-cli-" + Guid.NewGuid().ToString("N") + ".json.gz");
            try
            {
                File.WriteAllBytes(path, RoomRunCodec.Encode(BuildRepoTableEnvelope(out _)));

                // 目录缺失走 IO 语义（DirectoryNotFound → 退出码 4），不静默回退仓库路径
                Assert.Equal(4, ReplayTool.Program.Main(
                    new[] { "replay", path, "--json", "--tables", Path.Combine(Path.GetTempPath(), "no-such-tables-" + Guid.NewGuid().ToString("N")) }));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>仓库表装载链的信封（与 CLI 默认装载同一份表实例/摘要——退出码判定的前提）。</summary>
        private static RoomRunEnvelope BuildRepoTableEnvelope(out SimMapData map)
        {
            map = TestMaps.Greybox();
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
            return RoomRunEnvelope.Create(record, BuildHash.Value,
                room.FixedConfig.Digest, load.Digest, mapId: 0, mapDigest: SimMapDigest.Compute(map),
                config: RoomRunConfigDto.From(Config(map)),
                startedAtUtc: DateTime.UtcNow, endedAtUtc: DateTime.UtcNow, endReason: "test",
                serverLogTail: "log", clientMismatch: null, map: RoomRunMapDto.From(map));
        }

        /// <summary>仓库表目录定位（与 ServerTableLoader.FindRepoRoot 同款标记，测试侧自持）。</summary>
        private static string FindRepoTablesDir()
        {
            for (var cur = new DirectoryInfo(AppContext.BaseDirectory); cur != null; cur = cur.Parent)
            {
                string tables = System.IO.Path.Combine(cur.FullName,
                    ServerTableLoader.RelativeDir.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (System.IO.Directory.Exists(tables)
                    && File.Exists(System.IO.Path.Combine(cur.FullName, "Tests", "Tests.slnx")))
                    return tables;
            }
            throw new InvalidOperationException("找不到仓库表目录（需含 Assets 与 Tests/Tests.slnx）");
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
