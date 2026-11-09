namespace MetaWire
{
    /// <summary>建房请求：<c>{ requestId, buildHash, mapId, expectedPlayers, template, gmKey }</c>。
    /// 客户端不发明房号；0 = 未指定（按服务端默认图/房间模板）。模板非空时口令必须匹配。</summary>
    public sealed class CreateRoomRequest
    {
        public string RequestId { get; set; }
        public string BuildHash { get; set; }
        public int MapId { get; set; }
        public int ExpectedPlayers { get; set; }
        public string Template { get; set; }
        public string GmKey { get; set; }
    }
}
