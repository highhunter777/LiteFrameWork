using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LiteGame.Editor.UITools.Contracts;
using LiteGame.Editor.UITools.Page;
using LiteGame.Editor.UITools.Reference;
using LiteTesting;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 页面 C# "名字面"守卫（契约族）：页逻辑里出现的名字形状字面量必须落在该页契约面 （<c>controls[].bindName</c> ∪ <c>text[]</c> 节点名 ∪ 页面自身名）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class UIPageNameFaceEditModeTests
    {
        /// <summary>名字形状：&lt;前缀段&gt;_&lt;语义段&gt;（允许历史多下划线写法——它就是本用例要抓的形态）。</summary>
        private static readonly Regex NameLiteral =
            new Regex("\"([A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+)\"", RegexOptions.Compiled);

        /// <summary>行注释先摘除：注释里提到的名字不该算引用（避免注释笔误变成用例失败）。</summary>
        private static readonly Regex LineComment = new Regex("//[^\n]*", RegexOptions.Compiled);

        /// <summary>列表行字段名（.Text/.KeyText/.Field 的首参）：行协议字段，不属页面契约面。</summary>
        private static readonly Regex RowFieldLiteral = new Regex(
            "\\b(?:KeyText|Text|Field)\\(\\s*\"([A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+)\"", RegexOptions.Compiled);

        [Test]
        public void 页面CSharp的名字字面量必须落在契约面()
        {
            UIPageManifest manifest = UIPageManifestStore.Load();
            var pageNames = new HashSet<string>(manifest.Pages.Where(p => p != null && !string.IsNullOrEmpty(p.Name))
                .Select(p => p.Name));

            var violations = new List<string>();
            foreach (UIPageManifestEntry entry in manifest.Pages)
            {
                if (entry == null || !entry.ContractPresent || string.IsNullOrEmpty(entry.ContractPath)) continue;

                string source = UIPageLogicScanner.ReadSource(UIPageLogicScanner.ResolveFile(entry.Name));
                if (source == null) continue;                        // 源缺失归引用对账规则告警

                HashSet<string> face = ContractFace(entry);
                foreach (string literal in NameLiterals(source))
                {
                    if (face.Contains(literal) || pageNames.Contains(literal)) continue;
                    violations.Add($"{entry.Name}: \"{literal}\"");
                }
            }

            Assert.IsEmpty(violations,
                "页面 C# 出现契约面外的名字字面量（运行期 GetControl/OnButtonClick 按名寻址会 KeyNotFoundException/"
                + "InvalidOperationException——改名或拼错后必须同步契约/prefab）:\n"
                + string.Join("\n", violations));
        }

        /// <summary>契约面：控件绑定名 ∪ 静态文案节点名（与 ContractReferenceRule 同口径）。</summary>
        private static HashSet<string> ContractFace(UIPageManifestEntry entry)
        {
            UIPageContract contract = UIContractStore.Load(entry.ContractPath);
            var face = new HashSet<string>();
            foreach (UIContractControl control in contract.Controls)
                if (!string.IsNullOrEmpty(control.BindName)) face.Add(control.BindName);
            foreach (UIContractTextBinding text in contract.Text)
            {
                string nodePath = text.NodePath;
                if (string.IsNullOrEmpty(nodePath)) continue;
                int slash = nodePath.LastIndexOf('/');
                face.Add(slash < 0 ? nodePath : nodePath.Substring(slash + 1));
            }
            return face;
        }

        private static IEnumerable<string> NameLiterals(string source)
        {
            string code = LineComment.Replace(source, string.Empty);
            var rowFields = new HashSet<string>(RowFieldLiteral.Matches(code).Select(m => m.Groups[1].Value));
            return NameLiteral.Matches(code).Select(m => m.Groups[1].Value)
                .Where(name => !rowFields.Contains(name)).Distinct();
        }
    }
}
