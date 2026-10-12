using System.Collections.Generic;
using LiteGame.Editor.UiTools.Binding;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 规则：生成物 hash（《UI编辑器工具专项设计》§2.1.5/§4"绑定"族）。
    ///
    /// 资产面规则：对上下文资产集做 <see cref="UiBindGenerationHash.Check"/> 对账——
    /// 带 BindRoot 的 prefab 必须有记录、hash 须与当前绑定面一致、记录的生成物文件须在盘。
    /// **记录是作者动作**（菜单/窗口），本规则只判"改了源却没重新生成/记录"的漂移，
    /// 不代记录——漂移必须显红（Error），不冒充通过。
    /// </summary>
    public sealed class UiBindingHashRule : IUiRule
    {
        public string RuleId => "ui.binding-hash";
        public string Family => "绑定";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            if (context == null || context.AssetPaths == null || context.AssetPaths.Count == 0) return issues;

            foreach (var issue in UiBindGenerationHash.Check(
                context.AssetPaths,
                context.BindManifestPath ?? UiBindGenerationHash.DefaultManifestPath))
            {
                var severity = issue.Kind == UiBindHashIssueKind.OrphanEntry ? UiSeverity.Warning : UiSeverity.Error;
                issues.Add(new UiIssue
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

        private static string FixHintFor(UiBindHashIssueKind kind)
        {
            switch (kind)
            {
                case UiBindHashIssueKind.NotRecorded:
                    return "生成绑定代码后经 LiteGame/UI/绑定/生成物 hash 记录（首次生成即记录）";
                case UiBindHashIssueKind.Stale:
                    return "重新生成绑定代码（生成物与源同步）后再记录 hash";
                case UiBindHashIssueKind.GeneratedMissing:
                    return "生成物文件缺失——重新生成绑定代码（勿手改生成物）";
                default:
                    return "prefab 已删——重记录（全部）清理清单残留";
            }
        }
    }
}
