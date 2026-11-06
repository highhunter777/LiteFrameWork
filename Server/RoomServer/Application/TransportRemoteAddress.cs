using LiteNet.Transport;

namespace RoomServer.Application
{
    /// <summary>
    /// <see cref="IRoomRemoteAddress"/> 的 <see cref="IRoomTransport"/> 适配（组合根装配点）。
    /// </summary>
    internal sealed class TransportRemoteAddress : IRoomRemoteAddress
    {
        private readonly IRoomTransport _transport;

        public TransportRemoteAddress(IRoomTransport transport)
        {
            _transport = transport ?? throw new System.ArgumentNullException(nameof(transport));
        }

        /// <summary>探测不到返回 null（原样透传）——调用方按"限不了看不见的地址"处理，不在此处伪造。</summary>
        public string GetRemoteAddress(int connectionId) => _transport.GetRemoteAddress(connectionId);
    }
}
