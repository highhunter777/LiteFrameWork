namespace LiteGame
{
    /// <summary>
    /// 流程阶段 id（通用状态机 `StageMachine&lt;ProcedureId, ProcedureArgs&gt;` 的阶段标识，2026-09-17 A 路线）。
    /// 取代旧 `ChangeState&lt;ProcedureXxx&gt;()` 的"类型即标识"。
    /// C1-⑩ 增 `Patch`（§7.1 标准流程：Launch → Bootstrap → Patch → Preload——启动内容事务恢复 + 资源包初始化）。
    /// **预留**（《UI扩展能力设计》§9.1，M10 四批 / M11 落地，本轮不加枚举值以免出现空阶段）：
    /// `Match`（调 ConnectAsync）/ `Battle`（OnEnter 建 BattleContext）/ `Result`（销毁 BattleContext）。
    /// </summary>
    public enum ProcedureId
    {
        Launch,
        Patch,
        Preload,
        Main,
        Error,
    }
}
