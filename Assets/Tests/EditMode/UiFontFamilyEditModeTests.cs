using System;
using System.Collections.Generic;
using LiteTesting;
using NUnit.Framework;
using TMPro;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 字体族接线与覆盖（《UI框架总设计》§9"字体作为字体族和 fallback 链配置"；
    /// 制作规范 §7"按所支持语言检查缺字"）。
    ///
    /// 字体族单源 = TMP Settings（默认字体 + 全局 fallback 链）；本套件钉住两件事：
    /// ①配置契约——NotoSansSC-Regular 挂全局 fallback 且图集为 Dynamic/多图集
    /// （Static 图集遇未预烘焙字符即缺字，Dynamic 才具备 fallback 的"缺字动态补"能力）；
    /// ②代表文本覆盖——逐字符按"主字体 → fallback 逐级"判定（与 TMP 运行时同序）。
    /// 代表文本语料在此内联；全量文本表覆盖检查由文本表侧落库流程负责。
    /// </summary>
    public sealed class UiFontFamilyEditModeTests
    {
        private const string NotoSansScPath = "Assets/ShareResource/Fonts/NotoSansSC-Regular SDF.asset";

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

            var main = TMP_Settings.defaultFontAsset;
            var fallbacks = TMP_Settings.fallbackFontAssets;
            var missing = new List<string>();
            foreach (string text in corpus)
            {
                var seen = new HashSet<char>();
                foreach (char c in text)
                {
                    if (IsControl(c) || !seen.Add(c)) continue;       // 控制字符不占字形；同字只报一次
                    if (!FamilyHas(main, fallbacks, c)) missing.Add($"{text}: 缺 [{c}]");
                }
            }

            Assert.IsEmpty(missing, "代表文本缺字:\n" + string.Join("\n", missing));
        }

        // ---- 覆盖判定：主字体未命中时经 fallback ----

        [Test]
        public void 主字体不含中文_中文经fallback命中()
        {
            var noto = LoadNotoSansSc();
            Assert.IsNotNull(noto);

            // 默认字体（LiberationSans SDF）不含中文——中文必须经 fallback 命中
            Assert.IsFalse(FamilyHas(TMP_Settings.defaultFontAsset, new List<TMP_FontAsset>(), '中'),
                "前置失效：主字体本就含'中'（换默认字体后请调整用例构造）");
            Assert.IsTrue(FamilyHas(TMP_Settings.defaultFontAsset, new List<TMP_FontAsset> { noto }, '中'),
                "fallback 命中即覆盖");
        }

        /// <summary>单字符是否被字体族覆盖（主字体 → fallback 逐级；与 TMP 运行时查找序一致）。</summary>
        private static bool FamilyHas(TMP_FontAsset main, IReadOnlyList<TMP_FontAsset> fallbacks, char c)
        {
            if (main != null && main.HasCharacter(c, false, false)) return true;
            if (fallbacks == null) return false;
            for (int i = 0; i < fallbacks.Count; i++)
            {
                var font = fallbacks[i];
                if (font != null && font.HasCharacter(c, false, false)) return true;
            }
            return false;
        }

        /// <summary>排版控制字符（不占字形——由 TMP/排版层处理，不是缺字）。</summary>
        private static bool IsControl(char c)
            => c == '\n' || c == '\r' || c == '\t' || char.IsControl(c);
    }
}
