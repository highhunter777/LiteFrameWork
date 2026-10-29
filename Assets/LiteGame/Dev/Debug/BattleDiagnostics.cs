#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Text;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteNet;

namespace LiteGame
{
    /// <summary>
    /// 对局诊断读口（联机诊断 HUD 批）：当前对局 <see cref="BattleContext"/> 的 dev-only 单点登记
    /// ＋只读描述单源。**为什么是静态登记口**：对局上下文是流程私有件——不进 DI 容器、非场景组件，
    /// DevHUD 的两个发现面（容器统计 / 场景组件扫描）都够不到它；HUD 与编辑器 GM 页需要同一读口
    /// （同 <see cref="DevStartupConfig"/> 静态镜像口径）。整件随开发三宏剥离，release 无此面、零残留。
    /// 职责边界：只登记、只读、只拼展示文本——相位/网络质量/和解计数的事实全在
    /// <see cref="BattleContext"/>（本件零业务逻辑，投影不写运行态）。
    /// </summary>
    public static class BattleDiagnostics
    {
        /// <summary>当前对局上下文（视图装配点登记、拆视图点注销；无对局 = null）。</summary>
        public static BattleContext Current { get; private set; }

        /// <summary>登记当前对局（对局视图装配时调用——登记后 HUD/GM 页可读相位/网络质量/和解计数）。</summary>
        public static void Attach(BattleContext context) => Current = context;

        /// <summary>注销对局（拆视图时调用；只注销自己登记的实例——防误注销下一局）。</summary>
        public static void Detach(BattleContext context)
        {
            if (ReferenceEquals(Current, context)) Current = null;
        }

        /// <summary>只读快照：相位/网络质量/和解计数 + 对局身份（HUD 与 GM 页共用同一读数——两处消费不各拼一套）。</summary>
        public readonly struct Snapshot
        {
            public readonly ClientSessionPhase Phase;
            public readonly NetworkQuality Quality;
            public readonly long ReconcileCount;
            /// <summary>对局身份（现场包/日志关联用；空 = 无对局）。</summary>
            public readonly string MatchId;
            /// <summary>视图当前渲染帧（日志/现场包的帧轴）。</summary>
            public readonly int Frame;
            /// <summary>本地席位（-1 = 未知）。</summary>
            public readonly int PlayerId;
            /// <summary>本地实体 Id（0 = 尚未对齐）。</summary>
            public readonly long LocalEntityId;
            /// <summary>本局玩法配置摘要（0 = 未校验）。</summary>
            public readonly uint ConfigDigest;
            /// <summary>累计回滚次数（RollbackSim 计数）。</summary>
            public readonly int RollbackCount;
            /// <summary>本帧延后的回滚数（上限约束触发）。</summary>
            public readonly int DeferredRollbacks;
            /// <summary>输入领先帧数（固定常量；输入按 sim.Frame + lead 上行）。</summary>
            public readonly int InputLeadFrames;
            /// <summary>视图帧落后最近快照帧的帧数（-1 = 无快照）。</summary>
            public readonly int SnapshotAgeFrames;
            /// <summary>现场包身份（BuildHash/AppVersion/Platform）。</summary>
            public readonly DiagnosticIdentity Identity;

            public Snapshot(ClientSessionPhase phase, NetworkQuality quality, long reconcileCount)
                : this(phase, quality, reconcileCount, string.Empty, 0, -1, 0, 0u, 0, 0, BattleContext.InputLeadFrames, -1)
            {
            }

            public Snapshot(ClientSessionPhase phase, NetworkQuality quality, long reconcileCount,
                string matchId, int frame, int playerId, long localEntityId, uint configDigest)
                : this(phase, quality, reconcileCount, matchId, frame, playerId, localEntityId, configDigest,
                    0, 0, BattleContext.InputLeadFrames, -1)
            {
            }

            public Snapshot(ClientSessionPhase phase, NetworkQuality quality, long reconcileCount,
                string matchId, int frame, int playerId, long localEntityId, uint configDigest,
                int rollbackCount, int deferredRollbacks, int inputLeadFrames, int snapshotAgeFrames,
                DiagnosticIdentity? identity = null)
            {
                Phase = phase;
                Quality = quality;
                ReconcileCount = reconcileCount;
                MatchId = matchId ?? string.Empty;
                Frame = frame;
                PlayerId = playerId;
                LocalEntityId = localEntityId;
                ConfigDigest = configDigest;
                RollbackCount = rollbackCount;
                DeferredRollbacks = deferredRollbacks;
                InputLeadFrames = inputLeadFrames;
                SnapshotAgeFrames = snapshotAgeFrames;
                Identity = identity ?? DiagnosticIdentity.Unknown;
            }
        }

