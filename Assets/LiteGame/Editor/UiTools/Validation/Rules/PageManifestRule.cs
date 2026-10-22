using System;
using System.Collections.Generic;
using System.IO;
using LiteGame.Editor.UiTools.Page;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 契约族：页面清单与 `tbuiform`/磁盘一致（《UI 编辑器工具链专项设计》§6.2「契约」+ §8.5）。
    /// 清单缺失＝Warning（注册表应入库）；重对账与磁盘指纹不一致（表改了没重跑 / 被手改）＝Error；
    /// 对账告警（孤儿非草稿、prefab 缺失、多行 lua 不一致）逐条转 Warning。
    /// 仅在上下文显式包含 `Page_*` 资产时执行（合成上下文不触发）。
    /// </summary>
    public sealed class PageManifestRule : IUiRule
    {
        public string RuleId => UiRuleIds.PageManifest;
        public string Family => "契约";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            bool hasPage = false;
            foreach (string asset in context.AssetPaths)
                if (PageStructureRule.IsPageAsset(asset)) hasPage = true;
            if (!hasPage) return issues;

            string manifestPath = context.PageManifestPath ?? UiPageManifestPaths.DefaultPath;
            string full = FullPath(manifestPath);
            if (!File.Exists(full))
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Warning,
                    Asset = manifestPath,
                    Message = "页面清单缺失（注册表应入库）",
                    FixHint = "跑页面清单对账生成并提交（§8.5）",
                });
                return issues;
            }

            UiPageManifest disk;
            try
            {
                disk = UiPageManifestStore.Load(manifestPath);
            }
            catch (Exception ex)
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
                    Asset = manifestPath,
                    Message = "页面清单不可读：" + ex.Message,
                    FixHint = "修复/重新生成清单；手改 JSON 不是受支持的更新方式",
                });
                return issues;
            }

            UiPageManifestSyncResult sync = UiPageManifestSync.Sync(null, disk);
            if (!sync.Ok)
            {
                foreach (string error in sync.Errors)
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = manifestPath,
                        Message = "页面清单对账失败：" + error,
                        FixHint = "按错误修正 tbuiform/清单后重跑对账（§8.5）",
                    });
                return issues;
            }

            if (!string.Equals(disk.SourceHash, sync.Manifest.SourceHash, StringComparison.Ordinal))
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
                    Asset = manifestPath,
                    Message = $"页面清单漂移（磁盘 {disk.SourceHash} ≠ 重对账 {sync.Manifest.SourceHash}）",
                    FixHint = "重跑页面清单对账并复核 diff（表行改了没重跑，或清单被手改）",
                });
            }

            foreach (string warning in sync.Warnings ?? new List<string>())
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Warning,
                    Asset = manifestPath,
                    Message = "页面清单告警：" + warning,
                    FixHint = "按告警清理孤儿条目/补 prefab/统一 luaPath（§8.5）",
                });
            return issues;
        }

        private static string FullPath(string assetPath)
            => Path.Combine(UiValidator.ProjectRoot, (assetPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
    }
}
