using LiteFramework;
using LiteGame.Modules.Battle;
using LiteGame.Modules.Battle.Shared;

namespace LiteGame
{
    /// <summary>对局诊断登记的开发实现：转发静态读口 <see cref="BattleDiagnostics"/>（DevHud/GM 页只读）。</summary>
    public sealed class DevBattleDiagnostics : IBattleDevDiagnostics
    {
        public void Attach(BattleContext context) => BattleDiagnostics.Attach(context);

        public void Detach(BattleContext context) => BattleDiagnostics.Detach(context);
    }
}
