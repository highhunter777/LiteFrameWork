using System;

namespace LiteFramework
{
    /// <summary>
    /// 调用即防护（引擎无关，M3 §2.6 前置件，原列 2.7 提前随事件桥交付）：包住"外部回调"——
    /// 单个回调抛异常不得中断派发链（设计方案 §4.3 事件桥；xLua 侧薄封装见 LiteGame.EventBridge）。
    /// 异常经 Log.Error 落地（C# 日志可见，验收线 6），调用方存活。
    /// 仅用于低频入口；每帧热路径不走 try/catch（§4.3 热路径纪律）。
    /// </summary>
    public static class SafeCall
    {
        /// <summary>无返回值调用：抛异常被隔离（记录后吞掉），派发方存活。</summary>
        public static void Invoke(Action call, string where)
        {
            try { call(); }
            catch (Exception ex)
            {
                Log.Error($"[SafeCall:{where}] {ex.GetType().Name}:{ex.Message}", "SafeCall");
            }
        }

        /// <summary>
        /// 成败可判的调用（无返回值）：异常同样被隔离并记录，但调用方拿得到"这一步失败了"——
        /// 用于**必须能回滚**的编排步骤（如 UI 首次打开 OnInit/OnShow 失败要清理半成品，§4.1），
        /// 避免用返回值伪造成功（"Active + 空逻辑"伪装）。
        /// </summary>
        public static bool TryInvoke(Action call, string where)
        {
            try { call(); return true; }
            catch (Exception ex)
            {
                Log.Error($"[SafeCall:{where}] {ex.GetType().Name}:{ex.Message}", "SafeCall");
                return false;
            }
        }

        /// <summary>带返回值调用：抛异常被隔离并返回 fallback（调用方拿到确定值，不走半执行态）。</summary>
        public static T Invoke<T>(Func<T> call, string where, T fallback = default)
        {
            try { return call(); }
            catch (Exception ex)
            {
                Log.Error($"[SafeCall:{where}] {ex.GetType().Name}:{ex.Message}", "SafeCall");
                return fallback;
            }
        }
    }
}
