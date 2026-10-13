using System;
using System.Globalization;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 玩法数据身份摘要用例（《商业级通用服务端框架总设计》§5 P0-5：configHash 必须是规范化 SHA-256——
    /// 跨进程一致、文化无关、随输入变化；禁止 GetHashCode 进入协议。
    ///
    /// **现行口径**：摘要输入 = 判定面**表字节**（固定顺序 + 逐表内容 SHA-256）
    /// 与本局规则位——不再是 <see cref="CombatValues"/> 的投影字段；"表一字节改动即身份变"由本组钉住。
    /// 纪律：**不改写全局 CombatConfig 装载状态**——测试类并行跑，改静态值与基线用例竞态；
    /// 输入敏感性的验证走字节级入口（不构造真表）。
    /// </summary>
    public sealed class CombatConfigDigestTests
    {
        private static CombatConfigDigest.Entry[] Table(string path, params byte[] bytes)
            => new[] { new CombatConfigDigest.Entry(path, bytes) };

        [Fact]
        public void 基摘要_同字节跨调用一致_非零()
        {
            var a = Table("Combat/tbcombatnum", 1, 2, 3);
            var b = Table("Combat/tbcombatnum", 1, 2, 3);
            uint x = CombatConfigDigest.ComputeBase(a);
            uint y = CombatConfigDigest.ComputeBase(b);
            Assert.NotEqual(0u, x);
            Assert.Equal(x, y);                            // 同一字节 → 同一摘要（无进程随机种子——跨进程亦然）
        }

        [Fact]
        public void 基摘要_表字节单字节变化必变()
        {
            uint a = CombatConfigDigest.ComputeBase(Table("Combat/tbweapon", 1, 2, 3));
            uint b = CombatConfigDigest.ComputeBase(Table("Combat/tbweapon", 1, 2, 4));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void 基摘要_表路径与顺序参与身份()
        {
            uint one = CombatConfigDigest.ComputeBase(Table("Combat/tbweapon", 7));
            uint renamed = CombatConfigDigest.ComputeBase(Table("Combat/tbcombatnum", 7));
            Assert.NotEqual(one, renamed);                 // 同字节不同表 → 异摘要（表身份不可互换）

            var forward = new[]
            {
                new CombatConfigDigest.Entry("Combat/tbcombatnum", new byte[] { 1 }),
                new CombatConfigDigest.Entry("Combat/tbweapon", new byte[] { 2 }),
            };
            var reversed = new[]
            {
                new CombatConfigDigest.Entry("Combat/tbweapon", new byte[] { 2 }),
                new CombatConfigDigest.Entry("Combat/tbcombatnum", new byte[] { 1 }),
            };
            Assert.NotEqual(CombatConfigDigest.ComputeBase(forward), CombatConfigDigest.ComputeBase(reversed));
        }

        [Fact]
        public void 基摘要_空表集是合法形态_且异于任何真表集()
        {
            // 测试/嵌入式形态（无表身份）出确定值；生产两端恒为真表集（装载链），不可能撞到该形态。
            uint empty = CombatConfigDigest.ComputeBase(Array.Empty<CombatConfigDigest.Entry>());
            uint real = CombatConfigDigest.ComputeBase(Table("Combat/tbcombatnum", 1));
            Assert.NotEqual(empty, real);
        }

        [Fact]
        public void 规范文本_含标签与逐表内容摘要_文化无关()
        {
            string text = CombatConfigDigest.CanonicalBaseText(Table("Item/tbitemconfig", 0xAB));
            Assert.StartsWith(CombatConfigDigest.BaseSchemeTag, text, StringComparison.Ordinal);
            Assert.Contains("Item/tbitemconfig ", text, StringComparison.Ordinal);
            Assert.Equal(64, CombatConfigDigest.ContentHash(new byte[] { 0xAB }).Length);   // SHA-256 全量 hex

            // 文化无关：文本只由表路径 + hex 构成（de-DE 的小数逗号等绝不能进入摘要）
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal(text, CombatConfigDigest.CanonicalBaseText(Table("Item/tbitemconfig", 0xAB)));
            }
            catch (CultureNotFoundException) { }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Fact]
        public void 文本级入口_异文本异摘要_同文本同摘要()
        {
            uint a1 = CombatConfigDigest.Compute("gameplay-digest-v1\nCombat/tbweapon 00\n");
            uint a2 = CombatConfigDigest.Compute("gameplay-digest-v1\nCombat/tbweapon 00\n");
            uint b = CombatConfigDigest.Compute("gameplay-digest-v1\nCombat/tbweapon 01\n");
            Assert.Equal(a1, a2);
            Assert.NotEqual(a1, b);
        }

        [Fact]
        public void 终值_随本局规则位区分_确定性()
        {
            const uint Base = 0x0BADF00Du;
            uint plain = CombatConfigDigest.WithTestRules(Base, false, false);
            Assert.Equal(plain, CombatConfigDigest.WithTestRules(Base, false, false));      // 确定性
            Assert.NotEqual(plain, CombatConfigDigest.WithTestRules(Base, true, false));    // 免死（测试房身份）
            Assert.NotEqual(plain, CombatConfigDigest.WithTestRules(Base, false, true));    // 无限子弹
            Assert.NotEqual(plain, CombatConfigDigest.WithTestRules(Base ^ 1u, false, false));   // 基摘要参与终值
            Assert.Contains(CombatConfigDigest.RulesSchemeTag,
                CombatConfigDigest.CanonicalRulesText(Base, false, false), StringComparison.Ordinal);
        }
    }
}
