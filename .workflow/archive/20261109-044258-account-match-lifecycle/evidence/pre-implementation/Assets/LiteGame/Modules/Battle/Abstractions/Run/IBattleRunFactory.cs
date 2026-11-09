namespace LiteGame
{
    /// <summary>
    /// 对局运行工厂（Battle 域产物）：把账号运行时端口（<see cref="IAccountRuntime"/>）装配成流程可消费的
    /// <see cref="IBattleRun"/>。流程只认端口——不构造 <c>BattleSession</c>/<c>MatchSession</c>。
    /// </summary>
    public interface IBattleRunFactory
    {
        /// <summary>建对局运行端口（失败就地回滚；Account 会话所有权仍归调用方）。</summary>
        IBattleRun Create(IAccountRuntime account);
    }
}
