using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;

namespace LiteGame.Modules.UI.Pages.Screens.Auth
{
    /// <summary>登录页：游客一键登录（成功替换大厅、失败页内重试）、设置入口、构建版本号。</summary>
    public sealed class Page_LoginLogic : PageLogicBase
    {
        private bool _busy;

        protected override void OnShow(IUIData data)
        {
            PageServices services = Services;
            if (services == null) return;
            SetTextKey("Txt_Title", "UI.Login.Title");
            SetTextKey("Txt_GuestLogin", "UI.Login.GuestEnter");
            SetTextKey("Txt_Settings", "Common.Settings");
            SetTextKey("Txt_Version", "Common.Version", services.Meta.Version);
            SetBusy(false);
            OnButtonClick("Btn_GuestLogin", Login);
            OnButtonClick("Btn_Settings", () => Go(PageIds.Page_Settings));
        }

        /// <summary>忙碌/配置态：未配置 = 常驻 NoServer 且禁钮；在途 = 禁钮 + Connecting。</summary>
        private void SetBusy(bool busy)
        {
            _busy = busy;
            IMetaSession meta = Meta;
            if (!meta.IsConfigured)
            {
                SetVisible("Txt_Status", true);
                SetTextKey("Txt_Status", "UI.Login.NoServer");
                SetInteractable("Btn_GuestLogin", false);
                return;
            }
            SetInteractable("Btn_GuestLogin", !busy);
            SetVisible("Txt_Status", busy);
            if (busy) SetTextKey("Txt_Status", "UI.Login.Connecting");
        }

        private void Login() => LoginAsync().ForgetLogged("Page_Login");

        private async UniTask LoginAsync()
        {
            IMetaSession meta = Meta;
            if (_busy || !meta.IsConfigured) return;

            SetBusy(true);
            Index.Pulse("Btn_GuestLogin");
            int generation = Form.DisplayGeneration;

            MetaResult<bool> result;
            try { result = await meta.GuestLoginAsync(Form.DisplayToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Error("登录失败:" + ex.Message, LogTag.Meta);
                result = MetaResult<bool>.Fail("Internal");
            }
            if (!Form.IsDisplayCurrent(generation)) return;

            _busy = false;
            if (result.Ok)
            {
                Replace(PageIds.Page_Lobby);                 // 替换：返回栈不残留
                return;
            }
            SetInteractable("Btn_GuestLogin", true);
            SetVisible("Txt_Status", true);
            SetTextKey("Txt_Status", "UI.Login.Failed");
            Index.ShowToast(Locale != null ? Locale.Get("UI.Login.Failed") : "UI.Login.Failed");
        }

        protected override void OnPageHide() => _busy = false;
    }
}
