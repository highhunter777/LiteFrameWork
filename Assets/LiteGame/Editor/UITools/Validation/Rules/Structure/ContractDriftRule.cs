using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UITools.Contracts;
using LiteGame.Editor.UITools.Page;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 契约族：契约漂移对账（工具链设计 §6.2「sourceHash 漂移」；§9.3「生成物可重建、可对账；手改生成物被检出」）。
    /// </summary>
    public sealed class ContractDriftRule : IUIRule
    {
        public string RuleId => UIRuleIds.ContractDrift;
        public string Family => "契约";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            string manifestPath = context.PageManifestPath ?? UIPageManifestPaths.DefaultPath;
            UIPageManifest manifest;
            try
            {
                manifest = UIPageManifestStore.Load(manifestPath);
            }
            catch (Exception ex)
            {
                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = UISeverity.Error,
                    Asset = manifestPath,
                    Message = "页面清单读取失败：" + ex.Message,
                    FixHint = "重跑 ui.page-manifest 对账（清单是契约漂移判据的输入）",
                });
                return issues;
            }

            foreach (UIPageManifestEntry entry in manifest.Pages ?? new List<UIPageManifestEntry>())
            {
                if (!entry.ContractPresent || string.IsNullOrEmpty(entry.ContractPath)) continue;   // 缺契约归清单规则
                string contractFull = UIContractPaths.FullPath(entry.ContractPath);
                if (!File.Exists(contractFull)) continue;                                           // 同上：文件缺＝清单口径

                UIContractGenerationResult regenerated = UIContractBatch.Regenerate(
                    entry, allowDraft: entry.Forms == null || entry.Forms.Count == 0);
                if (!regenerated.Ok)
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = entry.ContractPath,
                        Message = "契约重生成失败（" + entry.Name + "）：" + regenerated.ErrorText,
                        FixHint = "先修复页面/prefab 生成错误，再重跑 ui.contract",
                    });
                    continue;
                }

                string onDisk = File.ReadAllText(contractFull);
                string expected = UIContractJson.Serialize(regenerated.Contract);
                if (!string.Equals(onDisk, expected, StringComparison.Ordinal))
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = entry.ContractPath,
                        Message = "契约与 prefab 重生成不一致（" + entry.Name + "，含手改检出）",
                        FixHint = "重跑 ui.contract 刷新契约（生成物禁手改）",
                    });
                }
            }
            return issues;
        }
    }
}
