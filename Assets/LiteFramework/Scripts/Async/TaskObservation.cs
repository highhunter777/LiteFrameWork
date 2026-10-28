using System;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 根任务观测（《客户端架构重构演进路线图》§4.10：".Forget() 只允许带观测回调"）。
    /// 产品代码用 <see cref="ForgetLogged(UniTask, string)"/> 取代裸 .Forget()：
    /// 异常落 <see cref="LogTag.Task"/> 的 Error（含操作名），不再是静默丢弃。
    /// 纯事件回包等外部边界仍可用带回调的 <c>.Forget(ex =&gt; ...)</c>。
    /// </summary>
    public static class TaskObservation
    {
        /// <summary>启动一个根任务并记录未处理异常（操作名进日志，便于定位）。</summary>
        public static void ForgetLogged(this UniTask task, string operation)
            => task.Forget(ex => LogError(operation, ex));

        /// <summary>有返回值的根任务（返回值丢弃，异常记录同 <see cref="ForgetLogged(UniTask, string)"/>）。</summary>
        public static void ForgetLogged<T>(this UniTask<T> task, string operation)
            => task.Forget(ex => LogError(operation, ex));

        /// <summary>BCL 任务（ContinueWith 等链）的等价观测口。</summary>
        public static void ForgetLogged(this System.Threading.Tasks.Task task, string operation)
            => task.ContinueWith(
                t => LogError(operation, t.Exception?.GetBaseException()),
                System.Threading.CancellationToken.None,
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted
                    | System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,
                System.Threading.Tasks.TaskScheduler.Default);

        private static void LogError(string operation, Exception ex)
            => Log.Error($"[Task] {operation} 未处理异常：{ex?.GetType().Name}: {ex?.Message}", LogTag.Task);
    }
}
