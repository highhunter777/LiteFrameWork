using LiteFramework.UI;

namespace LiteGame.Modules.UI.Pages.Screens.Hub
{
    /// <summary>房间页导航数据：房号（换票结果；空 = 不显房号）。</summary>
    public sealed class RoomPageData : IUIData
    {
        public readonly string RoomId;

        public RoomPageData(string roomId) => RoomId = roomId ?? string.Empty;
    }
}
