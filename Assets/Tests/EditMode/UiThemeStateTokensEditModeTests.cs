using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 状态色令牌补集（P2-3 B2 / AC6-①，D3 用户裁决"本批令牌化"）：令牌源 JSON 与生成物 SO 各含 8 枚
    /// `color.state.*`（值＝prefab 序列化现值量化，零可见变化；清单与来源见 plan.md 映射表）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiThemeStateTokensEditModeTests
    {
        /// <summary>8 枚状态令牌（ID → 量化值；来源 file:line 见 .workflow/active/ui-p2-token-refs/plan.md 映射表）。</summary>
        private static readonly (string Id, string Hex)[] StateTokens =
        {
            ("color.state.interact.normal", "#4073BFFF"),
            ("color.state.interact.pressed", "#264D8CFF"),
            ("color.state.interact.disabled", "#595959FF"),
            ("color.state.interact.selected", "#E6B333FF"),
            ("color.state.nav.selected", "#F2CC4DFF"),
            ("color.state.nav.normal", "#BFBFC7FF"),
            ("color.state.star.on", "#FFCC33FF"),
            ("color.state.star.off", "#4D4D4DFF"),
        };

        private static Dictionary<string, string> LoadColorTokens()
        {
            string json = File.ReadAllText(UiThemePaths.FullPath(UiThemePaths.DefaultJsonPath));
            UiThemeTokens tokens = UiThemeTokenJson.Deserialize(json);
            Assert.IsNotNull(tokens, "令牌源应可解析");
            return tokens.Groups.Color.Tokens.ToDictionary(
                pair => pair.Key, pair => pair.Value.Value, System.StringComparer.Ordinal);
        }

        [Test]
        public void 令牌源_八枚状态色齐全且值精确()
        {
            Dictionary<string, string> colors = LoadColorTokens();
            foreach ((string id, string hex) in StateTokens)
            {
                Assert.IsTrue(colors.TryGetValue(id, out string value), "缺状态令牌：" + id);
                Assert.AreEqual(hex, value.ToUpperInvariant(), id + " 值应为现值量化（零可见变化）");
            }
        }

        [Test]
        public void 生成物SO_八枚状态色同步()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            foreach ((string id, string hex) in StateTokens)
            {
                Assert.IsTrue(catalog.TryColor(id, out Color value), "SO 缺状态令牌：" + id + "（跑 ui.style.tokens 重生成）");
                Assert.IsTrue(UiThemeColorHex.TryParse(hex, out Color32 expected), hex);
                Assert.AreEqual((Color32)value, expected, id + " SO 值与令牌源一致");
            }
        }

        [Test]
        public void 命名_状态色全部落在color_state组()
        {
            Assert.IsTrue(StateTokens.All(pair => pair.Id.StartsWith("color.state.", System.StringComparison.Ordinal)),
                "状态令牌 ID 前缀＝color.state.（组归属由 schema 校验单源）");
        }
    }
}
