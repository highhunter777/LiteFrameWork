using LiteClient;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 发布形态的开发会话（全关空实现）：release 不编译开发静态，端口必须有可注入实现，
    /// 产品代码就能无条件依赖 <see cref="IDevSession"/>（读恒 false、请求恒 false）。
    /// </summary>
    public sealed class NullDevSession : IDevSession
    {
        public static readonly NullDevSession Instance = new NullDevSession();

        private NullDevSession() { }

        public bool Active => false;
        public void SetActive(bool active) { }
        public bool IsDevelopmentBuild => false;
        public bool DirectToTrainingEnabled => false;
        public void ApplyEnterDefaults() { }
        public bool NoDeath => false;
        public bool InfiniteAmmo => false;
        public bool LaserSight => false;
        public bool DamageNumbers => false;
        public bool AutoHeadshot => false;
        public bool TeleportEnabled => false;
        public bool BotFrozen => false;
        public bool BotInfiniteHp => false;
        public int BotCount => 0;
        public float HeadHitLineOverride => -1f;
        public float HeadshotRadiusOverride => -1f;
        public int WeaponDamageOverride => -1;
        public int WeaponRpmOverride => -1;
        public bool DrawHeadshotDebug => false;

        public MatchTestRules MatchRules => null;

        public void AttachMatchRules(MatchTestRules rules) { }

        public void DetachMatchRules(MatchTestRules rules) { }

        public void RequestEnter() { }
        public void RequestExit() { }
        public void RequestTeleport() { }

        public bool TakeEnterRequest() => false;
        public bool TakeExitRequest() => false;
        public bool TakeTeleportRequest() => false;
    }
}
