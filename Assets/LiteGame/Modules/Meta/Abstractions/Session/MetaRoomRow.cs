namespace LiteGame
{
    /// <summary>大厅房间行（页面列表投影）。</summary>
    public sealed class MetaRoomRow
    {
        public readonly string RoomId;
        public readonly int Players;
        public readonly int ExpectedPlayers;
        public readonly int MapId;
        public readonly string MapName;

        public MetaRoomRow(string roomId, int players, int expectedPlayers, int mapId, string mapName)
        {
            RoomId = roomId ?? string.Empty;
            Players = players;
            ExpectedPlayers = expectedPlayers;
            MapId = mapId;
            MapName = mapName ?? string.Empty;
        }
    }
}
