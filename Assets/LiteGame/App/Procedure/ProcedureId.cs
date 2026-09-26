namespace LiteGame
{
    /// <summary>
    /// 流程阶段 id（通用状态机 `StageMachine&lt;ProcedureId, ProcedureArgs&gt;` 的阶段标识，2026-09-17 A 路线）。
    /// C1-⑩ 增 `Patch`（§7.1 标准流程：Launch → Patch → Preload）。
    /// C2 批① 增 `Match`/`Battle`（原《UI扩展能力设计》§9.1 预留位兑现）：Match = 建会话进房，
    /// Battle = 建对局上下文并驱动；`Result` 归 G3（正式结算页），本轮不加枚举值以免出现空阶段。
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
