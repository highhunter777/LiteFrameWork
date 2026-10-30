#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using LiteSim;
using LiteFramework;
using LiteGame;

namespace LiteGame
{
    /// <summary>
    /// 开发会话端口实现（适配器）：转发 <see cref="TestModeRuntime"/> 静态快照；
    /// 本局规则是**实例**状态（对局会话绑定/解绑），不再有 Sim 侧静态"当前局"。
    /// 产品代码经 <see cref="IDevSession"/> 取用；静态入口只留给既有直读点（allowlist 基线）。
    /// </summary>
    public sealed class DevSession : IDevSession
    {
        private MatchTestRules _matchRules;
        private readonly TestModeRuntime _runtime = TestModeRuntime.Current;

        public bool Active => _runtime.Active;
        public void SetActive(bool active) => _runtime.Active = active;
        public bool IsDevelopmentBuild => true;
        public bool DirectToTrainingEnabled => DevStartupConfig.DirectToTrainingEnabled;
        public void ApplyEnterDefaults() => DevStartupConfig.ApplySnapshotOrDefaults();
        public bool NoDeath => _runtime.NoDeath;
        public bool InfiniteAmmo => _runtime.InfiniteAmmo;
        public bool LaserSight => _runtime.LaserSight;
        public bool DamageNumbers => _runtime.DamageNumbers;
        public bool AutoHeadshot => _runtime.AutoHeadshot;
        public bool TeleportEnabled => _runtime.TeleportEnabled;
        public bool BotFrozen => _runtime.BotFrozen;
        public bool BotInfiniteHp => _runtime.BotInfiniteHp;
        public int BotCount => _runtime.BotCount;
        public float HeadHitLineOverride => _runtime.HeadHitLineOverride;
        public float HeadshotRadiusOverride => _runtime.HeadshotRadiusOverride;
        public int WeaponDamageOverride => _runtime.WeaponDamageOverride;
        public int WeaponRpmOverride => _runtime.WeaponRpmOverride;
        public bool DrawHeadshotDebug => _runtime.DrawHeadshotDebug;

        /// <summary>当前对局规则持有件（对局会话绑定；null = 无对局）。</summary>
        public MatchTestRules MatchRules => _matchRules;

        /// <summary>本地测试房宿主钩子（Dev 实例；产品只经 IDevSession 端口使用）。</summary>
        public ILocalTestHooks LocalTest => TestModeRuntime.Current.LocalTest;

        public void AttachMatchRules(MatchTestRules rules) => _matchRules = rules;

        public void DetachMatchRules(MatchTestRules rules)
        {
            if (ReferenceEquals(_matchRules, rules)) _matchRules = null;
        }

        public void RequestEnter() => _runtime.RequestEnter();
        public void RequestExit() => _runtime.RequestExit();
        public void RequestTeleport() => _runtime.RequestTeleport();

        public bool TakeEnterRequest() => _runtime.TakeEnterRequest();
        public bool TakeExitRequest() => _runtime.TakeExitRequest();
        public bool TakeTeleportRequest() => _runtime.TakeTeleportRequest();
    }
}
#endif
