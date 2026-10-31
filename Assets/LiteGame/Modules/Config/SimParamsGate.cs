namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 判定参数覆写的求值门（纯函数，无静态可变状态）：**只有"本地测试模式 + 本地传输"**才允许把
    /// 客户端覆写合并进对局数值；联机 / 云端 sandbox 一律 false——两端判定几何必须同源，否则权威与预测
    /// 必然分叉（覆写只服务进程内本地服的调参试验）。
    ///
    /// **求值时机**：每会话（每一局）求值一次——进房单点在创建传输前调用
    /// （<c>ProductTransportFactory</c> 的每局装配步骤），结果交给 <see cref="SimParamsActivation.Resolve"/>
    /// 算出本局生效数值；运行期不再每次问门，对局中改滑杆只影响下一局。
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
