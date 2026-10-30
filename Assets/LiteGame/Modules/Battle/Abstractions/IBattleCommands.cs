namespace LiteGame
{
    /// <summary>战斗命令端口（流程 → 战斗域）：流程只表达"要做什么"，命令类型与派发策略归战斗域。
    /// 这样流程段不再认识 <c>TeleportToAimCommand</c> 之类的战斗内部件。</summary>
    public interface IBattleCommands
    {
        /// <summary>请求把本地角色传送到瞄准点（开发形态；是否生效由实现方与开发会话开关决定）。</summary>
        void RequestTeleportToAim();
    }
}
