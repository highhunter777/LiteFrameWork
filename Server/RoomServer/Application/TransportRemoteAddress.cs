using LiteNet.Transport;

namespace RoomServer.Application
{
    /// <summary>
    /// <see cref="IRoomRemoteAddress"/> 的 <see cref="IRoomTransport"/> 适配（组合根装配点）。
    ///
    /// 为什么需要这层：<see cref="JoinAdmissionGate"/> 的 IP 维度限流只依赖"能不能拿到远端地址"
    /// 这一件事，不该认识 Transport（传输可替换、且地址探测是可选能力）。
    /// 宿主在构造期把 Transport 包成本口注入准入门——**准入门因此可注入假地址源独立测试**。
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
