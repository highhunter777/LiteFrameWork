using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LiteGame.Editor.UITools.Reference;
using LiteGame.Editor.UITools.Validation;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 页面 C# "名字面"守卫：页逻辑里出现的名字形状字面量必须落在该页 **prefab 节点名**面 ∪ 页面自身名。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class UIPageNameFaceEditModeTests
    {
        /// <summary>名字形状：&lt;前缀段&gt;_&lt;语义段&gt;（允许历史多下划线写法——它就是本用例要抓的形态）。</summary>
        private static readonly Regex NameLiteral =
            new Regex("\"([A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+)\"", RegexOptions.Compiled);

        /// <summary>行注释先摘除：注释里提到的名字不该算引用（避免注释笔误变成用例失败）。</summary>
        private static readonly Regex LineComment = new Regex("//[^\n]*", RegexOptions.Compiled);

        /// <summary>列表行字段名（.Text/.KeyText/.Field 的首参）：行协议字段，不属页面节点面。</summary>
        private static readonly Regex RowFieldLiteral = new Regex(
            "\\b(?:KeyText|Text|Field)\\(\\s*\"([A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+)\"", RegexOptions.Compiled);

        [Test]
        public void 页面CSharp的名字字面量必须落在prefab面()
        {
            var pageNames = new HashSet<string>(PagePrefabs().Select(path => Path.GetFileNameWithoutExtension(path)));

            var violations = new List<string>();
            foreach (string source in UIPageLogicScanner.EnumerateSources(UIPageLogicScanner.LogicRoot))
            {
                string pageName = Path.GetFileName(source).Substring(0, Path.GetFileName(source).Length - "Logic.cs".Length);
                string prefabPath = UIValidator.ScreensDir + "/" + pageName + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;                     // 载体缺失归页面逻辑规则告警

                HashSet<string> face = NodeNames(prefab);
                string code = UIPageLogicScanner.ReadSource(source);
                if (code == null) continue;

                foreach (string literal in NameLiterals(code))
                {
                    if (face.Contains(literal) || pageNames.Contains(literal)) continue;
                    violations.Add($"{pageName}: \"{literal}\"");
                }
            }

            Assert.IsEmpty(violations,
                "页面 C# 出现 prefab 面外的名字字面量（运行期 GetControl/OnButtonClick 按名寻址会 KeyNotFoundException/"
                + "InvalidOperationException——改名或拼错后必须同步 prefab）:\n"
                + string.Join("\n", violations));
        }

        /// <summary>prefab 面：全部子节点名（含未激活件——绑定面按层级名寻址）。</summary>
        private static HashSet<string> NodeNames(GameObject prefab)
        {
            var face = new HashSet<string>();
            foreach (Transform node in prefab.GetComponentsInChildren<Transform>(true))
                if (node != null && !string.IsNullOrEmpty(node.name)) face.Add(node.name);
            return face;
        }

        /// <summary>页面 prefab 目录（目录即清单，不再经清单投影）。</summary>
        private static IEnumerable<string> PagePrefabs()
            => AssetDatabase.FindAssets("t:Prefab", new[] { UIValidator.ScreensDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, System.StringComparer.Ordinal);

        private static IEnumerable<string> NameLiterals(string source)
        {
            string code = LineComment.Replace(source, string.Empty);
            var rowFields = new HashSet<string>(RowFieldLiteral.Matches(code).Select(m => m.Groups[1].Value));
            return NameLiteral.Matches(code).Select(m => m.Groups[1].Value)
                .Where(name => !rowFields.Contains(name)).Distinct();
        }
    }
}
