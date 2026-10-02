namespace RoomServer.Application
{
    /// <summary>
    /// 广播管线的"一个席位"读写视图：连接号 + 连接代次 + 断开标记 + 广播记账。
    ///
    /// 两种执行形态共用同一管线实现，靠本视图隔开"谁在写"：
    /// - **宿主 owner 形态**：由 <see cref="Session"/> 实现（连接与会话一一对应，视图即会话本身）；
    /// - **Worker 执行形态**：由房间本地的每席位槽实现——连接事实来自 Runtime 席位
    ///   （`SeatOf(playerId).ConnectionId`），由驱动该房间的 Worker 单线程刷新。
    /// </summary>
    public interface IBroadcastSeat
    {
        /// <summary>目标连接号（-1 = 无连接，发送自然跳过）。</summary>
        int ConnectionId { get; }

        /// <summary>连接代次（0 = 不启用代次校验；宿主侧发送按它丢弃连接复用后的迟到帧）。</summary>
        long SessionEpoch { get; }

        /// <summary>断开标记（断开席位不接收增量广播）。</summary>
        bool Disconnected { get; }

        /// <summary>广播记账（水位/ledger/ACK/背压档——见 <see cref="BroadcastAccount"/>）。</summary>
        BroadcastAccount Account { get; }
    }
}