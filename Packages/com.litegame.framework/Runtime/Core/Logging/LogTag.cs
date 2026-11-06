namespace LiteFramework
{
    /// <summary>
    /// 日志标签词表（**单源**）。
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
        FileSystem,
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
        Task,
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
            LogTag.FileSystem => "FileSystem",
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
            LogTag.Task => "Task",
            LogTag.UI => "UI",
            LogTag.View => "View",
            LogTag.Vfx => "Vfx",
            LogTag.WidgetCheck => "WidgetCheck",
            LogTag.Workbench => "Workbench",
        };
    }
}
