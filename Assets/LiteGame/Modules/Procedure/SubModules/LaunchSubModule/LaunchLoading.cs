using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>
    /// 启动走廊装载作用域持有件（《通用加载页设计》§1 时间轴与 §8 批B）：
    /// **开在 Preload**（窗口③ Lua 预载，不定态）、**Main 只报场景进度**（窗口④，
    /// <c>SceneService.Progress</c>——不持所有权）、**收在首屏接线点**（窗口⑥，
    /// <c>ProcedureMain</c> 的 OpenEntryOnce 之后）；中途失败/Error 流程由各Procedure
    /// 显式 <see cref="End"/> 兜底（不留永久阻断面）。
    ///
    /// 静态单场次——进程内只此一条启动走廊（服务生命周期=进程生命周期）。
    /// 驱动面不变：内部持 <see cref="FeedbackService.LoadingScope"/>（Report/End 全经它，
    /// 唯一驱动纪律见《通用加载页设计》§0/§3）。
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
