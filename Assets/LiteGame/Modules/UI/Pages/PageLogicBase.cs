using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Map;
using LiteGame.Modules.Settings;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面逻辑基类：UIBindBase ＋ 装配面（PageServices）＋ 关闭即统一解绑。
    /// 页面覆写 <c>OnPageHide</c>；<c>OnHide</c> 由本类封口，先跑页面收尾再统一解绑。
    /// </summary>
    public abstract class PageLogicBase : UIBindBase
    {
        private PageServices _services;

        /// <summary>页面装配面（未注入 = null；需要能力的页面在 OnShow 早退）。</summary>
        protected PageServices Services
        {
            get
            {
                if (_services == null && Form != null) _services = Form.PageContext as PageServices;
                return _services;
            }
        }

        /// <summary>导航单写者。</summary>
        protected UINavigationController Nav => Require(Services?.Nav, "导航");

        /// <summary>联机业务面。</summary>
        protected IMetaSession Meta => Require(Services?.Meta, "联机面");

        /// <summary>地图表。</summary>
        protected IMapProvider Maps => Require(Services?.Maps, "地图表");

        /// <summary>房间会话读口。</summary>
        protected IRoomView Room => Require(Services?.Room, "房间读口");

        /// <summary>设置页端口。</summary>
        protected ISettingsPagePort Settings => Require(Services?.Settings, "设置端口");

        /// <summary>确认弹窗端口。</summary>
        protected IConfirmDialog Confirm => Require(Services?.Confirm, "确认弹窗");

        /// <summary>关闭本页（经 UI 服务统一关闭路径；未装配 = no-op）。</summary>
        protected void CloseSelf()
        {
            UIService ui = Services?.UI;
            if (ui == null || Form == null || !ui.IsOpen(Form.Id)) return;
            ui.CloseAsync(Form.Id).ForgetLogged("PageLogic");
        }

        /// <summary>压栈打开（fire-and-forget；异常进日志，与 PageFlow 同口径）。</summary>
        protected void Go(int pageId, IUIData data = null) => Navigate(pageId, data, replace: false);

        /// <summary>替换打开（返回栈不残留）。</summary>
        protected void Replace(int pageId, IUIData data = null) => Navigate(pageId, data, replace: true);

        private void Navigate(int pageId, IUIData data, bool replace)
        {
            UINavigationController nav = Nav;
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

        private static T Require<T>(T value, string name) where T : class
            => value ?? throw new InvalidOperationException("页面未注入 PageServices（缺 " + name + "）");
    }
}
