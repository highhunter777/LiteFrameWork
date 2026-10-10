namespace LiteGame.UI
{
    /// <summary>
    /// 动效默认值单源表：C# 原语默认参（<see cref="UiFx"/>）、绑定索引默认参（<see cref="UIBindIndex"/>）、
    /// 控件调用点（<see cref="UIWidget"/> 按压微动效）、气泡控件（<see cref="UIBubble"/>）与
    /// Lua shim（<see cref="LuaBridge.LuaBehaviourAdapter"/> 内嵌 ui-API 的缺省参）全部引用这里——
    /// 改动效手感只动这一处，杜绝"改 C# 默认漏改 Lua shim"的多副本漂移。
    /// </summary>
    public static class UiFxDefaults
    {
        /// <summary>Pulse 强度：呼吸到原透明度的比例（0.2 = 呼吸到 20%）。</summary>
        public const float PulseStrength = 0.2f;

        /// <summary>Pulse 单程时长（秒；Yoyo 双程即两次呼吸）。</summary>
        public const float PulseDuration = 0.16f;

        /// <summary>Flash 高亮回落时长（秒）。</summary>
        public const float FlashDuration = 0.3f;

        /// <summary>Slide 入场时长（秒）。</summary>
        public const float SlideDuration = 0.25f;

        /// <summary>气泡展示时长（秒；UIBubble 与 Lua showBubble 缺省共用）。</summary>
        public const float BubbleDuration = 1.5f;
    }
}
