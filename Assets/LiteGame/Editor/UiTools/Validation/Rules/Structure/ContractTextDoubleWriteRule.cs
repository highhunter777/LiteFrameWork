using System;
using System.Collections.Generic;
using System.Linq;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Reference;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 契约族：文本双写（工具链设计 §6.2「文本双写」；《UI 逻辑 API 参考》「有静态 key 的节点 Lua 不得再 SetTextKey/SetText」）。
    /// 契约 `text[]`（`LTextKeyRef` 静态文案节点）的节点名若被页面 Lua 的
    /// <see cref="UiRuleThresholds.LuaTextWriteApis"/> 调用点名命中＝Error（双写会互相覆盖且语言切换语义分裂）。
    /// Lua 文件解析默认走 <see cref="UiLuaReferenceScanner.TryResolveLuaFile"/>，测试可经构造器注入替代解析。
    /// </summary>
    public sealed class ContractTextDoubleWriteRule : IUiRule
    {
        private static readonly HashSet<string> TextWriteApis =
            new HashSet<string>(UiRuleThresholds.LuaTextWriteApis, StringComparer.Ordinal);

        private readonly Func<string, string> _luaFileResolver;

        /// <summary>生产形态：默认 luaPath 解析（起页 Lua 目录约定）。</summary>
        public ContractTextDoubleWriteRule() => _luaFileResolver = UiLuaReferenceScanner.TryResolveLuaFile;

        /// <summary>测试形态：注入替代解析（合成 Lua 落临时路径）。</summary>
        public ContractTextDoubleWriteRule(Func<string, string> luaFileResolver) => _luaFileResolver = luaFileResolver;

        public string RuleId => UiRuleIds.ContractTextDoubleWrite;
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
                    FixHint = "重跑 ui.page-manifest 对账",
                });
                return issues;
            }

            foreach (UiPageManifestEntry entry in manifest.Pages ?? new List<UiPageManifestEntry>())
            {
                if (!entry.ContractPresent || string.IsNullOrEmpty(entry.ContractPath)
                    || string.IsNullOrEmpty(entry.LuaPath)) continue;

                UiPageContract contract;
                try
                {
                    contract = UiContractStore.Load(entry.ContractPath);
                }
                catch (Exception ex)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = entry.ContractPath,
                        Message = "契约读取失败（" + entry.Name + "）：" + ex.Message,
                        FixHint = "重跑 ui.contract 刷新契约",
                    });
                    continue;
                }

                var staticNodeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (UiContractTextBinding text in contract.Text ?? new List<UiContractTextBinding>())
                {
                    string nodeName = NodeNameOf(text.NodePath);
                    if (!string.IsNullOrEmpty(nodeName)) staticNodeNames.Add(nodeName);
                }
                if (staticNodeNames.Count == 0) continue;

                string file = _luaFileResolver(entry.LuaPath);
                string source = string.IsNullOrEmpty(file) ? null : UiLuaReferenceScanner.ReadSource(file);
                if (source == null) continue;       // 源缺失归引用对账规则告警，此处不重复

                foreach (UiLuaApiCall call in UiLuaReferenceScanner.ScanApiCalls(source))
                {
                    if (!TextWriteApis.Contains(call.Api) || string.IsNullOrEmpty(call.Name)) continue;
                    if (!staticNodeNames.Contains(call.Name)) continue;
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = entry.LuaPath,
                        Message = "文本双写：静态文案节点被 Lua 再写（" + call.Name + " " + call.Api
                            + " @Page " + entry.Name + "——静态键与动态写互相覆盖）",
                        FixHint = "该节点保留 LTextKeyRef（静态），或摘除静态键全走 Lua（二选一）",
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
