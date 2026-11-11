using System.Text;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Platform.Ui;
using MetaWire;

namespace LiteGameNext.Features.Records
{
    /// <summary>战绩页：最近对局列表 + 刷新/关闭；只读 RecordsFeature。</summary>
    public sealed class RecordsPage : PageBase
    {
        private const string Title = "战绩";

        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Records/Content/RecordsPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.Lru);

        private RecordsFeature _feature;

        public void Bind(RecordsFeature feature) => _feature = feature;

        protected override void OnShow()
        {
            SetText("Txt_Title", Title);
            OnClick("Btn_Refresh", Refresh);
            OnClick("Btn_Close", Close);
            RefreshView();
            Run(RefreshAsync());
        }

        private void Refresh() => Run(RefreshAsync());

        private void Close()
        {
            if (Handle == null) return;
            Handle.CloseAsync().Forget(ex => Log.Error("关闭战绩页失败：" + ex));
        }

        private void Run(UniTask operation) => operation.Forget(ex => Log.Error("战绩操作失败：" + ex));

        private async UniTask RefreshAsync()
        {
            if (_feature == null) return;
            await _feature.RefreshAsync(CancellationToken);
            if (Handle != null && !Handle.IsClosed) RefreshView();
        }

        private void RefreshView()
        {
            if (_feature == null) return;
            SetText("Txt_Status", _feature.Status);
            SetText("Txt_Records", Describe(_feature.Matches));
            SetInteractable("Btn_Refresh", !_feature.IsBusy);
        }

        private static string Describe(System.Collections.Generic.IReadOnlyList<MatchRecordInfo> matches)
        {
            if (matches == null || matches.Count == 0) return "暂无战绩";
            var text = new StringBuilder();
            for (int index = 0; index < matches.Count; index++)
            {
                MatchRecordInfo match = matches[index];
                if (index > 0) text.Append('\n');
                text.Append(match.MatchId).Append("  K ").Append(match.Kills).Append(" / D ").Append(match.Deaths);
            }

            return text.ToString();
        }
    }
}
