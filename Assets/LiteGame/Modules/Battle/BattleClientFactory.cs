using LiteNet.Transport;

namespace LiteGame
{
    /// <summary>
    /// 对局客户端工厂（Battle 域实现）：**生产代码唯一 `new BattleClient` 处**——会话域只持
    /// <see cref="IBattleClientFactory"/> 端口，Account 会话装配经它建客户端（A1 解耦）。
    ///
    /// 传输由调用方装配并传入（KCP 安全信封不在这里改变 fail-closed 口径）；本类只做构造转发。
    /// </summary>
    public sealed class BattleClientFactory : IBattleClientFactory
    {
        public IBattleClient Create(string host, int port, string roomId, string token, string buildHash,
            IClientTransport transport)
            => new BattleClient(host, port, roomId, token, buildHash, transport);
    }
}
