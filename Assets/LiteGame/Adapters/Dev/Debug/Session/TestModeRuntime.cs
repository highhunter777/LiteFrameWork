#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using LiteSim;
using LiteFramework;
using LiteGame;

namespace LiteGame
{
    /// <summary>本地测试模式（Room-Test）的运行时快照与控制桥（开发/编辑器/开发包专用；release 整段剥离）。
    /// **启动配置在场景 DevStartupConfig**（免死/人数/冻结/传送/三个表现开关的进房初值——进房时快照进来）；
    /// **局内可调项在 GM 面板**（直接读写本快照的现场开关）。进入/退出由开发控制台（F3）或 GM 面板发起。
    /// **请求桥（进/退/传送）经 <see cref="RequestEnter"/>/<see cref="RequestExit"/>/<see cref="RequestTeleport"/>
    /// 与 <see cref="TakeEnterRequest"/>/<see cref="TakeExitRequest"/>/<see cref="TakeTeleportRequest"/>
    /// 类型化读写对收口**——消费侧读即清，一次性语义编译期保证（静态标志不再对外裸露）。
    /// 对局行为全部由本快照驱动——本地测试模式恒为本地服形态，两端同进程同源。</summary>
    public sealed class TestModeRuntime : ILocalTestHooks
    {
        /// <summary>进程内唯一实例（开发面单所有者；后续批次可改为 DevSession 构造注入）。</summary>
        public static TestModeRuntime Current { get; } = new TestModeRuntime();

        /// <summary>当前是否处于本地测试模式（进入置位；退出/离场由 GM 面板或对局内退出请求清除）。</summary>
        public bool Active;

        // ---- 配置快照（进房时由 DevStartupConfig 启动配置写入；默认口径见 TestModeOptions.All）----
        public bool NoDeath = true;          // 全房免死（Hp 保底 1、目标不消失）
        public bool InfiniteAmmo;            // 无限子弹（Sim 规则；GM 面板可现场开合）
        public bool LaserSight = true;       // 瞄准激光（运行中可切）
        public bool DamageNumbers = true;    // 伤害数字（运行中可切）
        public bool AutoHeadshot;            // 爆头自动验证（替换输入源自动打爆头；运行中可切）
        public bool TeleportEnabled = true;  // 定点传送（T 键 / GM 面板 → 准心点）
        public bool BotFrozen;               // bot 冻结（权威侧位置每帧回写）
        public bool BotInfiniteHp;           // bot 无限生命（权威侧每帧回满——假人永不死；命中/飘字反馈照常）
        public int BotCount = 1;             // 补位bot 数量（总席位 = 1 + BotCount）

        // ---- 本地测试房宿主钩子（与上面的“开关快照”分离：快照管 UI/配置，钩子管权威侧消费）----
        private bool _localTestActive;
        private bool _localTestBotFrozen;
        private bool _localTestBotInfiniteHp;
        private bool _localTestBotResetRequested;
        private bool _localTestTeleportDispatch;
        private SimVector3 _localTestTeleportTarget;

        /// <summary>本地测试房宿主钩子（Dev 实例；产品只经 IDevSession 端口使用）。</summary>
        public ILocalTestHooks LocalTest => this;

        /// <summary>爆头带试验覆写的**跨局待用值**（&lt; 0 = 不覆写）：GM 面板滑杆/爆头带调带工具在**无活动局**
        /// 时的写点——进房由 ProcedureMatch 种子进 <see cref="MatchTestRules"/>（有活动局时面板直写活动局实例
        /// 并同步本值，供下次进房延续）。不落盘、不进摘要（测试沙箱件）。</summary>
        public float HeadHitLineOverride = -1f;
        public float HeadshotRadiusOverride = -1f;

        /// <summary>武器试验覆写的**跨局待用值**（伤害 ≥ 0 / 射速 &gt; 0 生效）：调参面板在**无活动局**时的写点——
        /// 进房由 ProcedureMatch 种子进 <see cref="MatchTestRules"/>（有活动局时直写活动局实例并同步本值，
        /// 供下次进房延续）。不落盘、不进摘要（测试沙箱件）。</summary>
        public int WeaponDamageOverride = -1;
        public int WeaponRpmOverride = -1;

