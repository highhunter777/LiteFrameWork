using System.Linq;
using LiteGame.EditorTools.UI;
using LiteFramework.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 样式工具步 4（《UI编辑器工具专项设计》§3）的 L2 覆盖：文字档基线表（§3.1 数字）、
    /// 按标记精确应用（TMP/UGUI 双组件映射 + 幂等）、对账三类（未标记/属性漂移/引用失效）、
    /// 扫描根注入缝。合成临时 prefab 喂工具——真实资产面的对账基线由菜单首跑登记（手测）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiTextStyleEditModeTests
    {
        private const string TempDir = "Assets/TempUiStyleTests";
        private static readonly string[] TempDirs = { TempDir };

        [SetUp]
        public void EnsureTempDir()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "TempUiStyleTests");
        }

        [TearDown]
        public void CleanupTempDir()
        {
            if (AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.DeleteAsset(TempDir);
        }

        // ---- 基线表（§3.1） ----

        [Test]
        public void 基线_五角色齐全且值与设计表一致()
        {
            Assert.AreEqual(5, UiTextStyle.All.Length, "§3.1 初始五角色");
            Assert.IsTrue(UiTextStyle.All.SequenceEqual(UiTextStyle.All.OrderBy(d => (int)d.Role)), "全集顺序稳定（供遍历单源）");

            var title = UiTextStyle.All.First(d => d.Role == UiTextRole.Title);
            Assert.AreEqual(24f, title.FontSize);
            Assert.IsFalse(title.WordWrap, "标题不换行");
            Assert.IsTrue(title.Truncate, "标题初始 Truncate");
            Assert.IsFalse(title.Bold);

            var body = UiTextStyle.All.First(d => d.Role == UiTextRole.Body);
            Assert.AreEqual(20f, body.FontSize);
            Assert.IsTrue(body.WordWrap);
            Assert.IsFalse(body.Truncate, "正文初始 Overflow（扩展由容器承担）");

            var caption = UiTextStyle.All.First(d => d.Role == UiTextRole.Caption);
            Assert.AreEqual(14f, caption.FontSize);
            Assert.AreEqual(TextAlignmentOptions.MidlineLeft, caption.TmpAlignment, "说明文字左对齐");
            Assert.AreEqual(TextAnchor.MiddleLeft, caption.LegacyAlignment);

            var labelSingle = UiTextStyle.All.First(d => d.Role == UiTextRole.LabelSingle);
            Assert.IsFalse(labelSingle.WordWrap, "单行标签不换行");
            Assert.IsTrue(labelSingle.Truncate, "单行标签 Truncate");

            var emphasis = UiTextStyle.All.First(d => d.Role == UiTextRole.Emphasis);
            Assert.IsTrue(emphasis.Bold, "强调文本加粗（五角色唯一加粗档）");
            Assert.IsTrue(UiTextStyle.All.Count(d => d.Bold) == 1);

            Assert.IsFalse(UiTextStyle.TryGet((UiTextRole)99, out _), "未知角色不猜");
        }

        // ---- 应用（精确写入 + 幂等） ----

        [Test]
        public void 应用_TMP全属性写入且幂等()
        {
            string path = MakeTempPrefab("Page_TmpApply", node =>
            {
                var tmp = node.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 99f;                                  // 漂移态：全属性偏离 Title 档
                tmp.alignment = TextAlignmentOptions.TopLeft;
                tmp.enableWordWrapping = true;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                tmp.fontStyle = FontStyles.Italic;
                node.AddComponent<UiTextStyleRef>().Role = UiTextRole.Title;
            });

            var (prefabs, writes) = UiTextStyleTool.ApplyTextStyles(TempDirs);
            Assert.AreEqual(1, prefabs, "一个 prefab 被改");
            Assert.AreEqual(4, writes, "字号/对齐/换行/溢出四项写入（Italic 无 Bold 位——字重档合规零写）");

            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var tmp2 = reloaded.GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual(24f, tmp2.fontSize, "字号精确应用");
            Assert.AreEqual(TextAlignmentOptions.Center, tmp2.alignment);
            Assert.IsFalse(tmp2.enableWordWrapping);
            Assert.AreEqual(TextOverflowModes.Truncate, tmp2.overflowMode);
            Assert.AreEqual(FontStyles.Italic, tmp2.fontStyle, "非加粗档不碰既有 Italic 位——只清/置 Bold 位");

            var (prefabs2, writes2) = UiTextStyleTool.ApplyTextStyles(TempDirs);
            Assert.AreEqual(0, prefabs2, "幂等：重跑零改动");
            Assert.AreEqual(0, writes2);
        }

        [Test]
        public void 应用_UGUI映射与加粗()
        {
            MakeTempPrefab("Page_LegacyApply", node =>
            {
                var legacy = node.AddComponent<Text>();
                legacy.fontSize = 9;                                  // UGUI Text.fontSize 为 int
                legacy.alignment = TextAnchor.UpperRight;
                legacy.horizontalOverflow = HorizontalWrapMode.Overflow;
                legacy.fontStyle = FontStyle.Normal;
                node.AddComponent<UiTextStyleRef>().Role = UiTextRole.Caption;
            });
            MakeTempPrefab("Page_LegacyBold", node =>
            {
                var legacy = node.AddComponent<Text>();
                node.AddComponent<UiTextStyleRef>().Role = UiTextRole.Emphasis;
            });

            UiTextStyleTool.ApplyTextStyles(TempDirs);

            var caption = LoadTemp("Page_LegacyApply").GetComponentInChildren<Text>(true);
            Assert.AreEqual(14, caption.fontSize, "字号精确应用（UGUI int 口径）");
            Assert.AreEqual(TextAnchor.MiddleLeft, caption.alignment);
            Assert.AreEqual(HorizontalWrapMode.Wrap, caption.horizontalOverflow);
            Assert.AreEqual(FontStyle.Normal, caption.fontStyle);

            var emphasis = LoadTemp("Page_LegacyBold").GetComponentInChildren<Text>(true);
            Assert.AreEqual(FontStyle.Bold, emphasis.fontStyle, "Emphasis 加粗写入 UGUI fontStyle");
            Assert.AreEqual(20, emphasis.fontSize);
        }

        [Test]
        public void 应用_加粗位只动Bold不动斜体()
        {
            MakeTempPrefab("Page_TmpBoldKeepItalic", node =>
            {
                var tmp = node.AddComponent<TextMeshProUGUI>();
                tmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
                node.AddComponent<UiTextStyleRef>().Role = UiTextRole.Body;   // 非加粗档
            });

            UiTextStyleTool.ApplyTextStyles(TempDirs);

            var tmp2 = LoadTemp("Page_TmpBoldKeepItalic").GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual(FontStyles.Italic, tmp2.fontStyle, "档位清 Bold 位但保留 Italic——字重档只辖 Bold");
        }

        // ---- 对账（三类） ----

        [Test]
        public void 对账_未标记文本报告不冒充()
        {
            MakeTempPrefab("Page_Unmarked", node => node.AddComponent<TextMeshProUGUI>());

            var issues = UiTextStyleTool.ReportTextDrift(TempDirs);
            Assert.AreEqual(1, issues.Count, "未标记文本必须出现在整改清单");
            Assert.AreEqual(UiTextIssueKind.Unmarked, issues[0].Kind);
            StringAssert.Contains("无 UiTextStyleRef", issues[0].Detail);
        }

        [Test]
        public void 对账_属性漂移按档位报明细()
        {
            MakeTempPrefab("Page_Drift", node =>
            {
                var tmp = node.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 99f;                                  // 唯一漂移项
                node.AddComponent<UiTextStyleRef>().Role = UiTextRole.Body;
            });

            var issues = UiTextStyleTool.ReportTextDrift(TempDirs);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(UiTextIssueKind.Mismatch, issues[0].Kind);
            StringAssert.Contains("字号", issues[0].Detail, "漂移明细点名偏离项");
        }

        [Test]
        public void 对账_引用失效两类()
        {
            // TMP font 缺失——节点须**非激活**：TMP OnEnable 会把 null font 自愈为 TMP Settings 默认字体，
            // 装载期激活节点的缺失态保不住；非激活节点装载不跑 OnEnable，null 原样保留
            MakeTempPrefab("Page_FontMissing", node =>
            {
                node.SetActive(false);
                var tmp = node.AddComponent<TextMeshProUGUI>();
                tmp.font = null;
                node.AddComponent<UiTextStyleRef>().Role = UiTextRole.Body;
            });
            // 标记节点无文本组件（应用面跳过项——对账补查）
            MakeTempPrefab("Page_RefNoTarget", node => node.AddComponent<UiTextStyleRef>());

            var fontMissing = UiTextStyleTool.ReportTextDrift(TempDirs)
                .Where(i => i.Kind == UiTextIssueKind.BrokenRef).ToList();
            Assert.AreEqual(1, fontMissing.Count, "font 缺失＝引用失效");
            StringAssert.Contains("font", fontMissing[0].Detail);

            var noTarget = UiTextStyleTool.ReportBrokenRefs(TempDirs);
            Assert.AreEqual(1, noTarget.Count, "标记无文本组件＝引用失效（补查面）");
            StringAssert.Contains("无 TMP_Text/Text", noTarget[0].Detail);
        }

        [Test]
        public void 对账_扫描根注入缝隔离()
        {
            MakeTempPrefab("Page_ScopeA", node => node.AddComponent<TextMeshProUGUI>());   // 无标记（将报未标记）
            var issues = UiTextStyleTool.ReportTextDrift(new[] { TempDir });
            Assert.IsTrue(issues.All(i => i.Asset.StartsWith(TempDir)), "注入扫描根只扫临时面——真实资产不进合成用例报告");
        }

        // ---- 精确角色迁移（P2-3 B4 / AC4） ----

        [Test]
        public void 迁移_直挂加Body_嵌套跳过_幂等()
        {
            string sourcePath = MakeTempPrefab("Src_Body", node =>
            {
                var tmp = node.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 20f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = true;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.fontStyle = FontStyles.Normal;
            });

            // 页面里放一份源 prefab 的嵌套实例
            var root = new GameObject("Page_Nested", typeof(RectTransform));
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), root.transform);
                instance.name = "Txt_SourceInstance";
                string pagePath = TempDir + "/Page_Nested.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, pagePath);
            }
            finally { Object.DestroyImmediate(root); }

            var report = UiTextStyleTool.MigrateExactRoleRefs(UiTokenCatalog.Load(), TempDirs);

            Assert.AreEqual(1, report.Added, "只给直挂源节点加组件：" + string.Join("\n", report.Details));
            Assert.AreEqual(1, report.Inherited, "嵌套实例跳过并计数");
            var source = LoadTemp("Src_Body").GetComponentInChildren<TMP_Text>(true);
            var sourceRef = source.GetComponent<UiTextStyleRef>();
            Assert.IsNotNull(sourceRef, "源模板应加标记");
            Assert.AreEqual(UiTextRole.Body, sourceRef.Role);

            var page = AssetDatabase.LoadAssetAtPath<GameObject>(TempDir + "/Page_Nested.prefab");
            var nested = page.GetComponentInChildren<TMP_Text>(true);
            var inherited = nested.GetComponent<UiTextStyleRef>();
            Assert.IsNotNull(inherited, "嵌套实例应继承源模板标记");
            Assert.IsFalse(PrefabUtility.IsAddedComponentOverride(inherited),
                "不得在嵌套实例上制造新增组件 override");

            var second = UiTextStyleTool.MigrateExactRoleRefs(UiTokenCatalog.Load(), TempDirs);
            Assert.AreEqual(0, second.Added, "幂等：二次新增 0");
        }

        [Test]
        public void 迁移_UGUI按Legacy档匹配()
        {
            MakeTempPrefab("Page_LegacyCaption", node =>
            {
                var legacy = node.AddComponent<Text>();
                legacy.fontSize = 14;
                legacy.alignment = TextAnchor.MiddleLeft;
                legacy.horizontalOverflow = HorizontalWrapMode.Wrap;
                legacy.fontStyle = FontStyle.Normal;
            });

            var report = UiTextStyleTool.MigrateExactRoleRefs(UiTokenCatalog.Load(), TempDirs);

            Assert.AreEqual(1, report.Added);
            var legacyNode = LoadTemp("Page_LegacyCaption").GetComponentInChildren<Text>(true);
            Assert.AreEqual(UiTextRole.Caption, legacyNode.GetComponent<UiTextStyleRef>().Role);
        }

        [Test]
        public void 迁移_不匹配不加_36号留债()
        {
            MakeTempPrefab("Page_36", node =>
            {
                var tmp = node.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 36f;
            });

            var report = UiTextStyleTool.MigrateExactRoleRefs(UiTokenCatalog.Load(), TempDirs);

            Assert.AreEqual(0, report.Added, "不匹配任何角色档——留债");
            Assert.IsNull(LoadTemp("Page_36").GetComponentInChildren<TMP_Text>(true).GetComponent<UiTextStyleRef>());
        }

        [Test]
        public void 实例覆盖_嵌套实例保留属性且仍计未标记债()
        {
            string sourcePath = MakeTempPrefab("Src_OverrideBody", node =>
            {
                var tmp = node.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 20f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = true;
                tmp.overflowMode = TextOverflowModes.Overflow;
                node.AddComponent<UiTextStyleRef>().Role = UiTextRole.Body;
            });

            var root = new GameObject("Page_InstanceOverride", typeof(RectTransform));
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), root.transform);
                instance.name = "Txt_Instance";
                var tmp = instance.GetComponentInChildren<TMP_Text>(true);
                tmp.alignment = TextAlignmentOptions.Left;      // 实例有意覆盖
                var reference = tmp.GetComponent<UiTextStyleRef>();
                var so = new SerializedObject(reference);
                so.FindProperty("UseInstanceOverrides").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(reference);
                PrefabUtility.SaveAsPrefabAsset(root, TempDir + "/Page_InstanceOverride.prefab");
            }
            finally { Object.DestroyImmediate(root); }

            var issues = UiTextStyleTool.ReportTextDrift(TempDirs);
            Assert.AreEqual(1, issues.Count, "实例覆盖仍计未标记债（不静默通过）");
            Assert.AreEqual(UiTextIssueKind.Unmarked, issues[0].Kind);
            StringAssert.Contains("UseInstanceOverrides", issues[0].Detail);

            var (prefabs, writes) = UiTextStyleTool.ApplyTextStyles(TempDirs);
            Assert.AreEqual(0, prefabs, "实例覆盖不被应用改写");
            Assert.AreEqual(0, writes);
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(TempDir + "/Page_InstanceOverride.prefab");
            Assert.AreEqual(TextAlignmentOptions.Left,
                page.GetComponentInChildren<TMP_Text>(true).alignment,
                "保留实例自身对齐");
        }

        [Test]
        public void 实例覆盖_直挂节点非法_OverrideMisuse()
        {
            MakeTempPrefab("Page_DirectOverride", node =>
            {
                var tmp = node.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 20f;
                tmp.alignment = TextAlignmentOptions.Left;
                var reference = node.AddComponent<UiTextStyleRef>();
                reference.Role = UiTextRole.Body;
                reference.UseInstanceOverrides = true;             // 直挂节点不得关闭角色
            });

            var issues = UiTextStyleTool.ReportTextDrift(TempDirs);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(UiTextIssueKind.OverrideMisuse, issues[0].Kind);
            StringAssert.Contains("仅限嵌套", issues[0].Detail);
        }

        // ---- 辅助 ----

        private static GameObject LoadTemp(string name)
            => AssetDatabase.LoadAssetAtPath<GameObject>($"{TempDir}/{name}.prefab");

        private static string MakeTempPrefab(string name, System.Action<GameObject> buildNode)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var node = new GameObject("Txt_Node", typeof(RectTransform));
            node.transform.SetParent(root.transform, false);
            buildNode(node);
            string path = $"{TempDir}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }
    }
}
