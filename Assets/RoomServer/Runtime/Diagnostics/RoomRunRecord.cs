using System;
using System.Collections.Generic;
using LiteSim;

namespace RoomServer.Runtime.Diagnostics
{
    /// <summary>房间命令日志的事件种类（统一有序：命令 → 步进 → 测试规则，重放按同一顺序消费）。</summary>
    public enum RoomRunEventKind
    {
        Command = 0,
        Step = 1,
        TestRules = 2,
    }

    /// <summary>
    /// 一条被录制的 <see cref="RoomCommand"/>。ClientInput 的冗余帧必须深拷贝——
    /// 调用方的 Frames 是复用数组，直接持有会在下一包被覆写。
    /// </summary>
    public readonly struct RoomRunCommand
    {
        public RoomCommandKind Kind { get; }
        public int ConnectionId { get; }
        public int PlayerId { get; }
        public int NewConnectionId { get; }
        public long NowMs { get; }
        public ShutdownReason Reason { get; }
        public int InputFrame { get; }
        public int InputAckSnapshot { get; }
        public int InputViewFrame { get; }
        public int InputCount { get; }
        public SimInputFrame[] InputFrames { get; }

        public RoomRunCommand(RoomCommandKind kind, int connectionId, int playerId, int newConnectionId,
            long nowMs, ShutdownReason reason, int inputFrame, int inputAckSnapshot, int inputViewFrame,
            int inputCount, SimInputFrame[] inputFrames)
        {
            Kind = kind;
            ConnectionId = connectionId;
            PlayerId = playerId;
            NewConnectionId = newConnectionId;
            NowMs = nowMs;
            Reason = reason;
            InputFrame = inputFrame;
            InputAckSnapshot = inputAckSnapshot;
            InputViewFrame = inputViewFrame;
            InputCount = inputCount;
            InputFrames = inputFrames ?? Array.Empty<SimInputFrame>();
        }

        public static RoomRunCommand From(in RoomCommand cmd)
        {
            if (cmd.Kind != RoomCommandKind.ClientInput)
                return new RoomRunCommand(cmd.Kind, cmd.ConnectionId, cmd.PlayerId, cmd.NewConnectionId,
                    cmd.NowMs, cmd.Reason, 0, 0, 0, 0, Array.Empty<SimInputFrame>());

            ClientInputBatch batch = cmd.Input;
            int count = batch.Frames == null
                ? 0
                : Math.Min(Math.Max(batch.Count, 0), batch.Frames.Length);
            SimInputFrame[] frames = count == 0 ? Array.Empty<SimInputFrame>() : new SimInputFrame[count];
            if (count > 0) Array.Copy(batch.Frames, frames, count);

            return new RoomRunCommand(cmd.Kind, cmd.ConnectionId, cmd.PlayerId, cmd.NewConnectionId,
                cmd.NowMs, cmd.Reason, batch.Frame, batch.AckSnapshot, batch.ViewFrame, count, frames);
        }

        public RoomCommand ToCommand()
        {
            switch (Kind)
            {
                case RoomCommandKind.AuthenticatedJoin: return RoomCommand.Join(ConnectionId);
                case RoomCommandKind.ClientInput: return RoomCommand.ClientInput(PlayerId, ToBatch());
                case RoomCommandKind.Disconnect: return RoomCommand.Disconnect(ConnectionId);
                case RoomCommandKind.Rebind: return RoomCommand.Rebind(PlayerId, NewConnectionId);
                case RoomCommandKind.Tick: return RoomCommand.Tick(NowMs);
                case RoomCommandKind.Shutdown: return RoomCommand.Shutdown(Reason);
                case RoomCommandKind.RestoreAck: return RoomCommand.RestoreAck(PlayerId);
                case RoomCommandKind.StartEarly: return RoomCommand.StartEarly(PlayerId);
                default: throw new InvalidOperationException($"未登记的房间命令种类：{Kind}");
            }
        }

        private ClientInputBatch ToBatch() => new ClientInputBatch
        {
            Frame = InputFrame,
            AckSnapshot = InputAckSnapshot,
            ViewFrame = InputViewFrame,
            Count = InputCount,
            Frames = InputFrames,
        };
    }

