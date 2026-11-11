using System;
using LiteFrameworkNext;

namespace LiteGameNext.Platform.Ui.Content
{
    /// <summary>系统错误页（P0）：标题/消息 + 重试/回登录；由 UiRuntime 兜底打开，不属于任何功能。</summary>
    public sealed class ErrorPage : PageBase
    {
        private const string DefaultTitle = "出错了";

        /// <summary>页面元数据：System 层全屏常驻（错误页重开代价高，排 Back 优先级最前）。</summary>
        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Platform/Ui/Content/ErrorPage.prefab",
            PageLayer.System, fullScreen: true, PageCachePolicy.Resident);

        private Action _retry;
        private Action _backToLogin;

        /// <summary>注入错误内容与两个动作；动作传 null 时按钮只记日志（P0 兜底路径）。</summary>
        public void Bind(string title, string message, Action retry = null, Action backToLogin = null)
        {
            _retry = retry;
            _backToLogin = backToLogin;
            SetText("Txt_Title", string.IsNullOrEmpty(title) ? DefaultTitle : title);
            SetText("Txt_Message", message ?? string.Empty);
        }

        /// <summary>每次展示登记两个按钮（关闭时由 PageBase 统一解绑）。</summary>
        protected override void OnShow()
        {
            OnClick("Btn_Retry", OnRetry);
            OnClick("Btn_BackLogin", OnBackToLogin);
        }

        private void OnRetry()
        {
            Log.Info("ErrorPage 重试");
            _retry?.Invoke();
        }

        private void OnBackToLogin()
        {
            Log.Info("ErrorPage 回登录");
            _backToLogin?.Invoke();
        }
    }
}
