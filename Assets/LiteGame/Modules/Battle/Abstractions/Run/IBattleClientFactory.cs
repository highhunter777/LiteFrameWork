using LiteNet.Transport;

namespace LiteGame.Modules.Battle.Abstractions.Run
{
    /// <summary>
    /// 对局客户端工厂端口：会话域把"进房参数 + 已裁决的传输"交给它换回 IBattleClient。
    /// </summary>
    public interface IBattleClientFactory
    {
        /// <param name="host">房服地址。</param>
        /// <param name="port">房服端口。</param>
        /// <param name="roomId">房号（Join 载荷）。</param>
        /// <param name="token">进房票据（Join 载荷）。</param>
        /// <param name="buildHash">两端构建身份（Join 载荷；握手准入判据）。</param>
        /// <param name="transport">已装配的传输（工厂不接管其构造与安全信封）。</param>
        IBattleClient Create(string host, int port, string roomId, string token, string buildHash,
            IClientTransport transport);
    }
}
