using System;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Account.Content
{
    /// <summary>登录页：游客登录按钮 + 状态文本；只依赖 AccountFeature，不碰 HTTP/平台服务。</summary>
    public sealed class LoginPage : PageBase
    {
        private const string Title = "LiteGame Next";
        private const string IdleStatus = "未登录";
        private const string SignedInStatus = "已登录（游客）";

        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Account/Content/LoginPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.Lru);

        private AccountFeature _account;

        public void Bind(AccountFeature account) => _account = account;

        protected override void OnShow()
        {
            SetText("Txt_Title", Title);
            RefreshState();
            OnClick("Btn_GuestLogin", SignIn);
        }

        private bool IsSignedIn => _account != null && _account.IsSignedIn;

        private void SignIn() => SignInAsync().Forget(ex => Log.Error("登录失败：" + ex));

        private async UniTask SignInAsync()
        {
            if (_account == null)
            {
                Log.Warning("LoginPage 未绑定 AccountFeature：游客登录不可用");
                return;
            }

            SetInteractable("Btn_GuestLogin", false);
            await _account.SignInAsync(CancellationToken);
            if (Handle != null && !Handle.IsClosed) RefreshState();
        }

        private void RefreshState()
        {
            bool signedIn = IsSignedIn;
            string status = signedIn
                ? SignedInStatus
                : string.IsNullOrEmpty(_account?.LastError) ? IdleStatus : _account.LastError;
            SetText("Txt_Status", status);
            SetInteractable("Btn_GuestLogin", !signedIn);
        }
    }
}
