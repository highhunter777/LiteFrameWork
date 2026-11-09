using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面逻辑基类：UIBindBase ＋ 装配上下文 ＋ 关闭即统一解绑。
    /// 页面覆写 <c>OnPageHide</c>；<c>OnHide</c> 由本类封口，先跑页面收尾再统一解绑。
    /// </summary>
    public abstract class PageLogicBase : UIBindBase
    {
        private IPageContext _context;

        /// <summary>页面上下文（未装配 = null；页面按"能力缺失显性拒绝"处理）。</summary>
        protected IPageContext Ctx
        {
            get
            {
                if (_context == null && Form != null) _context = Form.PageContext as IPageContext;
                return _context;
            }
        }

        /// <summary>导航单写者（null = 未装配）。</summary>
        protected UINavigationController Nav => Ctx?.Nav;

        /// <summary>联机业务端口（null = 未装配）。</summary>
        protected IMetaSession Meta => Ctx?.Meta;

        /// <summary>地图目录端口（null = 未装配）。</summary>
        protected IMapCatalog Maps => Ctx?.Maps;

        /// <summary>房间会话读口（null = 未装配）。</summary>
        protected IRoomView Room => Ctx?.Room;

        /// <summary>设置页端口（null = 未装配）。</summary>
        protected ISettingsPagePort Settings => Ctx?.Settings;

        /// <summary>关闭本页（经 UI 服务统一关闭路径；未装配 = no-op）。</summary>
        protected void CloseSelf()
        {
            UIService ui = Ctx?.UI;
            if (ui == null || Form == null || !ui.IsOpen(Form.Id)) return;
            ui.CloseAsync(Form.Id).ForgetLogged("PageLogic");
        }

        /// <summary>压栈打开（fire-and-forget；异常进日志，与 PageFlow 同口径）。</summary>
        protected void Go(int pageId, IUIData data = null) => Navigate(pageId, data, replace: false);

        /// <summary>替换打开（返回栈不残留）。</summary>
        protected void Replace(int pageId, IUIData data = null) => Navigate(pageId, data, replace: true);

        private void Navigate(int pageId, IUIData data, bool replace)
        {
            UINavigationController nav = Nav
                ?? throw new InvalidOperationException("页面未注入 IPageContext（Form.PageContext）——导航不可用");
            UniTask<UIForm> task = replace ? nav.ReplaceAsync(pageId, data) : nav.GoAsync(pageId, data);
            UniTask<UIForm>.Awaiter awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                try { awaiter.GetResult(); }
                catch (Exception ex) { Log.Error("页面导航[" + pageId + "]:" + ex.Message, LogTag.UI); }
            });
        }

        /// <summary>关闭收尾（页面唯一覆写点；按钮/订阅/列表解绑由基类收口）。</summary>
        protected sealed override void OnHide()
        {
            OnPageHide();
            Index.UnbindAll();          // 按钮/订阅/列表解绑（含语言订阅；复用前框架会重新注入）
        }

        /// <summary>页面关闭回调（此时尚未解绑；只清自有状态）。</summary>
        protected virtual void OnPageHide() { }
    }
}
