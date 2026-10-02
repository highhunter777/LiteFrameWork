using System;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// `SimMath.Sqrt` 自研 software sqrt 的正确性（见《待办总览》§5-33）。
    ///
    /// 判据分两层：
    /// ① **正确性**——与 BCL `(float)Math.Sqrt`（IEEE 要求正确舍入，.NET 侧可信）**逐位一致**：
    ///    大量随机位型 + 幂次 + 次正规 + 完美平方及邻域（强制舍入边界）+ 特例。
    /// ② **跨运行时确定性**——纯整数/位运算无 BCL 数学调用；Unity 侧一致性由
    ///    `Assets/Tests/EditMode` 的 IEEE 基线对账（含 10k 步 Chain）与 `Baselines/SimChecksum.txt` 共同守住。
    /// </summary>
    public sealed class SimMathSqrtTests
    {
        [Fact]
        public void Sqrt_与BCL逐位一致_边界样本()
        {
            float[] samples =
            {
                0f, -0f, 1f, 2f, 4f, 0.25f, 0.5f, 3f, 1e-45f, 1.1754944e-38f, 3.4028235e38f,
                1e30f, 1e-30f, 123.456f, 0.1f, 0.2f, 0.3f, 0.7f, 1e5f,
                float.Epsilon, float.MaxValue, float.MinValue,
            };
            foreach (float v in samples) AssertSame(v);
        }

        [Fact]
        public void Sqrt_与BCL逐位一致_全指数范围幂次()
        {
            // 2^k：覆盖正规 / 次正规 / 极值；并测其 ±1ulp 邻域
            for (int k = -149; k <= 127; k++)
            {
                float v = Pow2(k);
                if (float.IsInfinity(v) || v <= 0f) continue;
                AssertSame(v);
                AssertSame(NextUp(v));
                AssertSame(PrevDown(v));
            }
        }

        [Fact]
        public void Sqrt_与BCL逐位一致_十万随机位型()
        {
            // 确定性伪随机（SimRng，避免依赖 Random 的实现差异）
            var rng = new SimRng(0xC0FFEE123456789UL);
            for (int i = 0; i < 100000; i++)
            {
                int bits = (int)rng.NextUInt32();              // 任意位型
                float v = BitConverter.Int32BitsToSingle(bits);
                AssertSame(v, $"随机位型 #{i} bits=0x{bits:X8}");
            }
        }

        [Fact]
        public void Sqrt_与BCL逐位一致_完美平方与邻域_强制舍入边界()
        {
            // m² 的 sqrt 恰为整数（无舍入）；m²±1 落在半数边界附近 → 逼出 round-to-nearest-even
            var rng = new SimRng(0x5EED5EED5EEDUL);
            for (int i = 0; i < 20000; i++)
            {
                uint m = (rng.NextUInt32() & 0x7FFFFF) | 0x800000;   // 24 位尾数域
                float mf = BitConverter.Int32BitsToSingle(unchecked((int)((127u << 23) | (m & 0x7FFFFF))));
                float sq = mf * mf;                                   // 精确方（fp 乘法可能舍入，仍产生边界样本）
                if (float.IsInfinity(sq) || sq <= 0f) continue;
                AssertSame(sq);
                AssertSame(NextUp(sq));
                AssertSame(PrevDown(sq));
            }
        }

        [Fact]
        public void Sqrt_特例_零与无穷与NaN与负数()
        {
            Assert.Equal(0f, SimMath.Sqrt(0f));
            Assert.True(float.IsNegative(SimMath.Sqrt(-0f)), "sqrt(-0) 应为 -0");
            Assert.True(float.IsPositiveInfinity(SimMath.Sqrt(float.PositiveInfinity)));
            Assert.True(float.IsNaN(SimMath.Sqrt(float.NegativeInfinity)));
            Assert.True(float.IsNaN(SimMath.Sqrt(-1f)));
            Assert.True(float.IsNaN(SimMath.Sqrt(float.MinValue)));
            Assert.True(float.IsNaN(SimMath.Sqrt(float.NaN)));
        }

        [Fact]
        public void Sqrt_纯确定_重复调用同值()
        {
            var rng = new SimRng(0x1234ABCDUL);
            for (int i = 0; i < 1000; i++)
            {
                float v = (rng.NextUInt32() & 0x7FFFFFFF) * 1e-3f;
                int a = BitConverter.SingleToInt32Bits(SimMath.Sqrt(v));
                int b = BitConverter.SingleToInt32Bits(SimMath.Sqrt(v));
                Assert.Equal(a, b);
            }
        }

        // ---- 辅助 ----

        private static void AssertSame(float v, string hint = null)
        {
            int expected = BitConverter.SingleToInt32Bits((float)Math.Sqrt(v));
            int actual = BitConverter.SingleToInt32Bits(SimMath.Sqrt(v));
            Assert.True(expected == actual,
                $"Sqrt 不一致{(hint == null ? "" : $" [{hint}]")}：v=0x{BitConverter.SingleToInt32Bits(v):X8}({v}) " +
                $"BCL=0x{expected:X8} 本端=0x{actual:X8}");
        }

        private static float Pow2(int k)
        {
            if (k >= -126) return BitConverter.Int32BitsToSingle((k + 127) << 23);
            // 次正规：2^-126 × 2^(k+126)
            return BitConverter.Int32BitsToSingle(1 << (k + 149));
        }

        private static float NextUp(float v) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v) + 1);

        private static float PrevDown(float v) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v) - 1);
    }
}
