using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Platform.Ui;
using LiteGameNext.Platform.Ui.Content;

namespace LiteGameNext
{
    /// <summary>顶层流程（P0）：登录 → 占位页 → 等取消；失败打开错误页。流程即 async 调用栈，无阶段机/流程枚举。</summary>
    public sealed class AppFlow
    {
        private const string ErrorTitle = "启动失败";

        private readonly UiRuntime _ui;
        private readonly AccountFeature _account;
        private readonly MatchmakingFeature _matchmaking;

        /// <summary>构造：只注入流程用到的 UI/账号/匹配；匹配为空时保留 P0 占位页路径。</summary>
        public AppFlow(UiRuntime ui, AccountFeature account, MatchmakingFeature matchmaking = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _matchmaking = matchmaking;
        }

        /// <summary>执行顶层流程：App 取消 → 静默返回；其他异常 → 打开错误页并等关闭。</summary>
        public async UniTask RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RunCoreAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log.Info("AppFlow 取消：静默退出");
            }
            catch (Exception ex)
            {
                Log.Error("AppFlow 失败：" + ex);
                await ShowErrorAsync(ex, cancellationToken);
            }
        }

        /// <summary>登录 → 占位页 → 等取消；登录页被关闭时回登录，直到登录成功或 App 取消。</summary>
        private async UniTask RunCoreAsync(CancellationToken cancellationToken)
        {
            while (!await _account.ShowLoginAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log.Info("登录被取消（登录页已关闭）：回到登录页");
            }

            if (_matchmaking != null)
            {
                await _matchmaking.OpenAsync(cancellationToken);
                Log.Info("大厅已打开");
            }
            else
            {
                PageHandle handle = await _ui.OpenAsync<PlaceholderPage>(
                    PlaceholderPage.Spec, null, cancellationToken);
                Log.Info("占位页已打开：address=" + handle.Spec.Address
                    + " generation=" + handle.DisplayGeneration);
            }

            // 用 CancellationToken awaitable 而非 UniTask.WaitUntilCanceled：后者依赖 PlayerLoop 帧推进，
            // EditMode 用例无法驱动；两者语义相同（取消即返回）。
            await cancellationToken.WaitUntilCanceled();
        }

        /// <summary>打开错误页（标题/消息/重试/回登录）并等取消；错误页本身打不开只记日志。</summary>
        private async UniTask ShowErrorAsync(Exception error, CancellationToken cancellationToken)
        {
            string message = error == null ? string.Empty : error.Message;
            try
            {
                await _ui.OpenAsync<ErrorPage>(ErrorPage.Spec,
                    page => page.Bind(ErrorTitle, message, OnRetry, OnBackToLogin), cancellationToken);
                await cancellationToken.WaitUntilCanceled();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log.Info("错误页随 App 取消关闭");
            }
            catch (Exception openError)
            {
                Log.Error("错误页打开失败：" + openError);
            }
        }

        // P0：两个动作只记录，P1 接「重进流程」（回登录 = 关错误页后重跑 RunCoreAsync）。
        private static void OnRetry() => Log.Info("错误页重试（P0 仅记录，P1 重进流程）");

        private static void OnBackToLogin() => Log.Info("错误页回登录（P0 仅记录，P1 重进流程）");
    }
}
