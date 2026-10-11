using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 规则：视觉单一来源（《UI框架总设计》§7／《修改指南》§1.7 视觉面）——运行时不得构建用户可见视觉。
    ///
    /// **源码面规则**：包 <see cref="LiteGame.Editor.VisualConstructionScanner"/>（不改其纯函数判定——
    /// 该扫描器是"真实代码面扫描"的既有载体，另由 <c>VisualSingleSourceEditModeTests</c> 直接消费；
    /// 本条规则只是把它的产出转成统一报告项，供 UI 校验核心汇总）。
    /// </summary>
    public sealed class RuntimeVisualRule : IUiRule
    {
        public string RuleId => "ui.runtime-visual";
        public string Family => "渲染/适配";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            if (context == null || string.IsNullOrEmpty(context.ProjectRoot)) return issues;

            foreach (var v in VisualConstructionScanner.ScanDirectory(context.ProjectRoot, context.SourceDirs.ToArray()))
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
                    Asset = v.File,
                    Node = null,
                    Message = $"第 {v.Line} 行：{v.Reason}（{v.Snippet}）",
                    FixHint = "改为引用 Assets/ShareResource/UI/{Screens,Widgets} 的模板，不 new GameObject/AddComponent 造视觉",
                });
            }
            return issues;
        }
    }
}
