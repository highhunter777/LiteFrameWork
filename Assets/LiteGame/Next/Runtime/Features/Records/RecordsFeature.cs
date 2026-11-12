using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Platform.Meta;
using LiteGameNext.Platform.Ui;
using MetaWire;

namespace LiteGameNext.Features.Records
{
    /// <summary>战绩功能：只读 Account 令牌 + Meta 历史，页面展示；不写结算、不碰战斗上下文。</summary>
    public sealed class RecordsFeature : IDisposable
    {
        private readonly UiRuntime _ui;
        private readonly AccountFeature _account;
        private readonly MetaClient _meta;

        public RecordsFeature(UiRuntime ui, AccountFeature account, MetaClient meta)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        }

        public IReadOnlyList<MatchRecordInfo> Matches { get; private set; } = Array.Empty<MatchRecordInfo>();

        public string Status { get; private set; } = string.Empty;

        public bool IsBusy { get; private set; }

        public UniTask<PageHandle> OpenAsync(CancellationToken cancellationToken)
            => _ui.OpenAsync<RecordsPage>(RecordsPage.Spec, page => page.Bind(this), cancellationToken);

        /// <summary>刷新最近战绩；失败保留旧列表并写 Status。</summary>
        public async UniTask<bool> RefreshAsync(CancellationToken cancellationToken, int limit = 20)
        {
            if (IsBusy) return false;
            IsBusy = true;
            Status = "读取战绩…";
            try
            {
                MetaCallResult<IReadOnlyList<MatchRecordInfo>> result =
                    await _meta.ListRecentMatchesAsync(_account.AccessToken, limit, cancellationToken);
                if (!result.Success)
                {
                    Status = "战绩读取失败：" + result.Error.Code;
                    Log.Warning("Records 读取失败：" + result.Error);
                    return false;
                }

                Matches = result.Value;
                Status = Matches.Count == 0 ? "暂无战绩" : "最近 " + Matches.Count + " 场";
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Dispose() => Matches = Array.Empty<MatchRecordInfo>();
    }
}
