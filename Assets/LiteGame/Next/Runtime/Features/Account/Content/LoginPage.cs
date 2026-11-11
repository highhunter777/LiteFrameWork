using System;
using LiteFrameworkNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Account.Content
{
    /// <summary>登录页（P0）：游客登录按钮 + 状态文本；只依赖 AccountFeature，不碰 HTTP/平台服务。</summary>
    public sealed class LoginPage : PageBase
    {
        private const string Title = "LiteGame Next";
        private const string IdleStatus = "未登录";
        private const string SignedInStatus = "已登录（游客）";

        /// <summary>页面元数据：地址是 P0 编辑器装载用的 prefab 资源路径（Player 换 YooAsset location）。</summary>
        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Account/Content/LoginPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.Lru);

        private AccountFeature _account;

        /// <summary>注入账号功能；打开页面时由 AccountFeature 调用（缺省时点击只告警，不伪造登录）。</summary>
        public void Bind(AccountFeature account) => _account = account;

        /// <summary>每次展示按当前登录态刷新文本/可交互位，并登记游客登录按钮。</summary>
        protected override void OnShow()
        {
            SetText("Txt_Title", Title);
            RefreshState();
            OnClick("Btn_GuestLogin", SignInAsGuest);
        }

        private bool IsSignedIn => _account != null && _account.IsSignedIn;

        private void SignInAsGuest()
        {
            if (_account == null)
            {
                Log.Warning("LoginPage 未绑定 AccountFeature：游客登录不可用");
                return;
            }

            _account.SignInAsGuest(RefreshState);
        }

        private void RefreshState()
        {
            bool signedIn = IsSignedIn;
            SetText("Txt_Status", signedIn ? SignedInStatus : IdleStatus);
            SetInteractable("Btn_GuestLogin", !signedIn);
        }
    }
}
