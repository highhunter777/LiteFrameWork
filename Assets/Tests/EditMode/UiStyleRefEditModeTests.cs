using System.Collections.Generic;
using System.IO;
using LiteClient.UI;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 颜色令牌引用的编辑期应用（P2-3 B2 / AC3、AC6-③）：`UiStyleRefApplier` refs 段（按声明写值/幂等/报告/
    /// 未知令牌不猜色）＋迁移段（widget 状态色最近匹配填 token、不命中不填）＋裸值段跳过已声明节点。
    /// 扫描目录注入临时目录（真实面不落盘）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiStyleRefEditModeTests
    {
        private const string TempDir = "Assets/TempUiStyleRefTests";

        [SetUp]
        public void SetUp() => EnsureFolder(TempDir);

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string PrefabPath(string name) => TempDir + "/" + name + ".prefab";

        private static GameObject SavePrefab(string name, System.Action<GameObject> build)
        {
            var root = new GameObject(name, typeof(RectTransform));
            build(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name));
        }

        private static GameObject RefNode(GameObject parent, string name, string token, Color color)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(Image));
            node.transform.SetParent(parent.transform, false);
            node.GetComponent<Image>().color = color;
            if (!string.IsNullOrEmpty(token)) node.AddComponent<UiStyleRef>().ColorToken = token;
            return node;
        }

        private static IReadOnlyList<string> Dirs() => new[] { TempDir };

        // ---- refs 应用段 ----

        [Test]
        public void ApplyRefs_按声明写令牌现值()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            catalog.TryColor("color.brand.primary", out Color primary);
            SavePrefab("refs_apply", root => RefNode(root, "Icon", "color.brand.primary", Color.white));

            UiStyleRefApplier.RefsReport report = UiStyleRefApplier.ApplyRefs(catalog, Dirs());

            Assert.GreaterOrEqual(report.Applied, 1, "应应用 1 处声明");
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("refs_apply"));
            Assert.AreEqual(primary, loaded.transform.Find("Icon").GetComponent<Image>().color, "声明节点＝令牌现值");
        }

        [Test]
        public void ApplyRefs_幂等_重跑零变更()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            SavePrefab("refs_idem", root => RefNode(root, "Icon", "color.bg.deep", Color.white));
            UiStyleRefApplier.ApplyRefs(catalog, Dirs());

            UiStyleRefApplier.RefsReport second = UiStyleRefApplier.ApplyRefs(catalog, Dirs());

            Assert.AreEqual(0, second.Applied, "值已在令牌现值上——重跑零应用");
        }

        [Test]
        public void ApplyRefs_未知令牌_不写值并计数()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            SavePrefab("refs_unknown", root => RefNode(root, "Icon", "color.nope", Color.magenta));

            UiStyleRefApplier.RefsReport report = UiStyleRefApplier.ApplyRefs(catalog, Dirs());

            Assert.GreaterOrEqual(report.UnknownToken, 1, "未知令牌应计数");
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("refs_unknown"));
            Assert.AreEqual(Color.magenta, loaded.transform.Find("Icon").GetComponent<Image>().color, "未知令牌不猜色");
        }

        [Test]
        public void ApplyColorsWith_裸值段跳过已声明节点()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            catalog.TryColor("color.bg.deep", out Color deep);
            catalog.TryColor("color.brand.primary", out Color primary);
            // 节点现值＝deep（裸值段会命中），但声明＝primary——refs 段是权威写者
            SavePrefab("refs_bare_skip", root => RefNode(root, "Icon", "color.brand.primary", deep));

            UiStyleTool.ApplyColorsWith(catalog, Dirs());

            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("refs_bare_skip"));
            Assert.AreEqual(primary, loaded.transform.Find("Icon").GetComponent<Image>().color,
                "已声明节点由 refs 段写令牌现值，不被裸值段改写");
        }

        [Test]
        public void ApplyRefs_跳过IThemeBound节点()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            catalog.TryColor("color.bg.screen", out Color screen);
            SavePrefab("refs_skip_widget", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                node.GetComponent<Image>().color = Color.white;
                node.AddComponent<StateButton>();
                node.AddComponent<UiStyleRef>().ColorToken = "color.bg.screen";   // 双写者：refs 段应跳过
            });

            UiStyleRefApplier.RefsReport report = UiStyleRefApplier.ApplyRefs(catalog, Dirs());

            Assert.GreaterOrEqual(report.Skipped, 1, "IThemeBound 节点应计入跳过");
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("refs_skip_widget"));
            Assert.AreEqual(Color.white, loaded.transform.Find("Btn").GetComponent<Image>().color,
                "refs 段不越权写 widget 自持的 Graphic");
        }

        [Test]
        public void ApplyColorsWith_UiSelectableTheme节点基色仍收敛()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            catalog.TryColor("color.bg.screen", out Color screen);
            Color near = new Color(screen.r + 0.002f, screen.g, screen.b, screen.a);
            SavePrefab("selectable_bare_converge", root =>
            {
                var image = root.AddComponent<Image>();
                image.color = near;
                root.AddComponent<Button>();
                root.AddComponent<UiSelectableTheme>();           // IThemeBound 但只写 tint，不拥有基色
            });

            UiStyleTool.ApplyColorsWith(catalog, Dirs());

            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("selectable_bare_converge"));
            Assert.AreEqual(screen, loaded.GetComponent<Image>().color,
                "UiSelectableTheme 不拥有基色写权——裸值段仍应收敛");
        }

        // ---- widget 状态色迁移段 ----

        [Test]
        public void MigrateWidgetTokens_最近匹配填入()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            SavePrefab("migrate_hit", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                var button = node.AddComponent<StateButton>();
                catalog.TryColor("color.state.interact.normal", out Color normal);
                button.NormalColor = normal;        // 显式赋色（C# 默认值已随 B4 清零）
            });

            UiStyleRefApplier.MigrationReport report = UiStyleRefApplier.MigrateWidgetTokens(catalog, Dirs());

            Assert.GreaterOrEqual(report.Filled, 1, "默认状态色应命中迁移");
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("migrate_hit"));
            var button = loaded.transform.Find("Btn").GetComponent<StateButton>();
            Assert.AreEqual("color.state.interact.normal", button.NormalColorToken, "常态色最近匹配＝interact.normal");
        }

        [Test]
        public void MigrateWidgetTokens_不填selectable专用令牌()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            catalog.TryColor(UiSelectableTheme.PressedToken, out Color pressed);
            SavePrefab("migrate_selectable_filter", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                var button = node.AddComponent<StateButton>();
                button.PressedColor = pressed;      // 仅命中 selectable.pressed——不得填入 widget 字段
            });

            UiStyleRefApplier.MigrateWidgetTokens(catalog, Dirs());

            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("migrate_selectable_filter"));
            Assert.IsTrue(string.IsNullOrEmpty(loaded.transform.Find("Btn").GetComponent<StateButton>().PressedColorToken),
                "selectable 专用令牌不得填进 widget 状态色字段");
        }

        [Test]
        public void MigrateWidgetTokens_不命中不填()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            SavePrefab("migrate_miss", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                var button = node.AddComponent<StateButton>();
                button.NormalColor = new Color(0.83f, 0.17f, 0.63f);   // 品红系，远离全部令牌
            });

            UiStyleRefApplier.MigrateWidgetTokens(catalog, Dirs());

            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("migrate_miss"));
            Assert.IsTrue(string.IsNullOrEmpty(loaded.transform.Find("Btn").GetComponent<StateButton>().NormalColorToken),
                "不命中任何令牌——留空合法，不硬填");
        }
    }
}
