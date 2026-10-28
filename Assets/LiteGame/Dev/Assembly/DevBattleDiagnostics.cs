using LiteClient;

namespace LiteGame
{
    /// <summary>对局诊断登记的开发实现：转发静态读口 <see cref="BattleDiagnostics"/>（DevHUD/GM 页只读）。</summary>
    public sealed class DevBattleDiagnostics : IBattleDevDiagnostics
    {
        public void Attach(BattleContext context) => BattleDiagnostics.Attach(context);

        public void Detach(BattleContext context) => BattleDiagnostics.Detach(context);
    }
}