        /// <summary>从对局上下文取快照（身份面与显示面同一次读）。</summary>
        public static Snapshot Take(BattleContext context)
        {
            int lastSnapshot = context.LastSnapshotFrame;
            int snapshotAge = lastSnapshot >= 0 ? context.ViewRenderFrame - lastSnapshot : -1;
            return new Snapshot(context.SessionPhase, context.NetworkQuality, context.ReconcileCount,
                context.MatchId, context.ViewRenderFrame, context.PlayerId, context.LocalEntityId, context.ConfigDigest,
                context.Sim != null ? context.Sim.RollbackCount : 0,
                context.Sim != null ? context.Sim.DeferredRollbacks : 0,
                BattleContext.InputLeadFrames, snapshotAge, context.DiagnosticsIdentity);
        }

        /// <summary>当前对局的诊断行（无对局 = null——消费方按空态隐藏，不显示陈旧值）。</summary>
        public static string[] DescribeCurrent()
            => Current == null ? null : DescribeLines(Take(Current));

        /// <summary>把当前对局诊断行追加进复用缓冲（无对局 = false、不写；HUD 热路径用——不建数组/不拼中间串）。</summary>
        public static bool AppendCurrent(StringBuilder sb)
        {
            BattleContext current = Current;
            if (current == null) return false;
            AppendLines(Take(current), sb);
            return true;
        }

        /// <summary>HUD 扩展读数：原有两行 + 回滚/输入领先/快照年龄（GM 页/用例仍用两行口径）。</summary>
        public static bool AppendExtendedCurrent(StringBuilder sb)
        {
            BattleContext current = Current;
            if (current == null) return false;
            Snapshot snapshot = Take(current);
            AppendLines(snapshot, sb);
            sb.Append('\n');
            sb.Append("回滚: ").Append(snapshot.RollbackCount)
              .Append("（延后 ").Append(snapshot.DeferredRollbacks).Append("）")
              .Append("  输入领先: ").Append(snapshot.InputLeadFrames).Append(" 帧");
            if (snapshot.SnapshotAgeFrames >= 0)
                sb.Append("  快照年龄: ").Append(snapshot.SnapshotAgeFrames).Append(" 帧");
            return true;
        }

        /// <summary>快照 → 诊断行。RTT/丢包与 <see cref="BattleNetworkHudDriver"/> 同口径：
        /// 丢包千分比 → 一位小数百分比；无样本不显示数值（占位不撒谎）。</summary>
        public static string[] DescribeLines(in Snapshot s)
        {
            var sb = new StringBuilder(96);
            AppendLines(s, sb);
            return sb.ToString().Split('\n');
        }

        private static void AppendLines(in Snapshot s, StringBuilder sb)
        {
            sb.Append("相位: ").Append(PhaseName(s.Phase)).Append("  RTT: ");
            if (s.Quality.HasSample) sb.Append(s.Quality.RttMs).Append("ms");
            else sb.Append("无样本");
            sb.Append("  丢包: ");
            if (s.Quality.HasSample)
            {
                int permille = s.Quality.LossPermille;      // 123‰ = 12.3%（一位小数，整数拆分零分配）
                sb.Append(permille / 10).Append('.').Append(permille % 10).Append('%');
            }
            else sb.Append('—');
            sb.Append('\n');
            sb.Append("和解: ").Append(s.ReconcileCount).Append(" 次");
        }

        private static string PhaseName(ClientSessionPhase phase)
        {
            switch (phase)
            {
                case ClientSessionPhase.Idle: return "Idle";
                case ClientSessionPhase.Connected: return "Connected";
                case ClientSessionPhase.SuspectedLost: return "SuspectedLost";
                case ClientSessionPhase.Reconnecting: return "Reconnecting";
                case ClientSessionPhase.Restoring: return "Restoring";
                case ClientSessionPhase.Failed: return "Failed";
                default: return phase.ToString();
            }
        }
    }
}
#endif
