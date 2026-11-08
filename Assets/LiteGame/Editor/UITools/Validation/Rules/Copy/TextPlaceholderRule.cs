using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LiteFramework.UI;
using LiteGame.Editor.UITools.Reference;
using LiteGame.Editor.UITools.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 文本族：占位符使用相符（工具链设计 §6.2「占位符不符」）。
    /// </summary>
    public sealed class TextPlaceholderRule : IUIRule
    {
        private static readonly Regex PlaceholderPattern = new Regex(@"\{[0-3]\}", RegexOptions.CultureInvariant);

        private readonly string _luaRootDir;
        private readonly string _logicRootDir;

        /// <summary>生产形态：Lua 根＝起页 Lua 目录（UIPageScaffold.LuaDir）；C# 根＝页面逻辑件根。</summary>
        public TextPlaceholderRule() : this(Page.UIPageScaffold.LuaDir, UIPageLogicScanner.LogicRoot) { }

        /// <summary>测试形态：注入替代扫描根（合成页源落临时目录）。</summary>
        public TextPlaceholderRule(string luaRootDir, string logicRootDir = null)
        {
            _luaRootDir = luaRootDir;
            _logicRootDir = logicRootDir ?? UIPageLogicScanner.LogicRoot;
        }

        public string RuleId => UIRuleIds.TextPlaceholder;
        public string Family => "文本";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            if (!UILTextEntries.TryResolve(context, out var entries, out string tableError))
            {
                issues.Add(TableError(tableError));
                return issues;
            }
            var placeholders = entries.ToDictionary(e => e.Key, e => CountPlaceholders(e.Zh), System.StringComparer.Ordinal);

            foreach (string asset in context.AssetPaths)
            {
                if (!asset.EndsWith(".prefab", System.StringComparison.Ordinal)) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null) continue;
                foreach (LTextKeyRef textRef in prefab.GetComponentsInChildren<LTextKeyRef>(true))
                {
                    string key = (textRef.Key ?? string.Empty).Trim();
                    if (key.Length == 0 || !placeholders.TryGetValue(key, out int count) || count == 0) continue;
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = asset,
                        Node = Binding.UIBindingRename.RelativePath(textRef.transform),
                        Message = "静态键挂带参条目：" + key + "（" + count + " 个占位符，LTextKeyRef 无参）",
                        FixHint = "带参文案走 Lua SetTextKeyArgs，或拆出无参条目",
                    });
                }
            }

            foreach (string relative in UIPageLogicScanner.EnumerateSources(_luaRootDir, _logicRootDir))
            {
                string source = UIPageLogicScanner.ReadSource(relative);
                if (source == null) continue;
                bool isCSharp = UIPageLogicScanner.IsCSharp(relative);
                foreach (UIPageApiCall call in UIPageLogicScanner.ScanApiCalls(relative, source))
                {
                    string key = call.Args.ElementAtOrDefault(1);
                    if (string.IsNullOrEmpty(key) || !placeholders.TryGetValue(key, out int count)) continue;
                    string line = relative + ":" + call.Line;

                    // Lua 面：无参 API 挂带参条目＝Error，传参走 SetTextKeyArgs；
                    // C# 面：SetTextKey(name, key, args…) 本身就带参——只核对实参个数。
                    if (!isCSharp && call.Api == "SetTextKey" && count > 0)
                    {
                        issues.Add(new UIIssue
                        {
                            RuleId = RuleId,
                            Severity = UISeverity.Error,
                            Asset = relative,
                            Message = "SetTextKey 用带参条目：" + key + "（" + count + " 个占位符） @" + line,
                            FixHint = "改用 SetTextKeyArgs 传参",
                        });
                    }
                    else if (isCSharp && call.Api == "SetTextKey")
                    {
                        int supplied = System.Math.Max(0, call.ArgCount - 2);
                        if (supplied != count)
                        {
                            issues.Add(new UIIssue
                            {
                                RuleId = RuleId,
                                Severity = UISeverity.Warning,
                                Asset = relative,
                                Message = "SetTextKey 参数数不符：" + key + "（实参 " + supplied
                                    + " / 条目 " + count + "） @" + line,
                                FixHint = "对齐条目占位符数（arg0..arg3，上限 4）",
                            });
                        }
                    }
                    else if (!isCSharp && call.Api == "SetTextKeyArgs")
                    {
                        int supplied = System.Math.Max(0, call.ArgCount - 2);      // 名字/key 之后的参数
                        if (supplied != count)
                        {
                            issues.Add(new UIIssue
                            {
                                RuleId = RuleId,
                                Severity = UISeverity.Warning,
                                Asset = relative,
                                Message = "SetTextKeyArgs 参数数不符：" + key + "（实参 " + supplied
                                    + " / 条目 " + count + "） @" + line,
                                FixHint = "对齐条目占位符数（arg0..arg3，上限 4）",
                            });
                        }
                    }
                }
            }
            return issues;
        }

        private static int CountPlaceholders(string sourceLanguage)
            => string.IsNullOrEmpty(sourceLanguage) ? 0 : PlaceholderPattern.Matches(sourceLanguage).Count;

        private UIIssue TableError(string error) => new UIIssue
        {
            RuleId = RuleId,
            Severity = UISeverity.Error,
            Asset = "tbltext",
            Message = "文案表读取失败：" + error,
            FixHint = "检查 Luban 生成物与表数据（重跑 gen.bat）",
        };
    }
}