        /// <summary>
        /// **身位/爆头调试绘制**开关（默认关）：打开后在每个活体身上常驻画**服务端身位圆柱**
        /// （半径/高 = 烘焙判定常量，与命中判定同源）与爆头带段（黄圈=下沿、红圈=上沿）——
        /// 肉眼核对"判定柱在哪、多高算爆头、贴墙距离多远"（绘制件 <see cref="BattleHeadshotDebugDrawer"/>）。
        /// 纯诊断，不改任何判定；面板可现场开关，不必重进本地测试模式。
        /// </summary>
        public bool DrawHeadshotDebug;

        // ---- 请求桥（编辑器面板/热键 → 运行时；一次性语义经类型化读写对编译期强制——根治审计中债）----
        private bool _enterRequested;        // 进入本地测试模式（主菜单消费 → 进 Room-Test）
        private bool _exitRequested;          // 退出本地测试模式（对局内 = 请求离场并关模式；主菜单 = 只关模式）
        private bool _teleportRequested;      //「传送到准心」请求（流程解析目标 → ILocalTestHooks 分发）

        /// <summary>请求进入本地测试模式（写侧：开发控制台 / GM 面板）。</summary>
        public void RequestEnter() => _enterRequested = true;

        /// <summary>请求退出本地测试模式（写侧：GM 面板；对局内=离场并关模式，主菜单=只关模式）。</summary>
        public void RequestExit() => _exitRequested = true;

        /// <summary>请求传送到准心（写侧：T 热键 / GM 面板）。</summary>
        public void RequestTeleport() => _teleportRequested = true;

        /// <summary>消费进入请求（读即清——一次性语义由本方法强制：忘清零/二次消费皆不可能）。</summary>
        public bool TakeEnterRequest() { bool v = _enterRequested; _enterRequested = false; return v; }

        /// <summary>消费退出请求（读即清；主菜单与对局两流程各持一次 Take，流程态互斥 ⇒ 恰一者见 true）。</summary>
        public bool TakeExitRequest() { bool v = _exitRequested; _exitRequested = false; return v; }

        /// <summary>消费传送请求（读即清——消费即移交派发权）。</summary>
        public bool TakeTeleportRequest() { bool v = _teleportRequested; _teleportRequested = false; return v; }

        // 「重置假人」是一次性宿主钩子（ILocalTestHooks.BotResetRequested，面板直写、本地服消费即清）——
        // 宿主不持本类的引用（程序集方向），故不在此设桥。

        /// <summary>默认口径（未挂 DevStartupConfig 时的进房兜底）：数据源是 <see cref="TestModeOptions.All"/>（单一来源）。</summary>
        public void ApplyDefaults()
        {
            TestModeOptions.ApplyDefaults();
            BotCount = 1;                     // 数量项不在开关表（int），随默认口径单独落
        }

        // ILocalTestHooks：宿主钩子与 UI/配置快照分离（同一开关快照经 MatchRulesSeeder 进房时镜像到钩子）。
        bool ILocalTestHooks.Active { get => _localTestActive; set => _localTestActive = value; }
        bool ILocalTestHooks.BotFrozen { get => _localTestBotFrozen; set => _localTestBotFrozen = value; }
        bool ILocalTestHooks.BotInfiniteHp { get => _localTestBotInfiniteHp; set => _localTestBotInfiniteHp = value; }
        bool ILocalTestHooks.BotResetRequested { get => _localTestBotResetRequested; set => _localTestBotResetRequested = value; }
        bool ILocalTestHooks.TeleportDispatch { get => _localTestTeleportDispatch; set => _localTestTeleportDispatch = value; }
        SimVector3 ILocalTestHooks.TeleportTarget { get => _localTestTeleportTarget; set => _localTestTeleportTarget = value; }
    }
}
#endif