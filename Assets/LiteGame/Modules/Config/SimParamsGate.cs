namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 判定参数覆写的求值门（纯函数，无静态可变状态）：**只有"本地测试模式 + 本地传输"**才允许把 客户端覆写合并进对局数值。
    /// </summary>
    public static class SimParamsGate
    {
        /// <param name="testModeActive">是否处于本地测试模式（客户端会话端口 <c>IDevSession.Active</c>）。</param>
        /// <param name="localServerTransport">本局是否走进程内本地服分支
        /// （测试房入口 ∧ 开发形态——与 <c>ProductTransportFactory.CreateTransport</c> 的本地分支同一判据）。</param>
        public static bool Evaluate(bool testModeActive, bool localServerTransport)
            => testModeActive && localServerTransport;
    }
}
