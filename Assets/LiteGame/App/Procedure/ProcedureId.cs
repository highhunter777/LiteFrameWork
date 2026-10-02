namespace LiteGame
{
    /// <summary>
    /// 流程阶段 id（通用状态机 `StageMachine&lt;ProcedureId, ProcedureArgs&gt;` 的阶段标识）。
    /// Match = 建会话进房，Battle = 建对局上下文并驱动；`Result` 预留（正式结算页），
    /// 暂不加枚举值以免出现空阶段。
    /// </summary>
    public enum ProcedureId
    {
        Launch,
        Patch,
        Preload,
        Main,
        Error,
        Match,
        Battle,
    }
}
