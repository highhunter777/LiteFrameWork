namespace MetaWire
{
    /// <summary>房间明细行（列表投影与实例上报共用同一形状）。</summary>
    public sealed class LobbyRoomEntry
    {
        public string RoomId { get; set; }

        /// <summary>对局阶段（内核 MatchPhase 数值；两端值域同源）。</summary>
        public int Phase { get; set; }

        public int Players { get; set; }

        /// <summary>模板期望人数（0 = 房间未配置）。</summary>
        public int ExpectedPlayers { get; set; }

        /// <summary>地图行键（0 = 未标注）。</summary>
        public int MapId { get; set; }
    }
}
