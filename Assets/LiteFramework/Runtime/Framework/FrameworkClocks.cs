namespace LiteFramework
{
    /// <summary>
    /// 双轨时钟 + 墙钟的装配入口：时钟之间没有构造依赖，但"三件一起给"是装配契约
    /// （逻辑轨受时停/变速、UI 轨不受；墙钟只做时间戳）——装配段不该逐个 `new` 后再想漏没漏。
    /// </summary>
    public readonly struct FrameworkClocks
    {
        public readonly WorldClock World;

        public readonly UIClock Ui;

        public readonly SystemWallClock Wall;

        private FrameworkClocks(WorldClock world, UIClock ui, SystemWallClock wall)
        {
            World = world;
            Ui = ui;
            Wall = wall;
        }

        public static FrameworkClocks Create() => new FrameworkClocks(new WorldClock(), new UIClock(), new SystemWallClock());
    }

    /// <summary>
    /// 事件总线装配入口：总线需要池（池化事件）与形态位（严格模式只在开发期开）。
    /// 事件域与时钟域分离——时钟模块不产出总线（"事件"是独立的能力域）。
    /// </summary>
    public static class FrameworkEvents
    {
        public static EventBus Create(IReferencePool pool, bool strictMode)
        {
            if (pool == null) throw new System.ArgumentNullException(nameof(pool));
            return new EventBus(pool, strictMode);
        }
    }
}
