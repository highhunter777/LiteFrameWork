namespace LiteFramework
{
    /// <summary>
    /// 双轨时钟 + 墙钟的装配入口：时钟之间没有构造依赖，但"三件一起给"是装配契约
    /// （逻辑轨受时停/变速、UI 轨不受；墙钟只做时间戳）——装配段不该逐个 `new` 后再想漏没漏。
    /// </summary>
    public readonly struct FrameworkClocks
    {
        public readonly WorldClock World;

        public readonly UIClock UI;

        public readonly IWallClock Wall;

        private FrameworkClocks(WorldClock world, UIClock ui, IWallClock wall)
        {
            World = world;
            UI = ui;
            Wall = wall;
        }

        /// <summary>默认装配（自建系统墙钟；与日志 sink 时间源不是同一实例时优先用带参重载）。</summary>
        public static FrameworkClocks Create() => Create(new SystemWallClock());

        /// <summary>装配并注入墙钟（与 <see cref="FrameworkInfrastructure.WallClock"/> 共实例）。</summary>
        public static FrameworkClocks Create(IWallClock wall)
            => new FrameworkClocks(new WorldClock(), new UIClock(), wall ?? new SystemWallClock());
    }

    /// <summary>事件总线装配入口：形态位（严格模式只在开发期开）；事件域与时钟域分离。</summary>
    public static class FrameworkEvents
    {
        public static EventBus Create(bool strictMode) => new EventBus(strictMode);
    }
}
