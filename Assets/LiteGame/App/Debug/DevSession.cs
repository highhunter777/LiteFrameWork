#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 开发会话端口实现（适配器）：转发 <see cref="TestModeRuntime"/> 静态快照。
    /// 产品代码经 <see cref="IDevSession"/> 取用；静态入口只留给既有直读点（allowlist 基线）。
    /// </summary>
    public sealed class DevSession : IDevSession
    {
        public bool Active => TestModeRuntime.Active;
        public bool NoDeath => TestModeRuntime.NoDeath;
        public bool InfiniteAmmo => TestModeRuntime.InfiniteAmmo;
        public bool LaserSight => TestModeRuntime.LaserSight;
        public bool DamageNumbers => TestModeRuntime.DamageNumbers;
        public bool AutoHeadshot => TestModeRuntime.AutoHeadshot;
        public bool TeleportEnabled => TestModeRuntime.TeleportEnabled;
        public bool BotFrozen => TestModeRuntime.BotFrozen;
        public bool BotInfiniteHp => TestModeRuntime.BotInfiniteHp;
        public int BotCount => TestModeRuntime.BotCount;
        public float HeadHitLineOverride => TestModeRuntime.HeadHitLineOverride;
        public float HeadshotRadiusOverride => TestModeRuntime.HeadshotRadiusOverride;
        public int WeaponDamageOverride => TestModeRuntime.WeaponDamageOverride;
        public int WeaponRpmOverride => TestModeRuntime.WeaponRpmOverride;
        public bool DrawHeadshotDebug => TestModeRuntime.DrawHeadshotDebug;

        public void RequestEnter() => TestModeRuntime.RequestEnter();
        public void RequestExit() => TestModeRuntime.RequestExit();
        public void RequestTeleport() => TestModeRuntime.RequestTeleport();

        public bool TakeEnterRequest() => TestModeRuntime.TakeEnterRequest();
        public bool TakeExitRequest() => TestModeRuntime.TakeExitRequest();
        public bool TakeTeleportRequest() => TestModeRuntime.TakeTeleportRequest();
    }
}
#endif
