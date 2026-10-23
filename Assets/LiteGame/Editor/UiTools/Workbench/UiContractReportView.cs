using System;
using System.Collections.Generic;
using LiteGame.Editor.UiTools.Contracts;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// 契约透视的报告投影：把 <see cref="UiContractBatch"/> 的生成/对账结果转成窗口与状态行消费的视图。
    /// **不复制契约生成逻辑、不重算 diff**——只做计数与摘要映射。
    /// </summary>
    public sealed class UiContractReportView
    {
        public string Title;
        public string Summary;
        public int Written;
        public int Skipped;
        public int Breaking;
        public int Changed;
        public bool Ok;
        public string OutputDirectory;
        public List<string> Lines = new List<string>();

        /// <summary>批量生成结果 → 视图（<paramref name="outputDirectory"/> 为本次写盘目录，供状态行显示）。</summary>
        public static UiContractReportView FromGeneration(UiContractBatchResult result, string outputDirectory)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return new UiContractReportView
            {
                Title = "契约生成",
                Summary = result.Summary,
                Written = result.Written.Count,
                Skipped = result.Skipped.Count,
                Ok = result.Ok,
                OutputDirectory = outputDirectory,
                Lines = Split(result.ToReport()),
            };
        }

        /// <summary>批量对账结果 → 视图（破坏性计数即失败口径）。</summary>
        public static UiContractReportView FromDiff(UiContractBatchDiffResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return new UiContractReportView
            {
                Title = "契约对账",
                Summary = result.Summary,
                Changed = result.ChangedCount,
                Breaking = result.BreakingCount,
                Ok = result.Ok,
                Lines = Split(result.ToReport()),
            };
        }

        private static List<string> Split(string report)
            => new List<string>((report ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
