using LiteFramework.UI;
namespace LiteGame
{
    /// <summary>
    /// 流程阶段 id（通用状态机 `StageMachine&lt）。
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
