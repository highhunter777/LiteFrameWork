#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Globalization;
using LiteClient;
using LiteNet;

namespace LiteGame
{
    /// <summary>
    /// 对局诊断读口（联机诊断 HUD 批）：当前对局 <see cref="BattleContext"/> 的 dev-only 单点登记
    /// ＋只读描述单源。**为什么是静态登记口**：对局上下文是流程私有件——不进 DI 容器、非场景组件，
    /// DevHUD 的两个发现面（容器统计 / 场景组件扫描）都够不到它；HUD 与编辑器 GM 页需要同一读口
    /// （同 <see cref="DebugTuner"/> 静态镜像口径）。整件随开发三宏剥离，release 无此面、零残留。
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

        /// <summary>只读快照：相位/网络质量/和解计数（HUD 与 GM 页共用同一读数——两处消费不各拼一套）。</summary>
        public readonly struct Snapshot
        {
            public readonly ClientSessionPhase Phase;
            public readonly NetworkQuality Quality;
            public readonly long ReconcileCount;

            public Snapshot(ClientSessionPhase phase, NetworkQuality quality, long reconcileCount)
            {
                Phase = phase;
                Quality = quality;
                ReconcileCount = reconcileCount;
            }
        }

        /// <summary>从对局上下文取快照。</summary>
        public static Snapshot Take(BattleContext context)
            => new Snapshot(context.SessionPhase, context.NetworkQuality, context.ReconcileCount);

        /// <summary>当前对局的诊断行（无对局 = null——消费方按空态隐藏，不显示陈旧值）。</summary>
        public static string[] DescribeCurrent()
            => Current == null ? null : DescribeLines(Take(Current));

        /// <summary>快照 → 诊断行。RTT/丢包与 <see cref="BattleNetworkHudDriver"/> 同口径：
        /// 丢包千分比 → 一位小数百分比（InvariantCulture）；无样本不显示数值（占位不撒谎）。</summary>
        public static string[] DescribeLines(in Snapshot s)
        {
            string rtt = s.Quality.HasSample
                ? string.Format(CultureInfo.InvariantCulture, "{0}ms", s.Quality.RttMs)
                : "无样本";
            string loss = s.Quality.HasSample
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.0}%", s.Quality.LossPermille / 10f)
                : "—";
            return new[]
            {
                string.Format(CultureInfo.InvariantCulture, "相位: {0}  RTT: {1}  丢包: {2}", s.Phase, rtt, loss),
                string.Format(CultureInfo.InvariantCulture, "和解: {0} 次", s.ReconcileCount),
            };
        }
    }
}
#endif
