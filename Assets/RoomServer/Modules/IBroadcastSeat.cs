namespace RoomServer.Application
{
    /// <summary>
    /// 广播管线的"一个席位"读写视图：连接号 + 连接代次 + 断开标记 + 广播记账。
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