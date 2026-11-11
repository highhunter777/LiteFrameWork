using System;
using LiteFramework.UI;
using LiteGame.Modules.Map;
using LiteGame.Modules.Settings;
using LiteGame.Modules.Meta.Abstractions.Session;

namespace LiteGame.Modules.UI.Pages
{
    /// <summary>页面装配面：组合根唯一创建点；纯持有，不做映射（同程序集单实现，故不留接口）。</summary>
    public sealed class PageServices
    {
        public PageServices(UINavigationController nav, UIService ui, IConfirmDialog confirm,
            IMetaSession meta, IMapProvider maps, IRoomView room, ISettingsPagePort settings)
        {
            Nav = nav ?? throw new ArgumentNullException(nameof(nav));
            UI = ui ?? throw new ArgumentNullException(nameof(ui));
            Confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            Meta = meta ?? throw new ArgumentNullException(nameof(meta));
            Maps = maps ?? throw new ArgumentNullException(nameof(maps));
            Room = room ?? throw new ArgumentNullException(nameof(room));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public UINavigationController Nav { get; }
        public UIService UI { get; }
        public IConfirmDialog Confirm { get; }
        public IMetaSession Meta { get; }
        public IMapProvider Maps { get; }
        public IRoomView Room { get; }
        public ISettingsPagePort Settings { get; }
    }
}