    /// <summary>一次权威步进后的帧号与全量口径 checksum（定位首分歧帧的裁判）。</summary>
    public readonly struct RoomRunStep
    {
        public int Frame { get; }
        public uint Checksum { get; }

        public RoomRunStep(int frame, uint checksum)
        {
            Frame = frame;
            Checksum = checksum;
        }
    }

    /// <summary>测试规则变更（运行态事实；null 规则用 HasRules=false 表达）。</summary>
    public readonly struct RoomRunTestRules
    {
        public int Frame { get; }
        public bool HasRules { get; }
        public bool NoDeath { get; }
        public bool InfiniteAmmo { get; }
        public float HeadHitLineOverride { get; }
        public float HeadshotRadiusOverride { get; }

        public RoomRunTestRules(int frame, bool hasRules, bool noDeath, bool infiniteAmmo,
            float headHitLineOverride, float headshotRadiusOverride)
        {
            Frame = frame;
            HasRules = hasRules;
            NoDeath = noDeath;
            InfiniteAmmo = infiniteAmmo;
            HeadHitLineOverride = headHitLineOverride;
            HeadshotRadiusOverride = headshotRadiusOverride;
        }

        public static RoomRunTestRules From(int frame, MatchTestRules rules)
        {
            if (rules == null)
                return new RoomRunTestRules(frame, false, false, false, -1f, -1f);
            return new RoomRunTestRules(frame, true, rules.NoDeath, rules.InfiniteAmmo,
                rules.HeadHitLineOverride, rules.HeadshotRadiusOverride);
        }

        public MatchTestRules ToRules()
        {
            if (!HasRules) return null;
            return new MatchTestRules
            {
                NoDeath = NoDeath,
                InfiniteAmmo = InfiniteAmmo,
                HeadHitLineOverride = HeadHitLineOverride,
                HeadshotRadiusOverride = HeadshotRadiusOverride,
            };
        }
    }

    /// <summary>有序事件（Sequence 仅诊断；重放按列表顺序消费）。</summary>
    public readonly struct RoomRunEvent
    {
        public long Sequence { get; }
        public RoomRunEventKind Kind { get; }
        public RoomRunCommand Command { get; }
        public RoomRunStep Step { get; }
        public RoomRunTestRules TestRules { get; }

        private RoomRunEvent(long sequence, RoomRunEventKind kind, RoomRunCommand command,
            RoomRunStep step, RoomRunTestRules testRules)
        {
            Sequence = sequence;
            Kind = kind;
            Command = command;
            Step = step;
            TestRules = testRules;
        }

        public static RoomRunEvent ForCommand(long sequence, in RoomRunCommand command)
            => new RoomRunEvent(sequence, RoomRunEventKind.Command, command, default, default);

        public static RoomRunEvent ForStep(long sequence, in RoomRunStep step)
            => new RoomRunEvent(sequence, RoomRunEventKind.Step, default, step, default);

        public static RoomRunEvent ForTestRules(long sequence, in RoomRunTestRules rules)
            => new RoomRunEvent(sequence, RoomRunEventKind.TestRules, default, default, rules);
    }

