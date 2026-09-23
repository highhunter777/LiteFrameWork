namespace RoomServer.Runtime
{
    /// <summary>
    /// Match 状态机（《商业级通用服务端框架总设计》§9.1）：
    /// <c>Created → WaitingForPlayers → Starting → Running → Finishing → Settling → Closed</c>；
    /// 任意阶段可 → <see cref="MatchPhase.Aborted"/>（不可恢复错误 / 运维终止 / 未开局即放弃）。
    ///
    /// 规则（§9.1）：
    /// - 每次迁移由单一触发验证当前状态；非法迁移**拒绝并不改状态**（调用方输出 Reason=Rejected 事件，不抛）；
    /// - 每次成功迁移记录原因与进入时刻（调用方产出结构化事件：from/to/reason/matchId/frame/nowMs）；
    /// - 超时策略由调用方（RoomRuntime）按 <see cref="EnteredMs"/> 判定，本类只提供时间轴；
    /// - 幂等由调用方保证（重复关闭在终态记事件不重迁移）。
    ///
    /// 与玩法规则的关系：本机只表达"比赛生命周期"，不含比分/胜负（P1 业务阶段）；R1 术语
    /// <c>Finishing</c> 冻结结果摘要、<c>Settling</c> 提交已冻结结果，均不重算任何玩法数值。
    /// </summary>
    public sealed class MatchStateMachine
    {
        /// <summary>当前阶段（初始 Created）。</summary>
        public MatchPhase Phase { get; private set; } = MatchPhase.Created;

        /// <summary>进入当前阶段的时刻（App 注入的单调毫秒；超时判定基准）。</summary>
        public long EnteredMs { get; private set; }

        /// <summary>最近一次成功迁移的原因（诊断/事件）。</summary>
        public string LastReason { get; private set; } = "created";

        /// <summary>终态（Closed/Aborted）——此后不接受任何迁移。</summary>
        public static bool IsTerminal(MatchPhase phase) =>
            phase == MatchPhase.Closed || phase == MatchPhase.Aborted;

        /// <summary>运行态（已开局、未进入收尾）——权威步进与增量广播只在 Running 进行。</summary>
        public static bool IsRunning(MatchPhase phase) => phase == MatchPhase.Running;

        /// <summary>合法迁移表（§9.1 的箭头即真值来源；新增阶段必须先改此表）。</summary>
        public static bool IsAllowed(MatchPhase from, MatchPhase to)
        {
            switch (from)
            {
                case MatchPhase.Created:
                    return to == MatchPhase.WaitingForPlayers || to == MatchPhase.Aborted;
                case MatchPhase.WaitingForPlayers:
                    return to == MatchPhase.Starting || to == MatchPhase.Aborted;
                case MatchPhase.Starting:
                    return to == MatchPhase.Running || to == MatchPhase.Aborted;
                case MatchPhase.Running:
                    return to == MatchPhase.Finishing || to == MatchPhase.Aborted;
                case MatchPhase.Finishing:
                    return to == MatchPhase.Settling || to == MatchPhase.Aborted;
                case MatchPhase.Settling:
                    return to == MatchPhase.Closed || to == MatchPhase.Aborted;
                default:
                    return false;   // Closed/Aborted 为终态
            }
        }

        /// <summary>
        /// 尝试迁移：合法则改状态并记原因/时刻，返回 true；非法（终态/跨越阶段）返回 false 且状态不变。
        /// </summary>
        public bool TryTransition(MatchPhase to, string reason, long nowMs)
        {
            if (!IsAllowed(Phase, to)) return false;
            Phase = to;
            EnteredMs = nowMs;
            LastReason = reason ?? string.Empty;
            return true;
        }
    }
}
