using System;

namespace LiteNet.Transport
{
    /// <summary>
    /// 客户端传输窄端口（《状态同步实施方案》§4.6 可替换性的**客户端侧**）：
    /// `RoomClient` 只依赖这几个操作与 3 个事件，不感知 kcp2k——换传输实现、或写假件做单测零改动。
    ///
    /// 与 <see cref="IRoomTransport"/>（服务端侧）同款理由：业务组件依赖窄端口，不依赖具体框架。
    /// `KcpTransportClient` 是 sealed 的，解耦后"订阅是否退订"这类生命周期行为可用假件验证（它正是最容易漏的一类 bug）。
    /// </summary>
    public interface IClientTransport : IDisposable
    {
        bool Connected { get; }

        void Connect(string address, int port);
        void Disconnect();
        void TickIncoming();
        void TickOutgoing();

        /// <summary>发送一段已编码载荷（实现须**同步拷贝**，调用方随后可复用缓冲）。</summary>
        void Send(ArraySegment<byte> data, bool reliable);

        event Action OnConnected;

        /// <summary>载荷、是否来自可靠通道。</summary>
        event Action<ArraySegment<byte>, bool> OnData;
        event Action OnDisconnected;
    }
}
