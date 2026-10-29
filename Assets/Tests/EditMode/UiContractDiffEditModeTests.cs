using System.Linq;
using LiteGame.Editor.UiTools.Contracts;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-1 契约 diff：新增为兼容，删除/改名/类型变化/文案 key 变化为破坏性；
    /// 纯样式变化与 sourceHash-only 变化不得误报为 API 破坏。
    /// </summary>
    public sealed class UiContractDiffEditModeTests
    {
        private static UiPageContract Contract()
        {
            return new UiPageContract
            {
                Page = new UiContractPage
                {
                    FormId = 11,
                    Name = "Page_Test",
                    PrefabPath = "Assets/ShareResource/UI/Screens/Page_Test.prefab",
                    PrefabGuid = "0123456789abcdef0123456789abcdef",
                    LuaPath = "UI.Page_Test",
                    Layer = 0,
                    FullScreen = true,
                    CacheStrategy = "Lru",
                },
                Controls =
                {
                    new UiContractControl { BindName = "Button_Start", NodePath = "Button_Start", Type = "UnityEngine.UI.Button", Kind = "Command", Required = true },
                    new UiContractControl { BindName = "Txt_Title", NodePath = "Txt_Title", Type = "UnityEngine.UI.Text", Kind = "None", Required = false },
                },
                Text =
                {
                    new UiContractTextBinding { NodePath = "Txt_Title", Key = "UI.Test.Title", Args = 0 },
                },
                Styles =
                {
                    new UiContractStyleBinding { NodePath = "Txt_Title", Role = "Title" },
                },
                Templates =
                {
                    new UiContractTemplateUse { NodePath = "List_Rows", Template = "Tmp_Row", BindingHash = "aaa" },
                },
                SourceHash = "sha256:" + new string('a', 64),
            };
        }

        [Test]
        public void 新增控件_向后兼容()
        {
            UiPageContract baseline = Contract();
            UiPageContract current = Contract();
            current.Controls.Add(new UiContractControl
            {
                BindName = "Button_New",
                NodePath = "Button_New",
                Type = "UnityEngine.UI.Button",
                Kind = "Command",
                Required = true,
            });

            var diff = UiContractDiff.Compare(baseline, current);
            Assert.IsFalse(diff.HasBreaking);
            Assert.That(diff.Changes.Any(c => c.Kind == UiContractChangeKind.Added && c.Key == "Button_New"));
        }

        [Test]
        public void 控件改名_判为破坏性()
        {
            UiPageContract baseline = Contract();
            UiPageContract current = Contract();
            UiContractControl c = current.Controls.Single(x => x.BindName == "Button_Start");
            c.BindName = "Button_Go";

            var diff = UiContractDiff.Compare(baseline, current);
            Assert.IsTrue(diff.HasBreaking);
            Assert.That(diff.Changes.Any(x => x.Kind == UiContractChangeKind.Renamed && x.Key == "Button_Go"));
        }

        [Test]
        public void 控件类型与文案key变化_判为破坏性()
        {
            UiPageContract baseline = Contract();
            UiPageContract current = Contract();
            current.Controls.Single(x => x.BindName == "Button_Start").Type = "LiteFramework.UI.UIWidget";
            current.Text.Single().Key = "UI.Test.Changed";

            var diff = UiContractDiff.Compare(baseline, current);
            Assert.IsTrue(diff.HasBreaking);
            Assert.That(diff.Changes.Any(x => x.Kind == UiContractChangeKind.Changed && x.Section == "controls"));
            Assert.That(diff.Changes.Any(x => x.Kind == UiContractChangeKind.Changed && x.Section == "text"));
        }

        [Test]
        public void 样式变化_非API破坏()
        {
            UiPageContract baseline = Contract();
            UiPageContract current = Contract();
            current.Styles.Single().Role = "Body";

            var diff = UiContractDiff.Compare(baseline, current);
            Assert.IsFalse(diff.HasBreaking);
            Assert.That(diff.Changes.Any(x => x.Kind == UiContractChangeKind.Visual && x.Section == "styles"));
        }

        [Test]
        public void sourceHash_only变化_不误报破坏()
        {
            UiPageContract baseline = Contract();
            UiPageContract current = Contract();
            current.SourceHash = "sha256:" + new string('b', 64);

            var diff = UiContractDiff.Compare(baseline, current);
            Assert.IsFalse(diff.HasBreaking);
            Assert.That(diff.Changes.Any(x => x.Kind == UiContractChangeKind.SourceOnly));
        }
    }
}