    /// <summary>
    /// 一局权威房间的完整重放记录：房间/对局身份 + Seed + 有序命令/步进/规则事件。
    /// 不含构建/表/地图身份（那是宿主信封的职责）；不参与 Sim 判定，只在房间静默后取走。
    ///
    /// 世界存档环（可选）：录制器按帧深拷权威世界（<see cref="SimWorldState.CopyTo"/>，容量有界），
    /// 让重放器在分歧帧能产出 <see cref="SimWorldDiff"/> 字段级报告——否则只有帧号与 checksum。
    /// 分歧帧早于窗口（环已覆盖）时只有帧号，与 SimRunRecord.BeginWithWorlds 同一取舍。
    /// </summary>
    public sealed class RoomRunRecord
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; }
        public string RoomId { get; }
        public string MatchId { get; }
        public int ExpectedPlayers { get; }
        public ulong Seed { get; }
        public bool Truncated { get; }
        public IReadOnlyList<RoomRunEvent> Events { get; }
        public int CommandCount { get; }
        public int StepCount { get; }
        public int TestRuleEventCount { get; }
        public int LastRecordedFrame { get; }

        /// <summary>世界存档环（null = 未启用；封印时录制器所有权移交——封印后不再覆写）。</summary>
        private readonly SimWorldState[] _worldRing;

        /// <summary>环内每槽对应的帧号（-1 = 空）。</summary>
        private readonly int[] _worldRingFrames;

        /// <summary>是否携带世界存档环（false = 重放分歧只有帧号，给不出字段级报告）。</summary>
        public bool HasWorlds => _worldRing != null;

        /// <summary>世界存档环容量（0 = 未启用）。</summary>
        public int WorldRingCapacity => _worldRing?.Length ?? 0;

        /// <summary>世界环（信封层序列化用；零拷贝返回内部对象——调用方不得改写）。</summary>
        public SimWorldState[] WorldRing => _worldRing;

        /// <summary>世界环各槽帧号（同上；与 <see cref="WorldRing"/> 等长成对）。</summary>
        public int[] WorldRingFrames => _worldRingFrames;

        /// <summary>
        /// 取某帧的世界存档（录制点 = RecordStep 时刻的权威世界）。未启用 / 已被环覆盖 → false。
        /// 返回内部对象（零拷贝）——只读。
        /// </summary>
        public bool TryGetWorld(int frame, out SimWorldState world)
        {
            world = null;
            if (_worldRing == null || frame < 0) return false;

            int slot = frame % _worldRing.Length;
            if (_worldRingFrames[slot] != frame) return false;
            world = _worldRing[slot];
            return true;
        }

        public RoomRunRecord(int formatVersion, string roomId, string matchId, int expectedPlayers,
            ulong seed, bool truncated, IReadOnlyList<RoomRunEvent> events,
            SimWorldState[] worldRing = null, int[] worldRingFrames = null)
        {
            if (formatVersion <= 0) throw new ArgumentOutOfRangeException(nameof(formatVersion));
            if (string.IsNullOrEmpty(roomId)) throw new ArgumentException("roomId 必填", nameof(roomId));
            if (string.IsNullOrEmpty(matchId)) throw new ArgumentException("matchId 必填", nameof(matchId));
            if (expectedPlayers <= 0) throw new ArgumentOutOfRangeException(nameof(expectedPlayers));
            if (worldRing != null || worldRingFrames != null)
            {
                if (worldRing == null || worldRingFrames == null || worldRing.Length != worldRingFrames.Length)
                    throw new ArgumentException("世界环数组必须成对出现且等长", nameof(worldRing));
            }

            FormatVersion = formatVersion;
            RoomId = roomId;
            MatchId = matchId;
            ExpectedPlayers = expectedPlayers;
            Seed = seed;
            Truncated = truncated;
            Events = events ?? Array.Empty<RoomRunEvent>();
            _worldRing = worldRing;
            _worldRingFrames = worldRingFrames;

            int commands = 0, steps = 0, testRules = 0, lastFrame = -1;
            for (int i = 0; i < Events.Count; i++)
            {
                RoomRunEvent e = Events[i];
                switch (e.Kind)
                {
                    case RoomRunEventKind.Command: commands++; break;
                    case RoomRunEventKind.Step:
                        steps++;
                        if (e.Step.Frame > lastFrame) lastFrame = e.Step.Frame;
                        break;
                    case RoomRunEventKind.TestRules: testRules++; break;
                }
            }
            CommandCount = commands;
            StepCount = steps;
            TestRuleEventCount = testRules;
            LastRecordedFrame = lastFrame;
        }
    }
}
