using System;

namespace LiteNet.Transport
{
    /// <summary>
    /// 房间传输窄端口（《状态同步实施方案》§4.6 可替换性）：
    /// FrameAggregator / AuthSim / SnapshotDiffer 等房间业务组件只依赖这 3 类操作 + 连接元数据
    /// + 3 个事件，不感知 KcpServer——换传输框架 / 拆 Gate 时 Sim 代码零改动。
    /// 驱动由业务循环负责（MVP 单循环：每 tick 顺序 TickIncoming → 逻辑 → TickOutgoing，~10ms 节拍）。
    /// </summary>
    public interface IRoomTransport : IDisposable
    {
        /// <summary>
        /// 绑定并启动监听。<paramref name="port"/> = 0 时由**系统分配空闲端口**，
        /// 实际端口经 <see cref="BoundPort"/> 回读——测试与多实例并行必须走这条（固定端口
        /// 会与同机其他软件冲突：实测本机 aTrustXtunnel 占 7777/7778 曾致测试偶发失败）。
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
        /// 规范化远端地址（**无端口**；IPv4 点分 / IPv6 压缩小写，IPv4-mapped 归一为 IPv4——
        /// 规则见 <see cref="RemoteAddress"/>）。per-IP 限流的**唯一**可信来源：
        /// 《商业级通用服务端框架总设计》§P0-6"Transport 必须提供规范化远端地址和连接元数据
        /// 后才可宣称实现 per-IP 限流"。
        /// 未知连接（未握手/已断开/假件未设定）返回 null——调用方对 null 跳过 IP 维度限流。
        /// </summary>
        string GetRemoteAddress(int connectionId);

        /// <summary>connectionId、载荷、是否来自可靠通道。</summary>
        event Action<int, ArraySegment<byte>, bool> OnData;
        event Action<int> OnConnected;
        event Action<int> OnDisconnected;
    }
}
