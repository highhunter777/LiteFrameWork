using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.EditorTools.UI;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 样式族：颜色 token 对账（工具链设计 §6.2「裸颜色」；§8.1「漂移报告区分引用缺失/解析值不符/裸值」）。
    /// 复用 <see cref="UiStyleTool.ReportDrift"/> 单源（最近 token 匹配 + 命中阈值 1e-4 在工具处单源），
    /// 报告**不命中任何 token** 的颜色＝Warning（手调/漂移由对账暴露，不擅改——工具文档口径）。
    /// 作用域＝上下文资产所在目录（去重），结果按上下文资产面过滤归属。
    /// </summary>
    public sealed class StyleColorDriftRule : IUiRule
    {
        public string RuleId => UiRuleIds.StyleColorDrift;
        public string Family => "样式";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            var assetSet = new HashSet<string>(context.AssetPaths ?? new string[0], System.StringComparer.Ordinal);
            if (assetSet.Count == 0) return issues;

            var scanDirs = context.AssetPaths
                .Select(Path.GetDirectoryName)
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d.Replace('\\', '/'))
                .Distinct(System.StringComparer.Ordinal)
                .ToList();

            foreach ((string asset, string node, Color color) drift in UiStyleTool.ReportDrift(scanDirs))
            {
                string assetPath = (drift.asset ?? string.Empty).Replace('\\', '/');
                if (!assetSet.Contains(assetPath)) continue;
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Warning,
                    Asset = assetPath,
                    Node = drift.node,
                    Message = "颜色不命中任何 token：" + drift.color + "（" + drift.node + "——手调/漂移，对账暴露）",
                    FixHint = "收敛到 UiStyle token（改 token 值重跑样式工具刷新）或维持手调并知悉质量度量",
                });
            }
            return issues;
        }
    }
}
