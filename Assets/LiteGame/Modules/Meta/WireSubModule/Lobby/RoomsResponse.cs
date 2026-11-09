using System.Collections.Generic;

namespace MetaWire
{
    /// <summary>房间列表响应：<c>{ rooms: [ ... ] }</c>。</summary>
    public sealed class RoomsResponse
    {
        public List<LobbyInstanceView> Rooms { get; set; }
    }
}
