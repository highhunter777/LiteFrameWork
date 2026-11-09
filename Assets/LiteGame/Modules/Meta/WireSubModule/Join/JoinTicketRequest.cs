namespace MetaWire
{
    /// <summary>换票请求：<c>{ requestId, roomId, buildHash }</c>（roomId 空 = 服务端默认房）。</summary>
    public sealed class JoinTicketRequest
    {
        public string RequestId { get; set; }
        public string RoomId { get; set; }
        public string BuildHash { get; set; }
    }
}
