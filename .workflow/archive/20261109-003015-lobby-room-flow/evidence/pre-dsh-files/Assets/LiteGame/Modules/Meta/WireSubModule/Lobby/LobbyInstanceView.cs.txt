using System.Collections.Generic;

namespace MetaWire
{
    /// <summary>实例投影（服务端上报事实的投影，不是权威）。</summary>
    public sealed class LobbyInstanceView
    {
        public string InstanceId { get; set; }
        public string BuildHash { get; set; }

        /// <summary>客户端可达地址 <c>host:port</c>。</summary>
        public string Address { get; set; }

        public int MaxRooms { get; set; }
        public int RoomCount { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayerCount { get; set; }
        public bool Draining { get; set; }

        /// <summary>距最近一次心跳的毫秒数（投影新鲜度）。</summary>
        public long LastSeenAgeMs { get; set; }

        /// <summary>房间明细（空/null = 该实例未上报明细——消费方按"无房"处理，不判整表非法）。</summary>
        public List<LobbyRoomEntry> Rooms { get; set; }
    }
}
