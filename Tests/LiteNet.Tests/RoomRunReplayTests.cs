using System;
using System.Collections.Generic;
using System.Linq;
using LiteSim;
using LiteSim.Tests;
using RoomServer.Diagnostics;
using RoomServer.Runtime;
using RoomServer.Runtime.Diagnostics;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 工具链 v2 第一刀：房间命令日志录制 → 用真实 RoomRuntime 重放 → 逐步 checksum 比对。
    /// 用例锁定“可复现”本身：同记录必同结论、篡改能定位到准确帧、截断与身份不符不得伪装成功。
    /// </summary>
    public sealed class RoomRunReplayTests
    {
        private const string RoomId = "ReplayTest";
        private const string MatchId = "ReplayTest:match";
        private const long Seed = 20261021L;

        private static RoomConfig Config() => new RoomConfig
        {
            Map = TestMaps.Greybox(),
            RoomId = RoomId,
            Seed = Seed,
            ExpectedPlayers = 2,
            MatchTimeLimitMs = 0,
            WaitingTimeoutMs = 0,
            Rules = MatchRules.Default,
        };

        private static RoomRunRecorder NewRecorder(int maxCommands = 100_000, int maxSteps = 100_000)
            => new RoomRunRecorder(RoomId, MatchId, expectedPlayers: 2,
                maxCommands: maxCommands, maxSteps: maxSteps);

        private static RoomRuntime NewRuntime(RoomRunRecorder recorder)
            => new RoomRuntime(Config(), matchId: MatchId, recorder: recorder);

        private static ClientInputBatch Batch(int frame, float moveX)
        {
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame
            {
                MoveX = moveX,
                MoveZ = 0f,
                AimPointX = 0f,
                AimPointY = 0f,
                AimPointZ = 0f,
            };
            return new ClientInputBatch
            {
                Frame = frame,
                AckSnapshot = frame - 1,
                ViewFrame = frame,
                Count = 1,
                Frames = frames,
            };
        }

        private static void RunMatch(RoomRuntime room, int frames)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            room.Execute(RoomCommand.StartEarly(0), outputs);
            for (int frame = 1; frame <= frames; frame++)
            {
                room.Execute(RoomCommand.ClientInput(0, Batch(frame, frame % 2 == 0 ? 1f : 0f)), outputs);
                room.Execute(RoomCommand.ClientInput(1, Batch(frame, 0f)), outputs);
                room.Execute(RoomCommand.Tick(frame * 16L), outputs);
            }
        }

        [Fact]
        public void 录制并重放_逐帧checksum一致()
        {
            var recorder = NewRecorder();
            var room = NewRuntime(recorder);
            RunMatch(room, 120);
            var record = room.TakeRunRecord();

            Assert.NotNull(record);
            Assert.Equal(RoomId, record.RoomId);
            Assert.Equal(MatchId, record.MatchId);
            Assert.Equal((ulong)Seed, record.Seed);
            Assert.Equal(120, record.StepCount);
            Assert.False(record.Truncated);

            var result = RoomReplayRunner.Replay(record, Config(), TestMaps.Greybox(),
                CombatValues.Default, WeaponTable.Default);

            Assert.True(result.Matches, result.Message);
            Assert.Equal(120, result.ReplayedSteps);
            Assert.Equal(-1, result.FirstDivergenceFrame);
        }

        [Fact]
        public void 篡改某帧输入_重放到准确分歧帧()
        {
            var recorder = NewRecorder();
            var room = NewRuntime(recorder);
            RunMatch(room, 100);
            var record = room.TakeRunRecord();

            // 只改记录里的输入数组（checksum 序列保持录制值）→ 重放必须在该帧开始分叉
            var tampered = record.Events.First(e => e.Kind == RoomRunEventKind.Command
                && e.Command.Kind == RoomCommandKind.ClientInput
                && e.Command.PlayerId == 0
                && e.Command.InputFrame == 42);
            tampered.Command.InputFrames[0].MoveX = -1f;

            var result = RoomReplayRunner.Replay(record, Config(), TestMaps.Greybox(),
                CombatValues.Default, WeaponTable.Default);

            Assert.False(result.Matches);
            Assert.Equal(42, result.FirstDivergenceFrame);
            Assert.True(result.DivergenceCommandIndex >= 0);
        }

        [Fact]
        public void 截断记录_重放只覆盖已记录段且显式报告()
        {
            var recorder = NewRecorder(maxCommands: 12, maxSteps: 4);
            var room = NewRuntime(recorder);
            RunMatch(room, 30);
            var record = room.TakeRunRecord();

            Assert.True(record.Truncated);
            Assert.True(record.StepCount <= 4);

            var result = RoomReplayRunner.Replay(record, Config(), TestMaps.Greybox(),
                CombatValues.Default, WeaponTable.Default);

            Assert.True(result.Truncated);
            Assert.Equal(record.StepCount, result.ReplayedSteps);
            Assert.Contains("截断", result.Message);
        }

        [Fact]
        public void 身份不符_重放拒绝而不是跑出假分歧()
        {
            var recorder = NewRecorder();
            var room = NewRuntime(recorder);
            RunMatch(room, 10);
            var record = room.TakeRunRecord();

            var envelope = RoomRunEnvelope.Create(
                record, buildHash: "build-A", fixedConfigDigest: 123u, gameplayDigestBase: 0u,
                mapId: 0, mapDigest: "map-A", config: RoomRunConfigDto.From(Config()),
                startedAtUtc: DateTime.UtcNow, endedAtUtc: DateTime.UtcNow, endReason: "test",
                serverLogTail: "log", clientMismatch: null);

            var verify = RoomRunIdentityVerifier.Verify(envelope, "build-B", 123u, "map-A");
            Assert.False(verify.Ok);
            Assert.Contains("buildHash", verify.Message);
        }

        [Fact]
        public void 测试规则事件_重放同帧生效()
        {
            var recorder = NewRecorder();
            var room = NewRuntime(recorder);
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            room.Execute(RoomCommand.StartEarly(0), outputs);
            for (int frame = 1; frame <= 30; frame++)
            {
                room.Execute(RoomCommand.ClientInput(0, Batch(frame, 1f)), outputs);
                room.Execute(RoomCommand.ClientInput(1, Batch(frame, 0f)), outputs);
                if (frame == 15) room.SetTestRules(new MatchTestRules { NoDeath = true });
                room.Execute(RoomCommand.Tick(frame * 16L), outputs);
            }
            var record = room.TakeRunRecord();

            Assert.Contains(record.Events, e => e.Kind == RoomRunEventKind.TestRules);

            var result = RoomReplayRunner.Replay(record, Config(), TestMaps.Greybox(),
                CombatValues.Default, WeaponTable.Default);

            Assert.True(result.Matches, result.Message);
        }
    }
}
