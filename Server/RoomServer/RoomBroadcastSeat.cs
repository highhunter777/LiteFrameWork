using RoomServer.Application;

using LiteSync.Client;
namespace RoomServer
{
    /// <summary>
    /// Worker 执行形态的房间本地广播席位槽（<see cref="IBroadcastSeat"/>）。
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