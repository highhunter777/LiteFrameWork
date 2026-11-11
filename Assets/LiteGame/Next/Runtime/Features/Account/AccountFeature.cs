using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Account.Content;
using LiteGameNext.Platform.Meta;
using LiteGameNext.Platform.Ui;
using MetaWire;

namespace LiteGameNext.Features.Account
{
    /// <summary>账号功能：登录态/令牌/登录页；有 MetaClient 走真实游客登录，无则保留 P0 假账号。</summary>
    public sealed class AccountFeature : IDisposable
    {
        private readonly UiRuntime _ui;
        private readonly MetaClient _meta;
        private readonly Func<string> _deviceId;
        private UniTaskCompletionSource _loginCompletion;

        public AccountFeature(UiRuntime ui, MetaClient meta = null, Func<string> deviceId = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _meta = meta;
            _deviceId = deviceId ?? (() => string.Empty);
        }

        public bool IsSignedIn { get; private set; }

        /// <summary>登录失败原因（空 = 无失败）；仅用于页面展示，不参与判定。</summary>
        public string LastError { get; private set; } = string.Empty;

        public string AccessToken { get; private set; } = string.Empty;

        public string AccountId { get; private set; } = string.Empty;

        /// <summary>初始化功能；重复调用幂等。</summary>
        public void Initialize()
        {
            Log.Info("AccountFeature 就绪（meta=" + (_meta != null) + "，ui=" + _ui.IsInitialized + "）");
        }

        /// <summary>打开登录页并等登录完成；登录页被关闭返回 false，App 取消抛 OperationCanceledException。</summary>
        public async UniTask<bool> ShowLoginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsSignedIn) return true;

            _loginCompletion = new UniTaskCompletionSource();
            PageHandle handle = await _ui.OpenAsync<LoginPage>(
                LoginPage.Spec, page => page.Bind(this), cancellationToken);
            try
            {
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, handle.Lifetime.Token))
                {
                    await _loginCompletion.Task.AttachExternalCancellation(linked.Token);
                }

                Log.Info("登录完成：IsSignedIn=" + IsSignedIn);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && handle.IsClosed)
            {
                Log.Info("登录页被关闭：本次登录取消");
                return false;
            }
            finally
            {
                _loginCompletion = null;
                if (!handle.IsClosed) await _ui.CloseAsync(handle);
            }
        }

        /// <summary>真实游客登录：有 MetaClient 走 HTTP；无则回退 P0 假账号；失败保留 LastError 供页面重试。</summary>
        public async UniTask<bool> SignInAsync(CancellationToken cancellationToken = default)
        {
            if (IsSignedIn) return true;
            if (_meta == null)
            {
                SignInAsGuest();
                return true;
            }

            LastError = string.Empty;
            MetaCallResult<GuestLoginInfo> result = await _meta.GuestLoginAsync(_deviceId(), cancellationToken);
            if (!result.Success)
            {
                LastError = result.Error.Message;
                Log.Warning("游客登录失败：" + result.Error);
                return false;
            }

            AccessToken = result.Value.AccessToken ?? string.Empty;
            AccountId = result.Value.AccountId ?? string.Empty;
            IsSignedIn = true;
            Log.Info("游客登录成功：account=" + AccountId);
            _loginCompletion?.TrySetResult();
            return true;
        }

        /// <summary>P0 假游客登录（测试/离线）：置状态并完成等待中的登录流程。</summary>
        public void SignInAsGuest(Action onSignedIn = null)
        {
            if (!IsSignedIn)
            {
                IsSignedIn = true;
                Log.Info("AccountFeature 游客登录（P0 假账号）：IsSignedIn=true");
            }

            onSignedIn?.Invoke();
            _loginCompletion?.TrySetResult();
        }

        /// <summary>关闭功能并复位登录态。</summary>
        public void Shutdown()
        {
            IsSignedIn = false;
            AccessToken = string.Empty;
            AccountId = string.Empty;
            LastError = string.Empty;
            _loginCompletion = null;
        }

        public void Dispose() => Shutdown();
    }
}
