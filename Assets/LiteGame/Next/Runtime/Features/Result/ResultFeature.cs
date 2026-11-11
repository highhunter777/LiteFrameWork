using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Features.Battle;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Result
{
    /// <summary>结算功能：只消费 BattleResult（不认 BattleContext）；显示结算并等待返回。</summary>
    public sealed class ResultFeature : IDisposable
    {
        private readonly UiRuntime _ui;

        public ResultFeature(UiRuntime ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        public BattleResult LastResult { get; private set; }

        /// <summary>打开结算页；页面关闭后返回，结果保留供流程读取。</summary>
        public async UniTask ShowAsync(BattleResult result, CancellationToken cancellationToken)
        {
            LastResult = result ?? throw new ArgumentNullException(nameof(result));
            PageHandle handle = await _ui.OpenAsync<ResultPage>(ResultPage.Spec,
                page => page.Bind(result), cancellationToken);
            try
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, handle.Lifetime.Token);
                await linked.Token.WaitUntilCanceled();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!handle.IsClosed) await _ui.CloseAsync(handle);
            }
        }

        public void Dispose() => LastResult = null;
    }
}
