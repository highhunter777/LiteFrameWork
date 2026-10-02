using RoomServer.Application;

namespace RoomServer
{
    /// <summary>
    /// Worker 执行形态的房间本地广播席位槽（<see cref="IBroadcastSeat"/>）。
    ///
    /// 与宿主 owner 形态的差异：那种形态一个连接 = 一个会话 = 一个席位视图；
    /// Worker 形态下一个 <c>playerId</c> 的席位会跨连接存续（重连换绑），因此把"广播面向的
    /// 连接事实"与"广播记账"收在房间本地，由归属 Worker **单线程**刷新（<see cref="RoomInstance.WorkerSeats"/>）：
    /// - 连接号/断开标记来自 Runtime 席位（`SeatOf(playerId).ConnectionId` / 相位）——单一真相；
    /// - 连接代次来自房间命令信封（Join/Rebind 的 <c>SessionEpoch</c>）——宿主据此丢弃连接复用后的迟到帧；
    /// - 广播记账在 <see cref="Broadcast"/>，换绑时由 Worker 重置（新连接没有历史发送记录）。
    /// </summary>
    internal sealed class RoomBroadcastSeat : IBroadcastSeat
    {
        /// <summary>当前绑定连接号（<see cref="RoomOutboundEnvelope.NoConnectionId"/> = 未绑定）。</summary>
        public int ConnectionId = RoomOutboundEnvelope.NoConnectionId;

        /// <summary>当前绑定连接的代次（0 = 未知，宿主不启用代次校验）。</summary>
        public long SessionEpoch;

        /// <summary>断开标记（未绑定或席位断开为 true——管线据此跳过）。</summary>
        public bool Disconnected = true;

        /// <summary>本席位广播记账（与宿主 owner 形态共用同一实现）。</summary>
        public readonly BroadcastAccount Broadcast = new BroadcastAccount();

        int IBroadcastSeat.ConnectionId
        {
            get { return ConnectionId; }
        }

        long IBroadcastSeat.SessionEpoch
        {
            get { return SessionEpoch; }
        }

        bool IBroadcastSeat.Disconnected
        {
            get { return Disconnected; }
        }

        BroadcastAccount IBroadcastSeat.Account
        {
            get { return Broadcast; }
        }
    }
}