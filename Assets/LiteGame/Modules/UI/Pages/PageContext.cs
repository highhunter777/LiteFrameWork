using System;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>IPageContext 实现：导航延迟取（UI 段内 UIService 先建、导航器后建）。</summary>
    public sealed class PageContext : IPageContext
    {
        private readonly Func<UINavigationController> _nav;
        private readonly Func<IMetaSession> _meta;
        private readonly Func<IMapCatalog> _maps;
        private readonly Func<IRoomView> _room;
        private readonly Func<IConfirmDialog> _confirm;
        private readonly Func<UIService> _ui;

        public PageContext(Func<UINavigationController> nav, Func<IMetaSession> meta = null,
            Func<IMapCatalog> maps = null, Func<IRoomView> room = null,
            Func<IConfirmDialog> confirm = null, Func<UIService> ui = null)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _meta = meta;
            _maps = maps;
            _room = room;
            _confirm = confirm;
            _ui = ui;
        }

        public UINavigationController Nav => _nav();
        public IMetaSession Meta => _meta?.Invoke();
        public IMapCatalog Maps => _maps?.Invoke();
        public IRoomView Room => _room?.Invoke();
        public IConfirmDialog Confirm => _confirm?.Invoke();
        public UIService UI => _ui?.Invoke();
    }
}
