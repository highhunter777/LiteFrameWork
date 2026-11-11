namespace MetaWire
{
    /// <summary>票据响应（换票与建房共用形状）：<c>{ instanceId, address, roomId, ticket, expiresAtMs }</c>。</summary>
    public sealed class JoinTicketInfo
    {
        public string InstanceId { get; set; }
        public string Address { get; set; }
        public string RoomId { get; set; }
        public string Ticket { get; set; }
        public long ExpiresAtMs { get; set; }
    }
}
