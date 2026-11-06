using System.Collections.Generic;
using LiteGame.Editor.UITools.Binding;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 规则：生成物 hash（《UI编辑器工具专项设计》§2.1.5/§4"绑定"族）。
    /// </summary>
    public sealed class UIBindingHashRule : IUIRule
    {
        public string RuleId => "ui.binding-hash";
        public string Family => "命名/绑定";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            if (context == null || context.AssetPaths == null || context.AssetPaths.Count == 0) return issues;

            foreach (var issue in UIBindGenerationHash.Check(
                context.AssetPaths,
                context.BindManifestPath ?? UIBindGenerationHash.DefaultManifestPath))
            {
                var severity = issue.Kind == UIBindHashIssueKind.OrphanEntry ? UISeverity.Warning : UISeverity.Error;
                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = severity,
                    Asset = issue.PrefabPath,
                    Node = "-",
                    Message = $"{issue.Kind}：{issue.Detail}",
                    FixHint = FixHintFor(issue.Kind),
                });
            }
            return issues;
        }

        private static string FixHintFor(UIBindHashIssueKind kind)
        {
            switch (kind)
            {
                case UIBindHashIssueKind.NotRecorded:
                    return "生成绑定代码后经 LiteGame/UI/绑定/生成物 hash 记录（首次生成即记录）";
                case UIBindHashIssueKind.Stale:
                    return "重新生成绑定代码（生成物与源同步）后再记录 hash";
                case UIBindHashIssueKind.GeneratedMissing:
                    return "生成物文件缺失——重新生成绑定代码（勿手改生成物）";
                default:
                    return "prefab 已删——重记录（全部）清理清单残留";
            }
        }
    }
}
