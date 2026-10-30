using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Page;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 契约族：契约漂移对账（工具链设计 §6.2「sourceHash 漂移」；§9.3「生成物可重建、可对账；手改生成物被检出」）。
    /// 页面清单（上下文可注入）里 `contractPresent` 的页面逐一对账：按 <see cref="UiContractBatch.Regenerate"/>
    /// （判据单源——身份取清单 forms[0]、维护字段/events 同批量口径）重生成内存态契约与盘上契约逐字节比较——
    /// 不一致＝Error（重跑 `ui.contract`；同时覆盖手改检出）。缺契约/草稿条目归 `ui.page-manifest`，本规则不重复报；
    /// 清单读取失败显式一条 Error。
    /// </summary>
    public sealed class ContractDriftRule : IUiRule
    {
        public string RuleId => UiRuleIds.ContractDrift;
        public string Family => "契约";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            string manifestPath = context.PageManifestPath ?? UiPageManifestPaths.DefaultPath;
            UiPageManifest manifest;
            try
            {
                manifest = UiPageManifestStore.Load(manifestPath);
            }
            catch (Exception ex)
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
                    Asset = manifestPath,
                    Message = "页面清单读取失败：" + ex.Message,
                    FixHint = "重跑 ui.page-manifest 对账（清单是契约漂移判据的输入）",
                });
                return issues;
            }

            foreach (UiPageManifestEntry entry in manifest.Pages ?? new List<UiPageManifestEntry>())
            {
                if (!entry.ContractPresent || string.IsNullOrEmpty(entry.ContractPath)) continue;   // 缺契约归清单规则
                string contractFull = UiContractPaths.FullPath(entry.ContractPath);
                if (!File.Exists(contractFull)) continue;                                           // 同上：文件缺＝清单口径

                UiContractGenerationResult regenerated = UiContractBatch.Regenerate(
                    entry, allowDraft: entry.Forms == null || entry.Forms.Count == 0);
                if (!regenerated.Ok)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = entry.ContractPath,
                        Message = "契约重生成失败（" + entry.Name + "）：" + regenerated.ErrorText,
                        FixHint = "先修复页面/prefab 生成错误，再重跑 ui.contract",
                    });
                    continue;
                }

                string onDisk = File.ReadAllText(contractFull);
                string expected = UiContractJson.Serialize(regenerated.Contract);
                if (!string.Equals(onDisk, expected, StringComparison.Ordinal))
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
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
