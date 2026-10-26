namespace LiteFramework
{
    /// <summary>
    /// 恢复能力端口：能"弹出恢复栈顶并回到该状态"的机体。
    ///
    /// **为什么单独成端口**：表驱动阶段（<see cref="TableStage{TId,TReq}"/>）到点恢复时
    /// 不应按具体类 `is` 探测——那会让"新增一台抢占机"静默漏掉恢复（历史实伤：
    /// 层级抢占机有 `TryResume` 却不被识别）。机体实现本端口即可被阶段实现发现，
    /// 与 <see cref="IStageHost{TId,TReq}"/> 的窄接口同一思路：能力声明在接口，调用方只认接口。
    /// </summary>
    public interface IResumeHost
    {
        /// <summary>恢复栈是否非空（诊断/守卫用）。</summary>
        bool HasResumePending { get; }

        /// <summary>弹栈顶并回到该状态（绕过准入）；栈空/已在目标 → false（无害，不抛）。</summary>
        bool TryResume();
    }
}
