using System.Linq;
using LiteGame.EditorTools.UI;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 令牌源（`UiStyleTokens.json`，schema 2.0）：解析、校验与规范文本/指纹。
    /// 《UI 编辑器工具链专项设计》§8.1「令牌源：JSON → 生成 UiThemeAsset」；schema 见
    /// `Docs/设计文档/客户端/UI系统/UiStyleTokens.schema.json`。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiThemeTokensEditModeTests
    {
        private const string BaseJson =
            "{\"schemaVersion\":\"2.0\"," +
            "\"meta\":{\"name\":\"UiTheme\",\"version\":\"1.0.0\",\"theme\":\"Default\"}," +
            "\"groups\":{" +
            "\"color\":{\"tokens\":{__COLOR__}}," +
            "\"typography\":{\"tokens\":{__TYPE__}}," +
            "\"spacing\":{\"tokens\":{}}," +
            "\"radius\":{\"tokens\":{}}," +
            "\"elevation\":{\"tokens\":{}}," +
            "\"motion\":{\"tokens\":{}}," +
            "\"icon\":{\"tokens\":{}}}}";

        private const string DefaultColor = "\"color.bg.screen\":{\"value\":\"#1F1F29F2\"}";
        private const string DefaultType =
            "\"font.title\":{\"fontFamily\":\"NotoSansSC SDF\",\"fontSize\":24,\"lineHeight\":1.0,\"weight\":\"Regular\"}";

        private static string Doc(string color = DefaultColor, string typography = DefaultType)
            => BaseJson.Replace("__COLOR__", color).Replace("__TYPE__", typography);

        // ---- 解析 ----

        [Test]
        public void 解析_七组齐全且按组计数()
        {
            UiThemeTokens tokens = UiThemeTokenJson.Deserialize(Doc());

            Assert.IsNotNull(tokens, "令牌源应可解析");
            Assert.AreEqual("2.0", tokens.SchemaVersion);
            Assert.AreEqual("Default", tokens.Meta?.Theme, "主题名");
            Assert.AreEqual("1.0.0", tokens.Meta?.Version, "版本");
            Assert.AreEqual(1, tokens.Groups.Color.Tokens.Count, "颜色组 1 条");
            Assert.AreEqual(1, tokens.Groups.Typography.Tokens.Count, "文字组 1 条");
            Assert.AreEqual(0, tokens.Groups.Spacing.Tokens.Count, "空组合法");
            Assert.IsNotNull(tokens.Groups.Icon, "七组必须都存在");
        }

        [Test]
        public void 校验_合法令牌源零问题()
        {
            Assert.IsEmpty(UiThemeTokenValidator.Validate(UiThemeTokenJson.Deserialize(Doc())), "合法源不应报问题");
        }

        // ---- 校验：ID 与组归属 ----

        [Test]
        public void 校验_ID前缀与组不符报错()
        {
            var issues = UiThemeTokenValidator.Validate(
                UiThemeTokenJson.Deserialize(Doc(color: "\"font.wrong\":{\"value\":\"#FFFFFF\"}")));

            Assert.IsTrue(issues.Any(issue => issue.Contains("font.wrong")), "应指出越组令牌：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 校验_ID文法非法报错()
        {
            var issues = UiThemeTokenValidator.Validate(
                UiThemeTokenJson.Deserialize(Doc(color: "\"color.Bad_Name\":{\"value\":\"#FFFFFF\"}")));

            Assert.IsTrue(issues.Any(issue => issue.Contains("color.Bad_Name")), "应指出非法 ID 文法：\n" + string.Join("\n", issues));
        }

        // ---- 校验：值域 ----

        [Test]
        public void 校验_颜色格式非法报错()
        {
            var issues = UiThemeTokenValidator.Validate(
                UiThemeTokenJson.Deserialize(Doc(color: "\"color.bg.screen\":{\"value\":\"#GGGGGG\"}")));

            Assert.IsTrue(issues.Any(issue => issue.Contains("color.bg.screen")), "应指出颜色格式：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 校验_字号越界报错()
        {
            var issues = UiThemeTokenValidator.Validate(UiThemeTokenJson.Deserialize(Doc(typography:
                "\"font.title\":{\"fontFamily\":\"f\",\"fontSize\":97,\"lineHeight\":1.0,\"weight\":\"Regular\"}")));

            Assert.IsTrue(issues.Any(issue => issue.Contains("font.title") && issue.Contains("fontSize")),
                "应指出字号越界：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 校验_行高越界报错()
        {
            var issues = UiThemeTokenValidator.Validate(UiThemeTokenJson.Deserialize(Doc(typography:
                "\"font.title\":{\"fontFamily\":\"f\",\"fontSize\":24,\"lineHeight\":3.0,\"weight\":\"Regular\"}")));

            Assert.IsTrue(issues.Any(issue => issue.Contains("lineHeight")), "应指出行高越界：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 校验_未知字重报错()
        {
            var issues = UiThemeTokenValidator.Validate(UiThemeTokenJson.Deserialize(Doc(typography:
                "\"font.title\":{\"fontFamily\":\"f\",\"fontSize\":24,\"lineHeight\":1.0,\"weight\":\"Heavy\"}")));

            Assert.IsTrue(issues.Any(issue => issue.Contains("weight")), "应指出字重非法：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 校验_缺组报错()
        {
            string json = Doc().Replace(",\"icon\":{\"tokens\":{}}", "");
            var issues = UiThemeTokenValidator.Validate(UiThemeTokenJson.Deserialize(json));

            Assert.IsTrue(issues.Any(issue => issue.Contains("icon")), "七组缺一应报错：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 校验_meta缺失报错()
        {
            string json = Doc().Replace(
                "\"meta\":{\"name\":\"UiTheme\",\"version\":\"1.0.0\",\"theme\":\"Default\"}", "\"meta\":{}");
            var issues = UiThemeTokenValidator.Validate(UiThemeTokenJson.Deserialize(json));

            Assert.IsTrue(issues.Any(issue => issue.Contains("meta")), "meta 必填字段缺失应报错：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 解析_未知字段被拒绝()
        {
            Assert.That(
                () => UiThemeTokenJson.Deserialize(
                    Doc(color: "\"color.bg.screen\":{\"value\":\"#FFFFFF\",\"valueTypo\":1}")),
                Throws.Exception,
                "未知字段必须报错，不静默忽略");
        }

        [Test]
        public void 解析_重复键被拒绝()
        {
            Assert.That(
                () => UiThemeTokenJson.Deserialize(Doc(color:
                    "\"color.bg.screen\":{\"value\":\"#1F1F29F2\"},\"color.bg.screen\":{\"value\":\"#FFFFFFFF\"}")),
                Throws.Exception.Message.Contains("重复键"),
                "重复键必须报错，不能被后者覆盖");
        }

        [Test]
        public void 解析_数值令牌缺value被拒绝()
        {
            string json = Doc().Replace("\"spacing\":{\"tokens\":{}}", "\"spacing\":{\"tokens\":{\"space.x\":{}}}");
            Assert.That(() => UiThemeTokenJson.Deserialize(json), Throws.Exception.Message.Contains("value"),
                "数值令牌缺 value 必须报错（0 是合法值，不能靠默认值判缺失）");
        }

        [Test]
        public void 解析_层级令牌缺偏移被拒绝()
        {
            string json = Doc().Replace("\"elevation\":{\"tokens\":{}}",
                "\"elevation\":{\"tokens\":{\"elev.x\":{\"color\":\"#000000FF\",\"blur\":0,\"spread\":0,\"offsetX\":0}}}");
            Assert.That(() => UiThemeTokenJson.Deserialize(json), Throws.Exception.Message.Contains("offsetY"),
                "层级令牌缺 offsetY 必须报错");
        }

        [Test]
        public void 解析_动效令牌缺时长被拒绝()
        {
            string json = Doc().Replace("\"motion\":{\"tokens\":{}}",
                "\"motion\":{\"tokens\":{\"motion.x\":{\"easing\":\"out\"}}}");
            Assert.That(() => UiThemeTokenJson.Deserialize(json), Throws.Exception.Message.Contains("durationMs"),
                "动效令牌缺 durationMs 必须报错");
        }

        // ---- 规范文本与指纹 ----

        [Test]
        public void 规范文本_令牌按ID排序且两次一致()
        {
            string json = Doc(color:
                "\"color.bg.screen\":{\"value\":\"#1F1F29F2\"},\"color.bg.deep\":{\"value\":\"#14141AE6\"}");
            UiThemeTokens tokens = UiThemeTokenJson.Deserialize(json);

            string first = UiThemeTokenJson.Serialize(tokens);
            string second = UiThemeTokenJson.Serialize(UiThemeTokenJson.Deserialize(json));

            Assert.AreEqual(first, second, "同源两次序列化必须逐字一致");
            Assert.Less(first.IndexOf("\"color.bg.deep\"", System.StringComparison.Ordinal),
                first.IndexOf("\"color.bg.screen\"", System.StringComparison.Ordinal), "组内令牌按 ID 排序");
            StringAssert.EndsWith("\n", first, "规范文本以 LF 末尾换行");
        }

        [Test]
        public void 指纹_为64位小写十六进制且随内容变化()
        {
            string a = UiThemeTokenJson.HashCanonical(UiThemeTokenJson.Serialize(UiThemeTokenJson.Deserialize(Doc())));
            string b = UiThemeTokenJson.HashCanonical(UiThemeTokenJson.Serialize(UiThemeTokenJson.Deserialize(
                Doc(color: "\"color.bg.screen\":{\"value\":\"#1F1F29F3\"}"))));

            Assert.AreEqual(64, a.Length, "SHA-256 十六进制 64 位");
            StringAssert.IsMatch("^[0-9a-f]{64}$", a);
            Assert.AreNotEqual(a, b, "值变化必须改变指纹");
        }
    }
}
