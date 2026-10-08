using System.Collections.Generic;
using System.Linq;
using LiteGame.UI;
using LiteTesting;
using NUnit.Framework;
using TMPro;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 字体族接线与缺字审计（《UI框架总设计》§9"字体作为字体族和 fallback 链配置，检查缺字"；
    /// 制作规范 §7"按所支持语言检查缺字"）。
    ///
    /// 字体族单源 = TMP Settings（默认字体 + 全局 fallback 链）；本套件钉住两件事：
    /// ①配置契约——NotoSansSC-Regular 挂全局 fallback 且图集为 Dynamic/多图集
    /// （Static 图集遇未预烘焙字符即缺字，Dynamic 才具备 fallback 的"缺字动态补"能力）；
    /// ②审计器行为——覆盖判定逐级、控制字符不计缺字、去重、报告可读。
    /// 代表文本语料在此内联；tbtext 表落库后装载侧平铺全量文本进审计入口（见 FontGlyphAudit 注释）。
    /// </summary>
    public sealed class UiFontFamilyEditModeTests
    {
        private const string NotoSansScPath = "Assets/Fonts/NotoSansSC-Regular SDF.asset";

        private static TMP_FontAsset LoadNotoSansSc()
            => UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NotoSansScPath);

        // ---- 配置契约（防回退：TMP Settings 被重置即红）----

        [Test]
        [Category(TestCategory.Contract)]
        public void Fallback链已挂NotoSansSC_且图集为Dynamic多图集()
        {
            var noto = LoadNotoSansSc();
            Assert.IsNotNull(noto, $"字体资产缺失:{NotoSansScPath}");

            var fallbacks = TMP_Settings.fallbackFontAssets;
            Assert.IsNotNull(fallbacks, "TMP 全局 fallback 列表缺失");
            Assert.IsTrue(fallbacks.Contains(noto), "NotoSansSC 必须挂在 TMP 全局 fallback 链（中文 TMP 文本缺字回退的唯一来源）");

            Assert.AreEqual(AtlasPopulationMode.Dynamic, noto.atlasPopulationMode,
                "fallback 字体图集必须 Dynamic——静态图集遇未预烘焙字符直接缺字");
            Assert.IsTrue(noto.isMultiAtlasTexturesEnabled, "图集容量上限时需自动扩展（多图集）");
        }

        // ---- 字体族覆盖：中英样例全过 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 中英代表文本_字体族全覆盖()
        {
            var noto = LoadNotoSansSc();
            Assert.IsNotNull(noto);
            Assert.IsTrue(noto.HasCharacter('中', true, false), "NotoSansSC 必须含中文（防资产被换）");

            // 代表语料：当前 UI 面常用语义（中文全角/标点）+ 英文/占位符（ASCII）
            string[] corpus =
            {
                "主界面", "金币", "确定", "取消", "设置", "返回", "暂无数据", "加载中…",
                "物品 {0} 件", "组队、聊天与公会",
                "Main", "Gold {0}", "OK, Cancel!", "Loading...", "items",
            };

            var results = FontGlyphAudit.Audit(corpus, TMP_Settings.defaultFontAsset, TMP_Settings.fallbackFontAssets);
            var missing = results.Where(r => !r.Covered).ToList();
            Assert.IsEmpty(missing, "代表文本缺字:\n" + FontGlyphAudit.Report(results));
        }

        // ---- 审计器行为 ----

        [Test]
        public void 审计器_主字体未覆盖时经fallback命中()
        {
            var noto = LoadNotoSansSc();
            Assert.IsNotNull(noto);

            // 默认字体（LiberationSans SDF）不含中文——中文必须经 fallback 命中
            Assert.IsFalse(FontGlyphAudit.FamilyHas(TMP_Settings.defaultFontAsset,
                new List<TMP_FontAsset>(), '中'), "前置失效：主字体本就含'中'（换默认字体后请调整用例构造）");
            Assert.IsTrue(FontGlyphAudit.FamilyHas(TMP_Settings.defaultFontAsset,
                new List<TMP_FontAsset> { noto }, '中'), "fallback 命中即覆盖");
        }

        [Test]
        public void 审计器_缺字检出_控制字符不计_同字去重()
        {
            // fallback 置空：中文全部缺字
            var results = FontGlyphAudit.Audit(new[] { "中\n文A\t文" }, TMP_Settings.defaultFontAsset,
                new List<TMP_FontAsset>());

            Assert.AreEqual(1, results.Count);
            var r = results[0];
            Assert.IsFalse(r.Covered);
            CollectionAssert.AreEqual(new[] { '中', '文' }, r.Missing,
                "缺字 = 中文两字符（换行/制表不计、'文'重复只报一次、'A' 主字体已覆盖）");
        }

        [Test]
        public void 审计器_报告可读_全覆盖为单行()
        {
            var covered = FontGlyphAudit.Audit(new[] { "Main" }, TMP_Settings.defaultFontAsset,
                TMP_Settings.fallbackFontAssets);
            StringAssert.Contains("全覆盖", FontGlyphAudit.Report(covered));

            var missing = FontGlyphAudit.Audit(new[] { "中文" }, TMP_Settings.defaultFontAsset,
                new List<TMP_FontAsset>());
            var report = FontGlyphAudit.Report(missing);
            StringAssert.Contains("1 条文本缺字", report);
            StringAssert.Contains("中", report);
        }
    }
}
