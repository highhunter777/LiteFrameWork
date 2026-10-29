using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteFramework.UI;
using LiteGame.Editor.UiTools.Reference;
using LiteGame.Editor.UiTools.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 文本族：静态文案 key 必须在 <c>tbltext</c>（工具链设计 §6.2「key 缺失」）。
    /// 两路输入：资产面 <c>LTextKeyRef.Key</c>；Lua 面 <c>SetTextKey/SetTextKeyArgs/SetTextKeyPlural</c> 的 key 实参。
    /// 表读取失败显式报一条 Error（不伪造空表——与清单规则同口径）；种子（<c>UiValidationContext.LTextSeed</c>）
    /// 供测试注入合成条目，缺省走 <c>UiLTextTable</c> 真实装载。空 key 归契约生成器（生成期拦），本规则不重复报。
    /// </summary>
    public sealed class TextStaticKeyRule : IUiRule
    {
        private static readonly HashSet<string> KeyApis = new HashSet<string>(
            new[] { "SetTextKey", "SetTextKeyArgs", "SetTextKeyPlural" }, System.StringComparer.Ordinal);

        private readonly string _luaRootDir;

        /// <summary>生产形态：Lua 扫描根＝起页 Lua 目录（UiPageScaffold.LuaDir 单源）。</summary>
        public TextStaticKeyRule() : this(Page.UiPageScaffold.LuaDir) { }

        /// <summary>测试形态：注入替代扫描根（合成 Lua 落临时目录）。</summary>
        public TextStaticKeyRule(string luaRootDir) => _luaRootDir = luaRootDir;

        public string RuleId => UiRuleIds.TextStaticKey;
        public string Family => "文本";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            if (!UiLTextEntries.TryResolve(context, out IReadOnlyList<UiLTextEntry> entries, out string tableError))
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
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
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Node = Binding.UiBindingRename.RelativePath(textRef.transform),
                        Message = "静态文案 key 不在 tbltext：" + key,
                        FixHint = "改用已有 key 或在 #text.xlsx 补行后重跑 gen.bat",
                    });
                }
            }

            foreach (var luaAsset in EnumerateLuaFiles())
            {
                string source = File.ReadAllText(luaAsset.fullPath);
                foreach (UiLuaApiCall call in UiLuaReferenceScanner.ScanApiCalls(source))
                {
                    if (!KeyApis.Contains(call.Api)) continue;
                    string key = call.Args.ElementAtOrDefault(1);
                    if (string.IsNullOrEmpty(key) || known.Contains(key)) continue;
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = luaAsset.relativePath,
                        Message = "Lua 文案 key 不在 tbltext：" + key + "（" + Path.GetFileName(luaAsset.relativePath)
                            + ":" + call.Line + " " + call.Api + "）",
                        FixHint = "改用已有 key 或在 #text.xlsx 补行后重跑 gen.bat",
                    });
                }
            }
            return issues;
        }

        private IEnumerable<(string fullPath, string relativePath)> EnumerateLuaFiles()
        {
            string root = Path.Combine(UiValidator.ProjectRoot, _luaRootDir ?? string.Empty);
            if (!Directory.Exists(root)) yield break;
            foreach (string file in Directory.GetFiles(root, "*.lua"))
                yield return (file, file.Substring(UiValidator.ProjectRoot.Length + 1).Replace('\\', '/'));
        }

        private static bool IsPrefab(string path)
            => !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", System.StringComparison.Ordinal);
    }
}
