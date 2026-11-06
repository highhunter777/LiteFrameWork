using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 规则：视觉单一来源（《UI框架总设计》§7／《修改指南》§1.7 视觉面）——运行时不得构建用户可见视觉。
    /// </summary>
    public sealed class RuntimeVisualRule : IUIRule
    {
        public string RuleId => "ui.runtime-visual";
        public string Family => "渲染/适配";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            if (context == null || string.IsNullOrEmpty(context.ProjectRoot)) return issues;

            foreach (var v in VisualConstructionScanner.ScanDirectory(context.ProjectRoot, context.SourceDirs.ToArray()))
            {
                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = UISeverity.Error,
                    Asset = v.File,
                    Node = null,
                    Message = $"第 {v.Line} 行：{v.Reason}（{v.Snippet}）",
                    FixHint = "改为引用 Assets/LiteGame/Modules/UI/Content/{Screens,Widgets} 或 Assets/LiteGame/Modules/Battle/Content/Hud 的模板，不 new GameObject/AddComponent 造视觉",
                });
            }
            return issues;
        }
    }
}
