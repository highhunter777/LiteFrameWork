using System;
using System.Globalization;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 战斗配置摘要用例（《商业级通用服务端框架总设计》§5 P0-5：configHash 必须是规范化 SHA-256——
    /// 跨进程一致、文化无关、随配置文本变化；禁止 GetHashCode 进入协议）。
    /// 纪律：**不改写全局 CombatConfig 装载状态**——测试类并行跑，改静态值会与基线/校验用例竞态；
    /// 配置变化语义经文本级摘要入口（<see cref="CombatConfigDigest.Compute(string)"/>）验证。
    /// </summary>
    public sealed class CombatConfigDigestTests
    {
        [Fact]
        public void 摘要_同值跨调用一致_非零()
        {
            uint a = CombatConfigDigest.Compute(CombatValues.Default);
            uint b = CombatConfigDigest.Compute(CombatValues.Default);
            Assert.NotEqual(0u, a);
            Assert.Equal(a, b);                            // 同一装载值 → 同一摘要（无进程随机种子——跨进程亦然）
        }

        [Fact]
        public void 摘要_配置文本变化必变_同文本必同()
        {
            // 与 CanonicalText 同规的两份文本（字段序/分隔不变，仅数值不同）→ 摘要必不同
            string textA = "5\n-20\n100\n0.5\n2\n25\n1\n100\n";
            string textB = "7\n-25\n120\n0.6\n2.5\n30\n2\n200\n";
            uint a1 = CombatConfigDigest.Compute(textA);
            uint a2 = CombatConfigDigest.Compute(textA);
            uint b = CombatConfigDigest.Compute(textB);

            Assert.Equal(a1, a2);                          // 同文本 → 同摘要（确定性）
            Assert.NotEqual(a1, b);                        // 异文本 → 异摘要（混房客户端当场可判）

            // 单字段变化也必变（不靠整行替换）
            Assert.NotEqual(a1, CombatConfigDigest.Compute("5\n-20\n100\n0.5\n2\n26\n1\n100\n"));

            // 空白/换行差异也必变（规范化严格——格式漂移不能蒙混过版本检查）
            Assert.NotEqual(a1, CombatConfigDigest.Compute("5 -20 100 0.5 2 25 1 100\n"));
        }

        [Fact]
        public void 规范化文本_与装载值绑定_文化无关()
        {
            string text = CombatConfigDigest.CanonicalText(CombatValues.Default);

            // 绑定：文本逐字段含当前装载值（InvariantCulture "R"/整型——配置装载面的完整快照）
            Assert.Contains(CombatConfig.MoveSpeed.ToString("R", CultureInfo.InvariantCulture), text);
            Assert.Contains(CombatConfig.Gravity.ToString("R", CultureInfo.InvariantCulture), text);
            Assert.Contains(CombatConfig.BaseDamage.ToString(CultureInfo.InvariantCulture), text);
            Assert.Contains(CombatConfig.EntityHp.ToString(CultureInfo.InvariantCulture), text);

            // 文化无关：小数点恒为句点（de-DE 的逗号绝不能进入摘要）
            Assert.DoesNotContain(",", text);
            Assert.DoesNotContain(" ", text);

            // 换线程文化实测（ICU 不可用的极端环境由上面的文本断言兜底，不失败）
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal(text, CombatConfigDigest.CanonicalText(CombatValues.Default));
            }
            catch (CultureNotFoundException) { }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }
    }
}
