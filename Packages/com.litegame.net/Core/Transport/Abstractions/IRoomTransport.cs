using System;
namespace LiteNet.Transport
{
    /// <summary>
    /// 房间传输窄端口（《状态同步实施方案》§4.6 可替换性）： FrameAggregator / AuthSim / SnapshotDiffer 等房间业务组件只依赖这 3 类操作 + 连接元数据 + 3 个事件。
    /// </summary>
    public interface IRoomTransport : IDisposable
    {
        /// <summary>
        /// 绑定并启动监听。
        /// </summary>
        void Start(int port);

        /// <summary>
        /// 实际绑定的端口（<see cref="Start"/> 之后有效；未启动/假件返回 -1）。
        /// 与 <c>Start(0)</c> 配合即"系统分配 + 回读"，调用方据此连接。
        /// </summary>
        int BoundPort { get; }

        void TickIncoming();
        void TickOutgoing();

        void SendTo(int connectionId, ArraySegment<byte> data, bool reliable);
        void Broadcast(ArraySegment<byte> data, bool reliable);
        void Disconnect(int connectionId);

        /// <summary>
        /// 规范化远端地址（**无端口**；IPv4 点分 / IPv6 压缩小写，IPv4-mapped 归一为 IPv4—— 规则见 <see cref="RemoteAddress"/>）。
        /// </summary>
        string GetRemoteAddress(int connectionId);

        /// <summary>connectionId、载荷、是否来自可靠通道。</summary>
        event Action<int, ArraySegment<byte>, bool> OnData;
        event Action<int> OnConnected;
        event Action<int> OnDisconnected;
    }
}
