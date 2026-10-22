using System;
using System.Collections.Generic;
using System.IO;
using LiteGame.Editor.UiTools.Widgets;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 契约族：控件清单与磁盘生成物一致（《UI 编辑器工具链专项设计》§6.2「契约」+ §8.4）。
    /// 清单未生成＝Info（提示生成，不阻断）；生成了但与当前模板不一致（`sourceHash` 漂移）＝Error。
    /// 仅在上下文显式包含 `Tmp_*` 资产时执行（合成上下文不触发）。
    /// </summary>
    public sealed class WidgetManifestRule : IUiRule
    {
        public string RuleId => UiRuleIds.WidgetManifest;
        public string Family => "契约";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            var widgets = new List<string>();
            foreach (string asset in context.AssetPaths)
                if (WidgetSpecRule.IsWidgetAsset(asset)) widgets.Add(asset);
            if (widgets.Count == 0) return issues;

            string manifestPath = context.WidgetManifestPath ?? UiWidgetManifestPaths.DefaultPath;
            UiWidgetManifestGenerationResult generated = UiWidgetManifestGenerator.Generate(widgets);
            if (!generated.Ok)
            {
                foreach (string error in generated.Errors)
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = manifestPath,
                        Message = "控件清单生成失败：" + error,
                        FixHint = "按 §8.4 修正模板声明后重跑清单生成",
                    });
                return issues;
            }

            string full = FullPath(manifestPath);
            if (!File.Exists(full))
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Info,
                    Asset = manifestPath,
                    Message = "控件清单未生成（跳过漂移检查）",
                    FixHint = "跑控件清单生成（§8.4）后再校验",
                });
                return issues;
            }

            UiWidgetManifest disk;
            try
            {
                disk = UiWidgetManifestStore.Load(manifestPath);
            }
            catch (Exception ex)
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
                    Asset = manifestPath,
                    Message = "控件清单不可读：" + ex.Message,
                    FixHint = "删除损坏清单后重新生成",
                });
                return issues;
            }

            if (!string.Equals(disk.SourceHash, generated.Manifest.SourceHash, StringComparison.Ordinal))
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
                    Asset = manifestPath,
                    Message = $"控件清单漂移（磁盘 {disk.SourceHash} ≠ 当前模板 {generated.Manifest.SourceHash}）",
                    FixHint = "重新生成控件清单并复核 diff（模板声明改了没重跑，或清单被手改）",
                });
            }
            return issues;
        }

        private static string FullPath(string assetPath)
            => Path.Combine(UiValidator.ProjectRoot, (assetPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
    }
}
