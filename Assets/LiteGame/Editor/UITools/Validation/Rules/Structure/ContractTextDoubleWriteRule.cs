using System;
using System.Collections.Generic;
using LiteGame.Editor.UITools.Contracts;
using LiteGame.Editor.UITools.Page;
using LiteGame.Editor.UITools.Reference;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 契约族：文本双写（工具链设计 §6.2「文本双写」；《UI 逻辑 API 参考》「有静态 key 的节点页面逻辑不得再 SetTextKey/SetText」）。
    /// </summary>
    public sealed class ContractTextDoubleWriteRule : IUIRule
    {
        private static readonly HashSet<string> TextWriteApis = new HashSet<string>(
            UIRuleThresholds.PageTextWriteApis, StringComparer.Ordinal);

        private readonly Func<UIPageManifestEntry, string> _sourceResolver;

        /// <summary>生产形态：解析单源＝UIPageLogicScanner（C# 页源）。</summary>
        public ContractTextDoubleWriteRule()
            => _sourceResolver = entry => UIPageLogicScanner.ResolveFile(entry.Name);

        /// <summary>测试形态：注入替代解析（合成页源落临时路径；按扩展名分派语法）。</summary>
        public ContractTextDoubleWriteRule(Func<UIPageManifestEntry, string> sourceResolver) => _sourceResolver = sourceResolver;

        public string RuleId => UIRuleIds.ContractTextDoubleWrite;
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
                    FixHint = "重跑 ui.page-manifest 对账",
                });
                return issues;
            }

            foreach (UIPageManifestEntry entry in manifest.Pages ?? new List<UIPageManifestEntry>())
            {
                if (!entry.ContractPresent || string.IsNullOrEmpty(entry.ContractPath)) continue;

                UIPageContract contract;
                try
                {
                    contract = UIContractStore.Load(entry.ContractPath);
                }
                catch (Exception ex)
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = entry.ContractPath,
                        Message = "契约读取失败（" + entry.Name + "）：" + ex.Message,
                        FixHint = "重跑 ui.contract 刷新契约",
                    });
                    continue;
                }

                var staticNodeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (UIContractTextBinding text in contract.Text ?? new List<UIContractTextBinding>())
                {
                    string nodeName = NodeNameOf(text.NodePath);
                    if (!string.IsNullOrEmpty(nodeName)) staticNodeNames.Add(nodeName);
                }
                if (staticNodeNames.Count == 0) continue;

                string file = _sourceResolver(entry);
                string source = UIPageLogicScanner.ReadSource(file);
                if (source == null) continue;       // 源缺失归引用对账规则告警，此处不重复

                foreach (UIPageApiCall call in UIPageLogicScanner.ScanApiCalls(source))
                {
                    if (!TextWriteApis.Contains(call.Api) || string.IsNullOrEmpty(call.Name)) continue;
                    if (!staticNodeNames.Contains(call.Name)) continue;
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = file,
                        Message = "文本双写：静态文案节点被页面逻辑再写（" + call.Name + " " + call.Api
                            + " @Page " + entry.Name + "——静态键与动态写互相覆盖）",
                        FixHint = "该节点保留 LTextKeyRef（静态），或摘除静态键全走动态写（二选一）",
                    });
                }
            }
            return issues;
        }

        private static string NodeNameOf(string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;
            int last = nodePath.LastIndexOf('/');
            return last < 0 ? nodePath : nodePath.Substring(last + 1);
        }
    }
}
