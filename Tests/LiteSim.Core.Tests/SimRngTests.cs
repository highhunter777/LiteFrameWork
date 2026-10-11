using System;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>SimRng 契约（《M7 实施指导》§2.4 用例）：固定种子回归锚点 + 边界 + 重现性。</summary>
    public class SimRngTests
    {
        // 回归锚点：seed=12345 的前 8 个值（.NET 侧记录，跨运行时须逐位一致）。
        private static readonly uint[] AnchorU32 =
        {
            2555902770u, 3234773579u, 328846939u, 3161420795u,
            513335584u, 904356694u, 4293856061u, 2283851398u,
        };

        private static readonly float[] AnchorF01 =
        {
            0.595092475f, 0.753154397f, 0.0765656233f, 0.73607558f,
            0.119520247f, 0.210561931f, 0.999741256f, 0.53175056f,
        };

        [Fact]
        public void SimRng_NextUInt32_固定种子前8值与锚点一致()
        {
            var rng = new SimRng(12345UL);
            for (int i = 0; i < AnchorU32.Length; i++)
            {
                Assert.Equal(AnchorU32[i], rng.NextUInt32());
            }
        }

        [Fact]
        public void SimRng_NextFloat01_固定种子前8值与锚点一致()
        {
            var rng = new SimRng(12345UL);
            for (int i = 0; i < AnchorF01.Length; i++)
            {
                Assert.Equal(AnchorF01[i], rng.NextFloat01());
            }
        }

        [Fact]
        public void SimRng_同种子_序列一致()
        {
            var a = new SimRng(999UL);
            var b = new SimRng(999UL);
            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal(a.NextUInt32(), b.NextUInt32());
            }
        }

        [Fact]
        public void SimRng_零种子_改为非零且不锁死()
        {
            var rng = new SimRng(0UL);
            Assert.True(rng.State != 0UL);
            // 不应锁死：连续值有变化
            uint first = rng.NextUInt32();
            uint second = rng.NextUInt32();
            Assert.True(first != second);
        }

        [Fact]
        public void SimRng_NextFloat01_恒在零到一之间()
        {
            var rng = new SimRng(7UL);
            for (int i = 0; i < 100000; i++)
            {
                float v = rng.NextFloat01();
                Assert.True(v >= 0f && v < 1f, "NextFloat01 越界: " + v);
            }
        }

        [Fact]
        public void SimRng_NextRange整数_不越界且空区间返回min()
        {
            var rng = new SimRng(42UL);
            for (int i = 0; i < 100000; i++)
            {
                int v = rng.NextRange(10, 20);
                Assert.True(v >= 10 && v < 20, "NextRange 越界: " + v);
            }
            Assert.Equal(5, rng.NextRange(5, 5)); // 空区间 → min
            Assert.Equal(5, rng.NextRange(5, 4));
        }

        [Fact]
        public void SimRng_NextRange_分桶粗检不偏斜()
        {
            // 仅粗检：分桶计数不应严重偏斜（不做统计强断言）。
            var rng = new SimRng(2024UL);
            int buckets = 10;
            int[] counts = new int[buckets];
            int n = 100000;
            for (int i = 0; i < n; i++) counts[rng.NextRange(0, buckets)]++;

            int expected = n / buckets;
            for (int i = 0; i < buckets; i++)
            {
                Assert.True(counts[i] > expected / 2 && counts[i] < expected * 2,
                    "桶 " + i + " 计数异常: " + counts[i]);
            }
        }
    }
}
