using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// <see cref="IJsonSerializer"/> 的测试替身——.NET 内建 <c>System.Text.Json</c>，
    /// 零新依赖（与 <c>FileSysTests.FakeJsonSerializer</c> 同款；Newtonsoft 属 Unity 层）。
    /// </summary>
    internal sealed class TestJsonSerializer : IJsonSerializer
    {
        public string Serialize<T>(T value) => JsonSerializer.Serialize(value);
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// 本地化（《UI框架总设计》§9）：目录查询、回退链、缺 key 策略、复数选取、
    /// 参数转义、key 命名校验、覆盖率诊断。
    /// </summary>
    public sealed class LocalizationTests
    {
        private static LocalizationCatalog Catalog()
        {
            var c = new LocalizationCatalog();
            c.Add("UI.Main.Title", "zh-CN", "主界面");
            c.Add("UI.Main.Title", "en", "Main");
            c.Add("Common.Ok", "zh-CN", "确定");          // 故意只有源语言
            c.Add("UI.Main.Count", "zh-CN", "{0} 个");
            c.Add("UI.Main.Count", "en", "{0} items");
            c.Add("UI.Main.Count.one", "en", "{0} item");
            c.Add("UI.Main.Count.other", "en", "{0} items");
            return c;
        }

        // ---- 目录查询与回退链 ----

        [Fact]
        public void 查得到_目标语言优先()
        {
            Assert.True(Catalog().TryGet("UI.Main.Title", "en", out string t, out bool fb));
            Assert.Equal("Main", t);
            Assert.False(fb);
        }

        [Fact]
        public void 目标语言缺失_回退源语言并标记()
        {
            // §9"发布期先回退源语言"
            Assert.True(Catalog().TryGet("Common.Ok", "en", out string t, out bool fb));
            Assert.Equal("确定", t);
            Assert.True(fb, "回退必须可观测——否则覆盖率统计失真");
        }

        [Fact]
        public void 源语言也没有_返回false()
        {
            Assert.False(Catalog().TryGet("UI.Nope", "en", out _, out _));
            Assert.False(Catalog().TryGet(null, "en", out _, out _));
            Assert.False(Catalog().TryGet("", "en", out _, out _));
        }

        [Fact]
        public void 空locale_走源语言()
        {
            Assert.True(Catalog().TryGet("UI.Main.Title", null, out string t, out bool fb));
            Assert.Equal("主界面", t);
            Assert.True(fb);
        }

        [Fact]
        public void 登记去重_同key同locale覆盖()
        {
            var c = new LocalizationCatalog();
            c.Add("UI.A", "en", "1");
            c.Add("UI.A", "en", "2");
            Assert.Equal(1, c.KeyCount);
            c.TryGet("UI.A", "en", out string t, out _);
            Assert.Equal("2", t);
        }

        [Fact]
        public void 登记拒绝空key或空locale()
        {
            var c = new LocalizationCatalog();
            Assert.Throws<ArgumentException>(() => c.Add("", "en", "x"));
            Assert.Throws<ArgumentException>(() => c.Add("UI.A", "", "x"));
            Assert.Throws<ArgumentException>(() => c.Add("UI.A", null, "x"));
        }

        // ---- 缺 key 策略（§9）----

        [Fact]
        public void 缺key_开发期显示方括号key并计数()
        {
            var svc = new LocalizationService(Catalog(), "en", developmentMode: true);
            Assert.Equal("[UI.Nope]", svc.Raw("UI.Nope"));
            Assert.Equal(1, svc.MissingKeyCount);
        }

        [Fact]
        public void 缺key_发布期显示占位并计数()
        {
            var svc = new LocalizationService(Catalog(), "en", developmentMode: false);
            Assert.Equal("…", svc.Raw("UI.Nope"));
            Assert.Equal(1, svc.MissingKeyCount);            // 发布期也必须计数
        }

        [Fact]
        public void 缺key_回调收到key()
        {
            var svc = new LocalizationService(Catalog(), "en");
            var seen = new List<string>();
            svc.OnMissingKey = seen.Add;
            svc.Raw("UI.X"); svc.Raw("UI.Y");
            Assert.Equal(new[] { "UI.X", "UI.Y" }, seen);
        }

        // ---- 语言切换（§9 不重跑 OnShow）----

        [Fact]
        public void 切语言_触发事件()
        {
            var svc = new LocalizationService(Catalog(), "zh-CN");
            var got = new List<string>();
            svc.OnLocaleChanged += got.Add;

            svc.SetLocale("en");
            Assert.Equal("en", svc.Locale);
            Assert.Equal(new[] { "en" }, got);
        }

        [Fact]
        public void 切语言_同值不触发()
        {
            var svc = new LocalizationService(Catalog(), "en");
            int hits = 0;
            svc.OnLocaleChanged += _ => hits++;
            svc.SetLocale("en");
            Assert.Equal(0, hits);
        }

        [Fact]
        public void 切语言_空值忽略()
        {
            var svc = new LocalizationService(Catalog(), "en");
            svc.SetLocale(null);
            svc.SetLocale("");
            Assert.Equal("en", svc.Locale);
        }

        [Fact]
        public void 切语言后_查询走新语言()
        {
            var svc = new LocalizationService(Catalog(), "zh-CN");
            Assert.Equal("主界面", svc.Raw("UI.Main.Title"));
            svc.SetLocale("en");
            Assert.Equal("Main", svc.Raw("UI.Main.Title"));
        }

        // ---- 参数与转义（§9）----

        [Fact]
        public void 填参_编号替换()
        {
            var svc = new LocalizationService(Catalog(), "en");
            Assert.Equal("3 items", svc.Format("UI.Main.Count", 3));
        }

        [Fact]
        public void 填参_参数转义富文本标记()
        {
            // §9"玩家/外部文本默认禁富文本"——玩家名里的 < 不能变成标签。
            // 只替换 '<'：它是 TMP 标签的唯一入口，单独一个 '>' 不构成标签。
            var svc = new LocalizationService(Catalog(), "zh-CN");
            Assert.Equal("＜script> 个", svc.Format("UI.Main.Count", "<script>"));
        }

        [Fact]
        public void 填参_无参数原样返回()
        {
            var svc = new LocalizationService(Catalog(), "en");
            Assert.Equal("Main", svc.Format("UI.Main.Title"));
            Assert.Equal("Main", svc.Format("UI.Main.Title", null));
        }

        [Fact]
        public void 填参_编号越界原样保留不抛()
        {
            var c = new LocalizationCatalog();
            c.Add("UI.High", "en", "{5} items");     // 模板要求编号 5
            var svc = new LocalizationService(c, "en");

            // 只给 3 个参数 → 编号 5 越界：保留占位原文（UI 文本不值得炸流程，但要可观测）
            Assert.Equal("{5} items", svc.Format("UI.High", 1, 2, 3));
            // 给足 6 个 → 正常替换
            Assert.Equal("X items", svc.Format("UI.High", 1, 2, 3, 4, 5, "X"));
        }

        [Fact]
        public void 填参_未闭合花括号原样()
        {
            var c = new LocalizationCatalog();
            c.Add("UI.A", "en", "x {0");
            var svc = new LocalizationService(c, "en");
            Assert.Equal("x {0", svc.Format("UI.A", 1));
        }

        // ---- 复数（§9 显式选取）----

        [Fact]
        public void 复数_英文一与多()
        {
            var svc = new LocalizationService(Catalog(), "en");
            Assert.Equal("1 item", svc.FormatPlural("UI.Main.Count", 1, 1));
            Assert.Equal("2 items", svc.FormatPlural("UI.Main.Count", 2, 2));
        }

        [Fact]
        public void 复数_中文一律other()
        {
            var svc = new LocalizationService(Catalog(), "zh-CN");
            // zh-CN 数据只登记了 .other 形式（源语言数据只需 other）
            Assert.Equal("1 个", svc.FormatPlural("UI.Main.Count", 1, 1));
            Assert.Equal("5 个", svc.FormatPlural("UI.Main.Count", 5, 5));
        }

        [Fact]
        public void 复数_无one形式回退other()
        {
            // en 有 .one 时才用 .one；此处构造只有 other 的 key
            var c = new LocalizationCatalog();
            c.Add("UI.B", "en", "raw");
            c.Add("UI.B.other", "en", "{0} things");
            var svc = new LocalizationService(c, "en");
            Assert.Equal("1 things", svc.FormatPlural("UI.B", 1, 1));   // 回退 .other（显式，不猜）
        }

        [Fact]
        public void 复数_无复数形式回退裸key()
        {
            var svc = new LocalizationService(Catalog(), "en");
            Assert.Equal("Main", svc.FormatPlural("UI.Main.Title", 3));
        }

        [Theory]
        [InlineData("en", 1, PluralCategory.One)]
        [InlineData("en", 0, PluralCategory.Other)]
        [InlineData("en", 2, PluralCategory.Other)]
        [InlineData("en-US", 1, PluralCategory.One)]
        [InlineData("zh-CN", 1, PluralCategory.Other)]
        [InlineData("ja", 1, PluralCategory.Other)]
        [InlineData("", 1, PluralCategory.Other)]
        public void 复数规则_首版覆盖zh与en(string locale, long n, PluralCategory expected)
        {
            Assert.Equal(expected, LocalizationService.PluralRule(locale, n));
        }

        // ---- key 命名（§9）----

        [Theory]
        [InlineData("UI.Main.Title")]
        [InlineData("Common.Ok")]
        [InlineData("UI.A.B.C")]
        [InlineData("UI.Main.Count_1")]
        public void key命名_合法(string key)
        {
            Assert.Null(LTextKey.Validate(key));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Main.Title")]          // 缺前缀
        [InlineData("Other.Title")]         // 前缀不在白名单
        [InlineData("UI")]                  // 单段
        [InlineData("UI.")]                 // 空段
        [InlineData("UI..Title")]           // 空段
        [InlineData("UI.Main-Title")]       // 非法字符
        [InlineData("UI.Main.Title ")]      // 尾随空格
        public void key命名_非法(string key)
        {
            Assert.NotNull(LTextKey.Validate(key));
        }

        // ---- 表解析与覆盖率 ----

        private const string TableJson = @"[
          {""key"":""UI.Main.Title"",""zh-CN"":""主界面"",""en"":""Main""},
          {""key"":""Common.Ok"",""zh-CN"":""确定""}
        ]";

        [Fact]
        public void 表解析_正常()
        {
            var serializer = new TestJsonSerializer();
            LocalizationCatalog c = LocalizationTable.Parse(TableJson, serializer, true, out var problems);
            Assert.NotNull(c);
            Assert.Equal(2, c.KeyCount);
            c.TryGet("UI.Main.Title", "en", out string t, out _);
            Assert.Equal("Main", t);
            Assert.Empty(problems);
        }

        [Fact]
        public void 表解析_严格模式_坏key整表拒绝()
        {
            var serializer = new TestJsonSerializer();
            const string bad = @"[{""key"":""BadKey"",""en"":""x""}]";
            Assert.Null(LocalizationTable.Parse(bad, serializer, strictKeyNaming: true, out var problems));
            Assert.NotEmpty(problems);
        }

        [Fact]
        public void 表解析_宽松模式_坏key跳过并计数()
        {
            var serializer = new TestJsonSerializer();
            const string mixed = @"[{""key"":""BadKey"",""en"":""x""},{""key"":""UI.A"",""en"":""y""}]";
            LocalizationCatalog c = LocalizationTable.Parse(mixed, serializer, strictKeyNaming: false, out var problems);
            Assert.NotNull(c);
            Assert.Equal(1, c.KeyCount);
            Assert.NotEmpty(problems);
        }

        [Fact]
        public void 表解析_畸形或空返回null()
        {
            var serializer = new TestJsonSerializer();
            Assert.Null(LocalizationTable.Parse(null, serializer, false, out _));
            Assert.Null(LocalizationTable.Parse("", serializer, false, out _));
            Assert.Null(LocalizationTable.Parse("{ not json", serializer, false, out _));
            Assert.Null(LocalizationTable.Parse("[]", serializer, false, out _));
        }

        [Fact]
        public void 覆盖率_en缺哪些key()
        {
            // §9 本地化覆盖率：发布期据此判断翻译完整性
            var serializer = new TestJsonSerializer();
            LocalizationCatalog c = LocalizationTable.Parse(TableJson, serializer, true, out _);
            var missing = LocalizationTable.FindMissingFor(c, new[] { "UI.Main.Title", "Common.Ok" }, "en");
            Assert.Equal(new[] { "Common.Ok" }, missing);   // 只有回退源语言的那个
        }

        [Fact]
        public void 覆盖率_源语言无缺()
        {
            var serializer = new TestJsonSerializer();
            LocalizationCatalog c = LocalizationTable.Parse(TableJson, serializer, true, out _);
            Assert.Empty(LocalizationTable.FindMissingFor(c, new[] { "UI.Main.Title", "Common.Ok" }, "zh-CN"));
        }
    }
}
