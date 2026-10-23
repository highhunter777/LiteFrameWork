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
    /// 契约族：引用对账（工具链设计 §6.2「controls 越界/未消费」；§5.2「required＝页面逻辑必须消费，未消费报 Warning」）。
    /// 按页面清单逐页（luaPath 非空且契约在盘）：
    /// **未消费**＝契约 `controls[]` 的 required 项、其 bindName 字面量未出现在页面 Lua 全文＝Warning（消费判定用
    /// 全文字面量匹配，覆盖 `_Begin("Button_X",…)` 式变量中转；动态构造名不识别，如实登记）；
    /// **越界**＝页面 Lua 名字型 API 调用点名不在契约 `controls[]`/`text[]` 面＝Error（名字打错即抓——
    /// self.ui 按名寻址依赖 BindNode，引用未绑名＝运行时取不到）。
    /// Lua 文件解析默认走 <see cref="UiLuaReferenceScanner.TryResolveLuaFile"/>，测试可经构造器注入替代解析。
    /// </summary>
    public sealed class ContractReferenceRule : IUiRule
    {
        private static readonly HashSet<string> NameFirstApis =
            new HashSet<string>(UiRuleThresholds.LuaNameFirstApis, StringComparer.Ordinal);

        private readonly Func<string, string> _luaFileResolver;

        /// <summary>生产形态：默认 luaPath 解析（起页 Lua 目录约定）。</summary>
        public ContractReferenceRule() => _luaFileResolver = UiLuaReferenceScanner.TryResolveLuaFile;

        /// <summary>测试形态：注入替代解析（合成 Lua 落临时路径）。</summary>
        public ContractReferenceRule(Func<string, string> luaFileResolver) => _luaFileResolver = luaFileResolver;

        public string RuleId => UiRuleIds.ContractReference;
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

                string source = ReadLuaSource(entry.LuaPath);
                if (source == null)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Warning,
                        Asset = entry.LuaPath,
                        Message = "页面 Lua 源缺失——引用对账跳过（" + entry.Name + "）",
                        FixHint = "确认 Lua 文件在起页 Lua 目录；未消费/越界无法核对该页",
                    });
                    continue;
                }

                var face = new HashSet<string>(StringComparer.Ordinal);
                foreach (UiContractControl control in contract.Controls ?? new List<UiContractControl>())
                    if (!string.IsNullOrEmpty(control.BindName)) face.Add(control.BindName);
                foreach (UiContractTextBinding text in contract.Text ?? new List<UiContractTextBinding>())
                {
                    string nodeName = NodeNameOf(text.NodePath);
                    if (!string.IsNullOrEmpty(nodeName)) face.Add(nodeName);
                }

                foreach (UiLuaApiCall call in UiLuaReferenceScanner.ScanApiCalls(source))
                {
                    if (!NameFirstApis.Contains(call.Api) || string.IsNullOrEmpty(call.Name)) continue;
                    if (face.Contains(call.Name)) continue;
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = entry.LuaPath,
                        Message = "越界引用：调用点名不在契约面（" + call.Name + " " + call.Api
                            + " @Page " + entry.Name + "——self.ui 按名寻址依赖 BindNode）",
                        FixHint = "修正名字或在节点挂 BindName（重跑 ui.contract）",
                    });
                }

                foreach (UiContractControl control in contract.Controls ?? new List<UiContractControl>())
                {
                    if (!control.Required || string.IsNullOrEmpty(control.BindName)) continue;
                    if (UiLuaReferenceScanner.ContainsLiteral(source, control.BindName)) continue;
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Warning,
                        Asset = entry.LuaPath,
                        Message = "required 控件未被 Lua 消费（" + control.BindName + " @Page " + entry.Name
                            + "）——字面量全文匹配（动态构造名不识别）",
                        FixHint = "接线消费该控件，或它不该标 required（改标记后重跑 ui.contract）",
                    });
                }
            }
            return issues;
        }

        private string ReadLuaSource(string luaPath)
        {
            string file = _luaFileResolver(luaPath);
            return string.IsNullOrEmpty(file) ? null : UiLuaReferenceScanner.ReadSource(file);
        }

        private static string NodeNameOf(string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;
            int last = nodePath.LastIndexOf('/');
            return last < 0 ? nodePath : nodePath.Substring(last + 1);
        }
    }
}
