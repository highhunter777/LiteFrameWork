using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>
    /// 启动走廊装载作用域持有件（《通用加载页设计》§1 时间轴与 §8 批B）： **开在 Preload**（窗口③ Lua 预载，不定态）、**Main 只报场景进度**（窗口④。）。
    /// </summary>
    internal static class LaunchLoading
    {
        private static FeedbackService.LoadingScope? _scope;

        /// <summary>启动走廊在场（阻断中）。</summary>
        public static bool Active => _scope != null;

        public static void Begin(FeedbackService feedback, string message)
        {
            End();                                              // 幂等：漏收的旧场次先兜底
            if (feedback != null) _scope = feedback.BeginLoading(message);
        }

        public static void Report(string message, float? progress01)
            => _scope?.Report(message, progress01);

        public static void End()
        {
            _scope?.Dispose();
            _scope = null;
        }
    }
}
