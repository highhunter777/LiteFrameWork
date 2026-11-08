using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteFramework.UI;
using LiteGame.Editor.UITools.Reference;
using LiteGame.Editor.UITools.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 文本族：静态文案 key 必须在 <c>tbltext</c>（工具链设计 §6.2「key 缺失」）。
    /// </summary>
    public sealed class TextStaticKeyRule : IUIRule
    {
        private static readonly HashSet<string> KeyApis = new HashSet<string>(
            new[] { "SetTextKey", "SetTextKeyArgs", "SetTextKeyPlural" }, System.StringComparer.Ordinal);

        private readonly string _luaRootDir;
        private readonly string _logicRootDir;

        /// <summary>生产形态：Lua 根＝起页 Lua 目录（UIPageScaffold.LuaDir）；C# 根＝页面逻辑件根。</summary>
        public TextStaticKeyRule() : this(Page.UIPageScaffold.LuaDir, UIPageLogicScanner.LogicRoot) { }

        /// <summary>测试形态：注入替代扫描根（合成页源落临时目录）。</summary>
        public TextStaticKeyRule(string luaRootDir, string logicRootDir = null)
        {
            _luaRootDir = luaRootDir;
            _logicRootDir = logicRootDir ?? UIPageLogicScanner.LogicRoot;
        }

        public string RuleId => UIRuleIds.TextStaticKey;
        public string Family => "文本";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            if (!UILTextEntries.TryResolve(context, out IReadOnlyList<UILTextEntry> entries, out string tableError))
            {
                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = UISeverity.Error,
                    Asset = "tbltext",
                    Message = "文案表读取失败：" + tableError,
                    FixHint = "检查 Luban 生成物与表数据（重跑 gen.bat）；表读失败时不得伪造空表",
                });
                return issues;
            }
            var known = new HashSet<string>(entries.Select(e => e.Key), System.StringComparer.Ordinal);

            foreach (string asset in context.AssetPaths)
            {
                if (!IsPrefab(asset)) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null) continue;
                foreach (LTextKeyRef textRef in prefab.GetComponentsInChildren<LTextKeyRef>(true))
                {
                    string key = (textRef.Key ?? string.Empty).Trim();
                    if (key.Length == 0 || known.Contains(key)) continue;
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = asset,
                        Node = Binding.UIBindingRename.RelativePath(textRef.transform),
                        Message = "静态文案 key 不在 tbltext：" + key,
                        FixHint = "改用已有 key 或在 #text.xlsx 补行后重跑 gen.bat",
                    });
                }
            }

            foreach (string relative in UIPageLogicScanner.EnumerateSources(_luaRootDir, _logicRootDir))
            {
                string source = UIPageLogicScanner.ReadSource(relative);
                if (source == null) continue;
                foreach (UIPageApiCall call in UIPageLogicScanner.ScanApiCalls(relative, source))
                {
                    if (!KeyApis.Contains(call.Api)) continue;
                    string key = call.Args.ElementAtOrDefault(1);
                    if (string.IsNullOrEmpty(key) || known.Contains(key)) continue;
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = relative,
                        Message = "页面文案 key 不在 tbltext：" + key + "（" + Path.GetFileName(relative)
                            + ":" + call.Line + " " + call.Api + "）",
                        FixHint = "改用已有 key 或在 #text.xlsx 补行后重跑 gen.bat",
                    });
                }
            }
            return issues;
        }

        private static bool IsPrefab(string path)
            => !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", System.StringComparison.Ordinal);
    }
}
