using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Account.Content;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Account
{
    /// <summary>账号功能入口（P0 假账号）：持登录态与登录页；游客登录只置状态，P1 由 MetaClient 真实登录替换。</summary>
    public sealed class AccountFeature : IDisposable
    {
        private readonly UiRuntime _ui;
        private UniTaskCompletionSource _loginCompletion;

        /// <summary>构造：账号功能只依赖 UI 运行时（P1 再加 Meta 客户端与设置切片）。</summary>
        public AccountFeature(UiRuntime ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        /// <summary>登录态：游客登录成功后为 true，关闭功能时复位。</summary>
        public bool IsSignedIn { get; private set; }

        /// <summary>初始化功能；重复调用幂等。</summary>
        public void Initialize()
        {
            Log.Info("AccountFeature 就绪（P0 假账号，ui=" + _ui.IsInitialized + "）");
        }

        /// <summary>打开登录页并等游客登录完成；登录页被关闭返回 false（调用方回登录），App 取消抛 OperationCanceledException。</summary>
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

                Log.Info("游客登录完成：IsSignedIn=" + IsSignedIn);
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

        /// <summary>P0 假游客登录：置登录态、刷新页面回调并完成等待中的登录流程（P1 换 MetaClient）。</summary>
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
            _loginCompletion = null;
        }

        /// <summary>为 Lifetime 逆序释放提供入口。</summary>
        public void Dispose() => Shutdown();
    }
}
