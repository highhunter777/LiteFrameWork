using LiteFramework;
using LiteGame.Modules.Battle.Shared;

namespace LiteGame.Modules.Battle
{
    /// <summary>
    /// 对局诊断登记端口（M3-6b 3b）：开发装配实现把本局上下文登记到诊断读口（DevHud/GM 页只读），
    /// 离场注销。发布形态无实现（开发诊断读口为 null）→ 产品会话不产生任何诊断面。
    /// </summary>
    public interface IBattleDevDiagnostics
    {
        void Attach(BattleContext context);
        void Detach(BattleContext context);
    }
}
