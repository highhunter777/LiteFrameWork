using System.Collections.Generic;

namespace MetaWire
{
    /// <summary>实例注册/心跳：<c>{ instanceId, buildHash, address, maxRooms, roomCount, maxPlayers,
    /// playerCount, draining, rooms }</c>。重复提交同一 instanceId = 更新（幂等键）。</summary>
    public sealed class InstanceRegisterRequest
    {
        public string InstanceId { get; set; }
        public string BuildHash { get; set; }
        public string Address { get; set; }
        public int MaxRooms { get; set; }
        public int RoomCount { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayerCount { get; set; }
        public bool Draining { get; set; }

        /// <summary>房间明细（可空/空 = 未上报明细，旧实例兼容；终态房间不上报）。</summary>
        public List<LobbyRoomEntry> Rooms { get; set; }
    }
}
