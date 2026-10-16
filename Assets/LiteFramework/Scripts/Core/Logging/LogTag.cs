namespace LiteFramework
{
    /// <summary>
    /// 日志标签词表（**单源**）。tag 是日志系统的全局分类：稳定的模块/域短名——
    /// 动态信息（实例名/路径）进 message 不进 tag；Recent 环缓冲容量固定，
    /// tag 集合必须小而稳定。
    ///
    /// 新增 tag = 在此加一行枚举项（显式一次评审）；门面只收 <see cref="LogTag"/>，
    /// 未登记值在语言层面无入口（编译期封闭）。新增后须在 <see cref="Log.TagName"/>
    /// 补显示名——穷尽守卫测试会拦住漏项。
    /// </summary>
    public enum LogTag
    {
        None = 0,       // 缺省值：无标签（显示面不出 [tag] 前缀）
        Anim,
        Audio,
        Battle,
        Bootstrap,
        Camera,
        Command,
        Config,
        Content,
        DevReload,
        Entity,
        Event,
        FileSys,
        Fsm,
        GameEntry,
        LText,
        Lua,
        LuaRefill,
        MainThreadDispatcher,
        Meta,
        Patch,
        Procedure,
        SafeCall,
        Scene,
        Scheduler,
        Setting,
        Smoke,
        SubscriptionBag,
        UI,
        View,
        Vfx,
        WidgetCheck,
        Workbench,
    }

    public static partial class Log
    {
        /// <summary>
        /// 标签显示名单源：枚举名即显示名；<see cref="LogTag.None"/> → 空串（无前缀）。
        /// 无 default 臂——漏臂即编译告警（CS8509）且穷尽守卫测试报红。
        /// </summary>
        public static string TagName(LogTag tag) => tag switch
        {
            LogTag.None => "",
            LogTag.Anim => "Anim",
            LogTag.Audio => "Audio",
            LogTag.Battle => "Battle",
            LogTag.Bootstrap => "Bootstrap",
            LogTag.Camera => "Camera",
            LogTag.Command => "Command",
            LogTag.Config => "Config",
            LogTag.Content => "Content",
            LogTag.DevReload => "DevReload",
            LogTag.Entity => "Entity",
            LogTag.Event => "Event",
            LogTag.FileSys => "FileSys",
            LogTag.Fsm => "Fsm",
            LogTag.GameEntry => "GameEntry",
            LogTag.LText => "LText",
            LogTag.Lua => "Lua",
            LogTag.LuaRefill => "LuaRefill",
            LogTag.MainThreadDispatcher => "MainThreadDispatcher",
            LogTag.Meta => "Meta",
            LogTag.Patch => "Patch",
            LogTag.Procedure => "Procedure",
            LogTag.SafeCall => "SafeCall",
            LogTag.Scene => "Scene",
            LogTag.Scheduler => "Scheduler",
            LogTag.Setting => "Setting",
            LogTag.Smoke => "Smoke",
            LogTag.SubscriptionBag => "SubscriptionBag",
            LogTag.UI => "UI",
            LogTag.View => "View",
            LogTag.Vfx => "Vfx",
            LogTag.WidgetCheck => "WidgetCheck",
            LogTag.Workbench => "Workbench",
        };
    }
}
