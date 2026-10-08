using System;
using System.Collections.Generic;
using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>对局阶段（《商业级通用服务端框架总设计》§9.1 Match 状态机）。</summary>
    public enum MatchPhase
    {
        Created = 0,
        WaitingForPlayers,
        Starting,
        Running,
        Finishing,
        Settling,
        Closed,
        /// <summary>不可恢复错误/运维终止（任意阶段可达）。</summary>
        Aborted,
    }

    /// <summary>房间输出（§8.1 RoomOutput）：Runtime 只产纯数据事件，编码与发送归 App。
    /// 低频事件族（进房/开局/状态迁移/关闭），sealed class 即可；热路径（逐帧快照）不经此——
    /// 快照由 App 层 SnapshotPipeline 直接从 AuthSim 构建（§19 禁第二套快照 DTO 的自然推论）。</summary>
    public abstract class RoomOutput
    {
    }

    /// <summary>纯域信令（非 wire DTO——App 负责映射为 JoinAck/StartGame 等 proto；<see cref="TargetPlayerId"/> = -1 表示全体席位）。</summary>
    public sealed class SignalOutput : RoomOutput
    {
        public readonly int TargetPlayerId;
        public readonly RoomSignal Signal;

        public SignalOutput(int targetPlayerId, RoomSignal signal)
        {
            TargetPlayerId = targetPlayerId;
            Signal = signal;
        }
    }

    /// <summary>进房被拒（满员等；App 记 Ops+拒绝日志——不回发明文，维持现有行为）。</summary>
    public sealed class JoinRejectedOutput : RoomOutput
    {
        public readonly int ConnectionId;
        public readonly string Reason;

        public JoinRejectedOutput(int connectionId, string reason)
        {
            ConnectionId = connectionId;
            Reason = reason;
        }
    }

    /// <summary>强制断开传输连接（席位失效/非法输入等；App 映射为 transport.Disconnect）。</summary>
    public sealed class CloseConnectionOutput : RoomOutput
    {
        public readonly int ConnectionId;
        public readonly string Reason;

        public CloseConnectionOutput(int connectionId, string reason)
        {
            ConnectionId = connectionId;
            Reason = reason;
        }
    }

    /// <summary>
    /// 状态迁移事件（§9.1：每次迁移记录 from/to/reason/matchId/frame/nowMs；非法迁移以
    /// Reason=Rejected 输出而不抛——单条坏命令不能炸掉权威循环）。
    /// </summary>
    public sealed class MatchStateChangedOutput : RoomOutput
    {
        public readonly MatchPhase From;
        public readonly MatchPhase To;
        public readonly string Reason;
        public readonly string MatchId;
        public readonly int Frame;
        public readonly long NowMs;

        public MatchStateChangedOutput(MatchPhase from, MatchPhase to, string reason, string matchId, int frame, long nowMs)
        {
            From = from;
            To = to;
            Reason = reason;
            MatchId = matchId;
            Frame = frame;
            NowMs = nowMs;
        }
    }

    /// <summary>
    /// 结算就绪（R1 最小口径：Finishing 冻结的 <see cref="MatchResultSummary"/>——完整结算信封、
    /// Outbox 与重试归 R3，本输出只交付"已冻结"的事实摘要）。
    /// </summary>
    public sealed class SettlementReadyOutput : RoomOutput
    {
        public readonly MatchResultSummary Summary;

        public SettlementReadyOutput(MatchResultSummary summary) => Summary = summary;
    }

    /// <summary>对局结果摘要（Finishing 时冻结；§9.1"Settling 只提交已冻结结果，不重算"）。</summary>
    public sealed class MatchResultSummary
    {
        public readonly string MatchId;
        public readonly long Seed;
        public readonly int FinalFrame;
        public readonly ShutdownReason EndReason;
        private readonly int[] _seatPlayerIds;
        private readonly PlayerMatchResult[] _players;
        public int[] SeatPlayerIds => (int[])_seatPlayerIds.Clone();
        public PlayerMatchResult[] Players => (PlayerMatchResult[])_players.Clone();
        public readonly long WinnerEntityId;
        public readonly MatchEndReason GameplayEndReason;

        public MatchResultSummary(string matchId, long seed, int finalFrame, ShutdownReason endReason, int[] seatPlayerIds,
            long winnerEntityId = 0L, MatchEndReason gameplayEndReason = MatchEndReason.None,
            PlayerMatchResult[] players = null)
        {
            MatchId = matchId;
            Seed = seed;
            FinalFrame = finalFrame;
            EndReason = endReason;
            _seatPlayerIds = seatPlayerIds == null ? Array.Empty<int>() : (int[])seatPlayerIds.Clone();
            _players = players == null ? Array.Empty<PlayerMatchResult>() : (PlayerMatchResult[])players.Clone();
            WinnerEntityId = winnerEntityId;
            GameplayEndReason = gameplayEndReason;
        }
    }

    /// <summary>结算冻结的单玩家数据；只含值类型，输入/输出数组均与 Sim 分离。</summary>
    public readonly struct PlayerMatchResult
    {
        public int PlayerId { get; }
        public long EntityId { get; }
        public int Kills { get; }
        public int Deaths { get; }

        public PlayerMatchResult(int playerId, long entityId, int kills, int deaths)
        {
            PlayerId = playerId;
            EntityId = entityId;
            Kills = kills;
            Deaths = deaths;
        }
    }

    /// <summary>域信令基类（App → proto 映射的锚点；业务阶段新增信令在此扩展，不动 Runtime 输出骨架）。</summary>
    public abstract class RoomSignal
    {
    }

    /// <summary>席位就位（App：JoinAck——Members 为当前全员快照，playerId 升序）。</summary>
    public sealed class PlayerAdmitted : RoomSignal
    {
        public readonly int PlayerId;
        public readonly int[] Members;

        public PlayerAdmitted(int playerId, int[] members)
        {
            PlayerId = playerId;
            Members = members;
        }
    }

    /// <summary>对局开始（App：StartGame 广播——Seed/Frame/ConfigHash 与协议字段一一对应）。</summary>
    public sealed class MatchStarted : RoomSignal
    {
        public readonly long Seed;
        public readonly int Frame;
        public readonly uint ConfigHash;
        public readonly MatchStateData MatchState;
        public readonly string MatchId;

        public MatchStarted(long seed, int frame, uint configHash, MatchStateData matchState = default, string matchId = null)
        {
            Seed = seed;
            Frame = frame;
            ConfigHash = configHash;
            MatchState = matchState;
            MatchId = matchId;
        }
    }

    /// <summary>席位恢复完成（§9.3 步骤 6：Restoring → Active——App 据此计 Ops，增量广播自此恢复）。</summary>
    public sealed class SeatRestored : RoomSignal
    {
        public readonly int PlayerId;

        public SeatRestored(int playerId) => PlayerId = playerId;
    }
}
