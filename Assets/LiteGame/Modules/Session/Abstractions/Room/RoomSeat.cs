namespace LiteGame
{
    /// <summary>房间席位（页面投影：席位号/名字/在线）。</summary>
    public readonly struct RoomSeat
    {
        public readonly long PlayerId;
        public readonly string Name;
        public readonly bool Online;

        public RoomSeat(long playerId, string name, bool online)
        {
            PlayerId = playerId;
            Name = name ?? string.Empty;
            Online = online;
        }
    }
}
