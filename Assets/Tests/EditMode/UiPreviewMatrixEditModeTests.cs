using System.Collections.Generic;
using System.Linq;
using LiteGame;
using LiteGame.Editor.UiTools.Preview;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 预览矩阵纯函数面与字体缩放（P2-3 B3 / AC2、AC3）：组合展开/文件名/索引结构（含降级记录）、
    /// 字体缩放应用与原值恢复。GameView 分辨率反射与真跑矩阵不进 EditMode（真跑矩阵为验收实产证据）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiPreviewMatrixEditModeTests
    {
        private static UiDeviceProfileDocument Doc(int inMatrixCount)
        {
            var doc = new UiDeviceProfileDocument { SchemaVersion = "1.0" };
            for (int i = 0; i < inMatrixCount; i++)
                doc.Profiles.Add(new UiDeviceProfile
                {
                    Id = "test.dev-" + i, Width = 1000 + i, Height = 500, Orientation = "landscape",
                    Aspect = 2f, SafeInsets = new UiSafeInsets(), InMatrix = true,
                });
            doc.Profiles.Add(new UiDeviceProfile
            { Id = "test.portrait", Width = 500, Height = 1000, Orientation = "portrait", Aspect = 0.5f, SafeInsets = new UiSafeInsets(), InMatrix = false });
            return doc;
        }

        [Test]
        public void 展开_组合数与确定性顺序()
        {
            List<UiPreviewCombo> combos = UiPreviewMatrix.Expand(Doc(2));
            // 2 入矩阵档 × 3 语言 × 3 字体档 = 18；顺序：档 id → 语言 → 缩放
            Assert.AreEqual(18, combos.Count, "档×语言×字体缩放全组合");
            CollectionAssert.AreEqual(
                combos.Select(c => c.ProfileId).Distinct().ToList(),
                combos.Select(c => c.ProfileId).Distinct().OrderBy(i => i, System.StringComparer.Ordinal).ToList(),
                "档位按 id Ordimal 排序");
        }

        [Test]
        public void 展开_排除非入矩阵档()
        {
            List<UiPreviewCombo> combos = UiPreviewMatrix.Expand(Doc(1));
            Assert.IsNotEmpty(combos, "1 入矩阵档 × 3 语言 × 3 字体档应非空");
            Assert.AreEqual(9, combos.Count, "排除了非入矩阵档后的组合数");
            Assert.IsTrue(combos.All(c => c.ProfileId != "test.portrait"), "inMatrix=false 不进默认矩阵");
        }

        [Test]
        public void 文件名_确定性与平台安全()
        {
            var combo = new UiPreviewCombo { ProfileId = "phone.19-5-9", Locale = "zh-CN", FontScale = 1.15f };
            string name = UiPreviewMatrix.FileName(combo);
            StringAssert.DoesNotContain(" ", name, "无空格");
            StringAssert.EndsWith(".png", name);
            Assert.AreEqual(name, UiPreviewMatrix.FileName(combo), "同组合同名（确定性）");
            Assert.AreNotEqual(name, UiPreviewMatrix.FileName(
                new UiPreviewCombo { ProfileId = "phone.19-5-9", Locale = "en", FontScale = 1.15f }), "不同组合不同名");
        }

        [Test]
        public void 索引_结构含分辨率结果与降级记录()
        {
            List<UiPreviewCombo> combos = UiPreviewMatrix.Expand(Doc(1));
            var applied = new Dictionary<string, bool> { { UiPreviewMatrix.FileName(combos[0]), false } };
            string index = UiPreviewMatrix.BuildIndex("Page_Login", combos, applied, new List<string> { "GameView 反射不可用——分辨率维度降级" });
            Assert.IsNotEmpty(index);
            StringAssert.Contains("Page_Login", index, "页名");
            StringAssert.Contains("resolutionApplied", index, "分辨率应用结果");
            StringAssert.Contains("降级", index, "降级记录");
        }

        [Test]
        public void 计划_默认产物目录与组合()
        {
            var options = new UiPreviewMatrixOptions
            {
                PagePath = "Assets/ShareResource/UI/Screens/Page_Login.prefab",
                ProfileIds = new List<string> { "pc.16-9" },
                Locales = new List<string> { "zh-CN" },
                FontScales = new List<float> { 1f },
            };
            Assert.IsTrue(UiPreviewMatrixPlanner.TryCreatePlan(options, out UiPreviewMatrixPlan plan, out string error),
                error);
            Assert.AreEqual("Artifacts/PreviewMatrix/Page_Login", plan.OutputDir);
            Assert.AreEqual(1, plan.Combos.Count);
        }

        [Test]
        public void 计划_产物目录逃逸拒绝()
        {
            var options = new UiPreviewMatrixOptions
            {
                PagePath = "Assets/ShareResource/UI/Screens/Page_Login.prefab",
                OutputDir = "Artifacts/../Assets/PreviewMatrix",
            };
            Assert.IsFalse(UiPreviewMatrixPlanner.TryCreatePlan(options, out _, out string error));
            StringAssert.Contains("不能包含", error);
        }

        [Test]
        public void 计划_非法语言拒绝()
        {
            var options = new UiPreviewMatrixOptions
            {
                PagePath = "Assets/ShareResource/UI/Screens/Page_Login.prefab",
                Locales = new List<string> { "fr" },
            };
            Assert.IsFalse(UiPreviewMatrixPlanner.TryCreatePlan(options, out _, out string error));
            StringAssert.Contains("不支持的语言", error);
        }

        [Test]
        public void 语言不可用_仅非空语言且状态unavailable()
        {
            Assert.IsFalse(UiPreviewMatrix.IsLocaleUnavailable(string.Empty, "unavailable"));
            Assert.IsTrue(UiPreviewMatrix.IsLocaleUnavailable("en", "unavailable"));
            Assert.IsFalse(UiPreviewMatrix.IsLocaleUnavailable("en", "pending"));
            Assert.IsFalse(UiPreviewMatrix.IsLocaleUnavailable("en", "ok"));
        }

        [Test]
        public void 运行_语言失败保留分辨率结果()
        {
            var combo = new UiPreviewCombo { ProfileId = "pc.16-9", Locale = "en", FontScale = 1f };
            var run = new UiPreviewMatrixRun("Page_Login", new List<UiPreviewCombo> { combo });
            run.RecordFailure(combo, "语言表直载失败", resolutionApplied: true);
            string index = run.BuildIndex();
            StringAssert.Contains("\"resolutionApplied\": true", index, "分辨率已应用不应被失败覆盖");
            StringAssert.Contains("语言表直载失败", index);
        }

        [Test]
        public void 运行_失败组合不中断且索引留痕()
        {
            var combos = new List<UiPreviewCombo>
            {
                new UiPreviewCombo { ProfileId = "pc.16-9", Locale = "zh-CN", FontScale = 1f },
                new UiPreviewCombo { ProfileId = "pc.16-9", Locale = "en", FontScale = 1f },
                new UiPreviewCombo { ProfileId = "pc.16-9", Locale = "pseudo", FontScale = 1f },
            };
            var run = new UiPreviewMatrixRun("Page_Login", combos);
            run.RecordSuccess(combos[0], true);
            run.RecordFailure(combos[1], "截图失败");
            run.RecordSuccess(combos[2], false);

            Assert.AreEqual(UiPreviewMatrixRunStatus.CompletedWithFailures, run.Status, "单组合失败不中断整体");
            Assert.AreEqual(2, run.Succeeded.Count);
            Assert.AreEqual(1, run.Failed.Count);
            string index = run.BuildIndex();
            StringAssert.Contains("截图失败", index, "失败原因留痕");
            StringAssert.Contains("Page_Login", index, "页名");
        }

        [Test]
        public void 运行_中断终态保留已完成组合()
        {
            var combos = new List<UiPreviewCombo>
            {
                new UiPreviewCombo { ProfileId = "pc.16-9", Locale = "zh-CN", FontScale = 1f },
                new UiPreviewCombo { ProfileId = "pc.16-9", Locale = "en", FontScale = 1f },
            };
            var run = new UiPreviewMatrixRun("Page_Login", combos);
            run.RecordSuccess(combos[0], true);
            run.MarkInterrupted();

            Assert.AreEqual(UiPreviewMatrixRunStatus.Interrupted, run.Status, "中断为独立终态");
            Assert.AreEqual(1, run.Succeeded.Count, "已截组合保留");
            StringAssert.Contains("interrupted", run.BuildIndex(), "索引标注中断");
        }

        [Test]
        public void 字体缩放_应用与原值恢复()
        {
            var go = new GameObject("tmp-text", typeof(RectTransform));
            try
            {
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(400f, 40f);
                var text = go.AddComponent<TextMeshProUGUI>();
                text.fontSize = 20f;
                var scale = new PreviewFontScale();

                scale.Apply(go, 1.3f);
                Assert.AreEqual(26f, text.fontSize, 0.01f, "1.3 档＝20×1.3");

                scale.Apply(go, 1.0f);
                Assert.AreEqual(20f, text.fontSize, 0.01f, "重复施加先恢复再按新档（幂等）");

                scale.Restore();
                Assert.AreEqual(20f, text.fontSize, 0.01f, "恢复记录的原值（非逆乘）");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
