using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Editor.UiTools.Contracts
{
    /// <summary>批量生成的一条产物记录。</summary>
    public sealed class UiContractBatchEntry
    {
        public string PageName;
        public string PrefabPath;
        public string ContractPath;

        /// <summary>草稿契约（未登记 tbuiform；`formId=0`）。</summary>
        public bool Draft;
    }

    /// <summary>批量生成结果：成功/跳过/告警/错误分开，避免"全有或全无"的假结论。</summary>
    public sealed class UiContractBatchResult
    {
        public readonly List<UiContractBatchEntry> Written = new List<UiContractBatchEntry>();
        public readonly List<string> Skipped = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool Ok => Errors.Count == 0;

        public int DraftCount => Written.Count(entry => entry.Draft);

        public string Summary =>
            "契约生成：" + Written.Count + " 页（草稿 " + DraftCount + "） / 跳过 " + Skipped.Count
            + " / 告警 " + Warnings.Count + " / 错误 " + Errors.Count;

        public string ToReport()
        {
            var lines = new List<string> { Summary };
            foreach (UiContractBatchEntry entry in Written)
                lines.Add("  写入 " + entry.PageName + (entry.Draft ? "（草稿）" : string.Empty) + " → " + entry.ContractPath);
            foreach (string item in Skipped) lines.Add("  跳过 " + item);
            foreach (string item in Warnings) lines.Add("  告警 " + item);
            foreach (string item in Errors) lines.Add("  错误 " + item);
            return string.Join("\n", lines);
        }
    }

    /// <summary>单页契约对账结果。</summary>
    public sealed class UiContractDiffEntry
    {
        public string PageName;
        public string ContractPath;
        public UiContractDiffResult Diff;
    }

    /// <summary>批量对账结果：破坏性变更与错误分开计数（破坏性＝失败，其余只是变化）。</summary>
    public sealed class UiContractBatchDiffResult
    {
        public readonly List<UiContractDiffEntry> Compared = new List<UiContractDiffEntry>();
        public readonly List<string> MissingBaseline = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public int BreakingCount => Compared.Count(entry => entry.Diff != null && entry.Diff.HasBreaking);

        public int ChangedCount => Compared.Count(entry => entry.Diff != null && !entry.Diff.IsEmpty);

        public bool Ok => Errors.Count == 0 && BreakingCount == 0;

        public string Summary =>
            "契约对账：" + Compared.Count + " 页 / 变更 " + ChangedCount + "（破坏性 " + BreakingCount + "）"
            + " / 无基线 " + MissingBaseline.Count + " / 错误 " + Errors.Count;

        public string ToReport()
        {
            var lines = new List<string> { Summary };
            foreach (UiContractDiffEntry entry in Compared)
            {
                if (entry.Diff == null || entry.Diff.IsEmpty) continue;
                lines.Add("  " + entry.PageName + "：变更 " + entry.Diff.Changes.Count
                    + (entry.Diff.HasBreaking ? "（含破坏性）" : string.Empty));
                foreach (UiContractChange change in entry.Diff.Changes)
                {
                    lines.Add("    [" + change.Kind + (change.Breaking ? "/破坏" : string.Empty) + "] "
                        + change.Section + " " + change.Key + " " + change.Detail);
                }
            }
            foreach (string item in MissingBaseline) lines.Add("  无基线 " + item);
            foreach (string item in Warnings) lines.Add("  告警 " + item);
            foreach (string item in Errors) lines.Add("  错误 " + item);
            return string.Join("\n", lines);
        }
    }
}
