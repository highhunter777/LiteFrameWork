using System.Text;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Battle;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Result
{
    /// <summary>结算页：终态/原因/结算行 + 返回；只读 BattleResult，不提交结算。</summary>
    public sealed class ResultPage : PageBase
    {
        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Result/Content/ResultPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.DestroyOnClose);

        private BattleResult _result;

        public void Bind(BattleResult result) => _result = result;

        protected override void OnShow()
        {
            SetText("Txt_Title", "结算");
            SetText("Txt_Outcome", OutcomeText(_result));
            SetText("Txt_Detail", Detail(_result));
            OnClick("Btn_Back", Close);
        }

        private void Close()
        {
            if (Handle == null) return;
            Handle.CloseAsync().Forget(ex => Log.Error("关闭结算页失败：" + ex));
        }

        private static string OutcomeText(BattleResult result)
        {
            if (result == null) return "无结果";
            switch (result.Outcome)
            {
                case BattleOutcome.Completed: return "对局完成";
                case BattleOutcome.SessionFailed: return "会话失败";
                case BattleOutcome.Aborted: return "对局中止";
                case BattleOutcome.LeftByUser: return "已离开";
                default: return result.Outcome.ToString();
            }
        }

        private static string Detail(BattleResult result)
        {
            if (result == null) return string.Empty;
            var text = new StringBuilder();
            if (!string.IsNullOrEmpty(result.Reason)) text.Append(result.Reason).Append('\n');
            for (int index = 0; index < result.Settlement.Count; index++)
            {
                BattleSettlementRow row = result.Settlement[index];
                if (index > 0) text.Append('\n');
                text.Append(row.PlayerId).Append("  K ").Append(row.Kills).Append(" / D ").Append(row.Deaths)
                    .Append("  ").Append(row.Score);
            }

            return text.Length == 0 ? "无结算明细" : text.ToString();
        }
    }
}
