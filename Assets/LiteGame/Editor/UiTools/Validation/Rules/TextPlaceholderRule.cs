using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Reference;
using LiteGame.Editor.UiTools.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 文本族：占位符使用相符（工具链设计 §6.2「占位符不符」）。
    /// 三路判定：① <c>LTextKeyRef</c> 挂到带占位符（<c>{0}</c> 形态）的条目＝Error（静态键无参——契约 <c>args</c> 固定 0）；
    /// ② Lua <c>SetTextKey</c> 用带占位符条目＝Error（应改 <c>SetTextKeyArgs</c>）；
    /// ③ <c>SetTextKeyArgs</c> 实参数（名字/key 之后的参数）与条目占位符数不符＝Warning。
    /// 占位符数取源语言列（Zh）——源语言是源（En 列完整性归语言列规则）。种子注入同 <see cref="TextStaticKeyRule"/>。
    /// </summary>
    public sealed class TextPlaceholderRule : IUiRule
    {
        private static readonly Regex PlaceholderPattern = new Regex(@"\{[0-3]\}", RegexOptions.CultureInvariant);

        private readonly string _luaRootDir;

        /// <summary>生产形态：Lua 扫描根＝起页 Lua 目录（UiPageScaffold.LuaDir 单源）。</summary>
        public TextPlaceholderRule() : this(Page.UiPageScaffold.LuaDir) { }

        /// <summary>测试形态：注入替代扫描根（合成 Lua 落临时目录）。</summary>
        public TextPlaceholderRule(string luaRootDir) => _luaRootDir = luaRootDir;

        public string RuleId => UiRuleIds.TextPlaceholder;
        public string Family => "文本";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            if (!UiLTextEntries.TryResolve(context, out var entries, out string tableError))
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
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Node = Binding.UiBindingRename.RelativePath(textRef.transform),
                        Message = "静态键挂带参条目：" + key + "（" + count + " 个占位符，LTextKeyRef 无参）",
                        FixHint = "带参文案走 Lua SetTextKeyArgs，或拆出无参条目",
                    });
                }
            }

            string luaRoot = Path.Combine(UiValidator.ProjectRoot, _luaRootDir ?? string.Empty);
            if (!Directory.Exists(luaRoot)) return issues;
            foreach (string file in Directory.GetFiles(luaRoot, "*.lua"))
            {
                string relative = file.Substring(UiValidator.ProjectRoot.Length + 1).Replace('\\', '/');
                foreach (UiLuaApiCall call in UiLuaReferenceScanner.ScanApiCalls(File.ReadAllText(file)))
                {
                    string key = call.Args.ElementAtOrDefault(1);
                    if (string.IsNullOrEmpty(key) || !placeholders.TryGetValue(key, out int count)) continue;
                    string line = relative + ":" + call.Line;

                    if (call.Api == "SetTextKey" && count > 0)
                    {
                        issues.Add(new UiIssue
                        {
                            RuleId = RuleId,
                            Severity = UiSeverity.Error,
                            Asset = relative,
                            Message = "SetTextKey 用带参条目：" + key + "（" + count + " 个占位符） @" + line,
                            FixHint = "改用 SetTextKeyArgs 传参",
                        });
                    }
                    else if (call.Api == "SetTextKeyArgs")
                    {
                        int supplied = System.Math.Max(0, call.ArgCount - 2);      // 名字/key 之后的参数
                        if (supplied != count)
                        {
                            issues.Add(new UiIssue
                            {
                                RuleId = RuleId,
                                Severity = UiSeverity.Warning,
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

        private UiIssue TableError(string error) => new UiIssue
        {
            RuleId = RuleId,
            Severity = UiSeverity.Error,
            Asset = "tbltext",
            Message = "文案表读取失败：" + error,
            FixHint = "检查 Luban 生成物与表数据（重跑 gen.bat）",
        };
    }
}
