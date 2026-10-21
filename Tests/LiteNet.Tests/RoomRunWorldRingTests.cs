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
    /// T0-2a：房间记录的世界环——录制器按帧深拷权威世界（容量有界），
    /// 信封往返位级一致，重放分歧帧能给出 <see cref="SimWorldDiff"/> 字段级报告。
    /// </summary>
    public sealed class RoomRunWorldRingTests
    {
        private const string RoomId = "WorldRingTest";
        private const string MatchId = "WorldRingTest:match";
        private const long Seed = 20261022L;

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

        private static RoomRunRecorder NewRecorder(int worldRingCapacity)
            => new RoomRunRecorder(RoomId, MatchId, expectedPlayers: 2,
                worldRingCapacity: worldRingCapacity);

        private static RoomRuntime NewRuntime(RoomRunRecorder recorder)
            => new RoomRuntime(Config(), matchId: MatchId, recorder: recorder);

        private static ClientInputBatch Batch(int frame, float moveX)
        {
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame { MoveX = moveX };
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
                room.Execute(RoomCommand.ClientInput(1, Batch(frame, 0.5f)), outputs);
                room.Execute(RoomCommand.Tick(frame * 16L), outputs);
            }
        }

        private static RoomRunRecord RecordMatch(int frames, int worldRingCapacity)
        {
            var recorder = NewRecorder(worldRingCapacity);
            var room = NewRuntime(recorder);
            RunMatch(room, frames);
            return room.TakeRunRecord();
        }

        [Fact]
        public void 世界环_窗口内帧可取回_被覆盖帧报告缺失()
        {
            RoomRunRecord record = RecordMatch(frames: 30, worldRingCapacity: 8);

            Assert.True(record.HasWorlds);
            Assert.Equal(8, record.WorldRingCapacity);
            Assert.Equal(30, record.StepCount);

            // 最近 8 帧在窗口内；更早的帧已被覆盖
            Assert.True(record.TryGetWorld(30, out SimWorldState last));
            Assert.Equal(30, last.Frame);
            Assert.False(record.TryGetWorld(22, out _));
            Assert.False(record.TryGetWorld(0, out _));

            // 取回的世界与该帧录制的 checksum 位级对应（录制点即 RecordStep 时刻的权威世界）
            RoomRunStep step = record.Events
                .Where(e => e.Kind == RoomRunEventKind.Step && e.Step.Frame == 30)
                .Select(e => e.Step).Single();
            Assert.Equal(step.Checksum, SimChecksum.ComputeChecksum(last));
        }

        [Fact]
        public void 世界环_缺省关闭_行为与既有记录一致()
        {
            RoomRunRecord record = RecordMatch(frames: 10, worldRingCapacity: 0);

            Assert.False(record.HasWorlds);
            Assert.Equal(0, record.WorldRingCapacity);
            Assert.False(record.TryGetWorld(10, out _));

            var envelope = RoomRunEnvelope.Create(record, "build", 1u, 0u, 0, "map",
                RoomRunConfigDto.From(Config()), DateTime.UtcNow, DateTime.UtcNow, "test", null, null);
            Assert.Null(RoomRunRecordDto.From(record).WorldRing);
            byte[] bytes = RoomRunCodec.Encode(envelope);
            RoomRunEnvelope decoded = RoomRunCodec.Decode(bytes);
            Assert.False(decoded.Record.ToRecord().HasWorlds);
        }

        [Fact]
        public void 世界环_DTO往返_位级一致_扰动可见()
        {
            RoomRunRecord record = RecordMatch(frames: 20, worldRingCapacity: 4);
            Assert.True(record.TryGetWorld(20, out SimWorldState original));

            // DTO 往返（信封层的序列化形态）必须位级还原——否则字段级 diff 会被序列化噪声淹没
            RoomRunWorldDto dto = RoomRunWorldDto.From(original);
            SimWorldState roundtripped = dto.ToWorld();
            SimWorldDiff.Report clean = SimWorldDiff.Compare(original, roundtripped);
            Assert.True(clean.FullEqual, "DTO 往返必须位级一致：" + clean.ToText());

            // 扰动单个字段 → diff 必须指名该字段（可见性）
            roundtripped.Entities[0].Hp += 1;
            SimWorldDiff.Report dirty = SimWorldDiff.Compare(original, roundtripped);
            Assert.False(dirty.FullEqual);
            Assert.Contains(dirty.Differences, d => d.Path == "Entities[0].Hp");
        }

        [Fact]
        public void 世界环_信封编解码往返_环与帧号保持()
        {
            RoomRunRecord record = RecordMatch(frames: 24, worldRingCapacity: 6);

            var envelope = RoomRunEnvelope.Create(record, "build", 1u, 0u, 0, "map",
                RoomRunConfigDto.From(Config()), DateTime.UtcNow, DateTime.UtcNow, "test", null, null);
            byte[] bytes = RoomRunCodec.Encode(envelope);
            RoomRunEnvelope decoded = RoomRunCodec.Decode(bytes);

            RoomRunRecord decodedRecord = decoded.Record.ToRecord();
            Assert.True(decodedRecord.HasWorlds);
            Assert.Equal(record.WorldRingCapacity, decodedRecord.WorldRingCapacity);

            // 窗口内每帧都可取回，且世界与原记录位级一致
            for (int frame = 19; frame <= 24; frame++)
            {
                Assert.True(record.TryGetWorld(frame, out SimWorldState before), $"frame={frame}");
                Assert.True(decodedRecord.TryGetWorld(frame, out SimWorldState after), $"frame={frame}");
                Assert.True(SimWorldDiff.Compare(before, after).FullEqual, $"frame={frame} 往返不一致");
            }
            Assert.False(decodedRecord.TryGetWorld(18, out _));
        }

        [Fact]
        public void 重放分歧_窗口内帧出字段级报告()
        {
            RoomRunRecord record = RecordMatch(frames: 100, worldRingCapacity: 120);

            // 篡改 42 帧输入 → 重放自该帧分叉；录制世界（原始轨迹）vs 重放世界（篡改轨迹）必有一处字段差异
            var tampered = record.Events.First(e => e.Kind == RoomRunEventKind.Command
                && e.Command.Kind == RoomCommandKind.ClientInput
                && e.Command.PlayerId == 0
                && e.Command.InputFrame == 42);
            tampered.Command.InputFrames[0].MoveX = -1f;

            var result = RoomReplayRunner.Replay(record, Config(), TestMaps.Greybox(),
                CombatValues.Default, WeaponTable.Default);

            Assert.False(result.Matches);
            Assert.Equal(42, result.FirstDivergenceFrame);
            Assert.NotNull(result.DivergenceReport);
            Assert.NotEmpty(result.DivergenceReport.Differences);
            Assert.Contains(result.DivergenceReport.Differences, d => d.Path.StartsWith("Entities[", StringComparison.Ordinal));
            Assert.Contains("字段差异", result.Message);
        }

        [Fact]
        public void 重放分歧_早于环窗口_只有帧号并提示()
        {
            RoomRunRecord record = RecordMatch(frames: 100, worldRingCapacity: 4);

            var tampered = record.Events.First(e => e.Kind == RoomRunEventKind.Command
                && e.Command.Kind == RoomCommandKind.ClientInput
                && e.Command.PlayerId == 0
                && e.Command.InputFrame == 42);
            tampered.Command.InputFrames[0].MoveX = -1f;

            var result = RoomReplayRunner.Replay(record, Config(), TestMaps.Greybox(),
                CombatValues.Default, WeaponTable.Default);

            Assert.False(result.Matches);
            Assert.Equal(42, result.FirstDivergenceFrame);
            Assert.Null(result.DivergenceReport);
            Assert.Contains("世界环窗口", result.Message);
        }
    }
}
