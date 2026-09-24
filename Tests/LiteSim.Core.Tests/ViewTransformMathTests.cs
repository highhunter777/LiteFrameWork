using System;
using LiteSim;
using LiteSim.View;
using Xunit;

namespace LiteSim.Core.Tests
{
    /// <summary>
    /// 表现变换纯函数验收（《联机战斗演示专项设计》§2.4 C3 / 《M11实施指导》"插值/衰减抽成纯函数
    /// （ViewTransformMath，无引擎依赖）→ 进 Tests/LiteSim.Core.Tests（L1 覆盖）"）：
    /// 这些数学过去只能靠肉眼验证，现按 L1 钉死——插值端点与单调性、yaw 最短弧、衰减的帧率无关性与
    /// 收敛性、硬切判据边界、平面距离。
    /// </summary>
    public sealed class ViewTransformMathTests
    {
        private static SimVector3 V(float x, float y, float z) => new SimVector3(x, y, z);

        // ---- Lerp ----

        [Fact]
        public void Lerp_端点与中点()
        {
            var a = V(0f, 0f, 0f);
            var b = V(10f, 20f, -30f);

            Assert.Equal(a, ViewTransformMath.Lerp(a, b, 0f));
            Assert.Equal(b, ViewTransformMath.Lerp(a, b, 1f));

            var mid = ViewTransformMath.Lerp(a, b, 0.5f);
            Assert.Equal(5f, mid.X, 5);
            Assert.Equal(10f, mid.Y, 5);
            Assert.Equal(-15f, mid.Z, 5);
        }

        [Fact]
        public void Lerp_越界alpha被夹取_不外推()
        {
            var a = V(0f, 0f, 0f);
            var b = V(1f, 0f, 0f);

            Assert.Equal(a, ViewTransformMath.Lerp(a, b, -5f), new SimVector3Comparer());
            Assert.Equal(b, ViewTransformMath.Lerp(a, b, 7f), new SimVector3Comparer());
        }

        [Fact]
        public void Lerp_单调推进()
        {
            var a = V(0f, 0f, 0f);
            var b = V(100f, 0f, 0f);

            float prev = -1f;
            for (int i = 0; i <= 10; i++)
            {
                float x = ViewTransformMath.Lerp(a, b, i / 10f).X;
                Assert.True(x >= prev, "插值必须单调（不倒退）");
                prev = x;
            }
        }

        // ---- YawLerp / WrapPi ----

        [Fact]
        public void WrapPi_归一化到负派到派()
        {
            Assert.Equal(0f, ViewTransformMath.WrapPi(0f), 5);
            Assert.Equal(0f, ViewTransformMath.WrapPi((float)(Math.PI * 2)), 5);

            // 3π/2 等价于 -π/2（绕远路的最小表示）
            Assert.Equal((float)(-Math.PI / 2), ViewTransformMath.WrapPi((float)(Math.PI * 1.5)), 5);

            float w = ViewTransformMath.WrapPi((float)(Math.PI * 7.25));
            Assert.True(w > -Math.PI - 1e-4 && w <= Math.PI + 1e-4, "归一化结果必须落在 (-π, π]");
        }

        [Fact]
        public void YawLerp_跨正负派走最短弧_不绕远路()
        {
            // 从 170° 到 -170°：最短弧是跨 π（+20°），不是反向绕 340°
            float from = (float)(Math.PI * 170.0 / 180.0);
            float to = (float)(-Math.PI * 170.0 / 180.0);

            float mid = ViewTransformMath.YawLerp(from, to, 0.5f);
            // 中点应落在 ±180° 附近（|mid| ≈ π），而非 0°（绕远路的中点）
            Assert.True(Math.Abs(mid) > 3.0f, $"跨 π 插值中点应贴近 ±π，实得 {mid}");
        }

        [Fact]
        public void YawLerp_端点保持()
        {
            Assert.Equal(1f, ViewTransformMath.YawLerp(1f, 2f, 0f), 5);
            Assert.Equal(2f, ViewTransformMath.YawLerp(1f, 2f, 1f), 5);
        }

        // ---- Decay ----

        [Fact]
        public void Decay_单调向目标收敛_不越界()
        {
            var from = V(0f, 0f, 0f);
            var to = V(10f, 0f, 0f);

            float prev = from.X;
            for (int i = 0; i < 60; i++)
            {
                var cur = ViewTransformMath.Decay(from, to, sharpness: 8f, deltaSeconds: 1f / 60f);
                from = cur;
                Assert.True(cur.X >= prev, "衰减必须单调靠近目标");
                Assert.True(cur.X <= 10f + 1e-4, "衰减不得越过目标（无过冲）");
                prev = cur.X;
            }
            Assert.True(prev > 9f, $"1 秒后应基本收敛，实得 {prev}");
        }

        [Fact]
        public void Decay_帧率无关_两步小帧约等于一步大帧()
        {
            var from = V(0f, 0f, 0f);
            var to = V(10f, 0f, 0f);

            var oneBig = ViewTransformMath.Decay(from, to, 5f, 0.1f);

            var step = ViewTransformMath.Decay(from, to, 5f, 0.05f);
            var twoSmall = ViewTransformMath.Decay(step, to, 5f, 0.05f);

            // 指数衰减的合成性：两次半程 = 一次全程（1-e^-0.25 ≈ 0.2212 两次合成 vs 1-e^-0.5）
            Assert.Equal(oneBig.X, twoSmall.X, 3);
        }

        [Fact]
        public void Decay_无时间或无衰减系数_原地不动()
        {
            var from = V(1f, 2f, 3f);
            var to = V(9f, 9f, 9f);

            Assert.Equal(from, ViewTransformMath.Decay(from, to, 0f, 1f), new SimVector3Comparer());
            Assert.Equal(from, ViewTransformMath.Decay(from, to, 5f, 0f), new SimVector3Comparer());
            Assert.Equal(from, ViewTransformMath.Decay(from, to, -1f, 1f), new SimVector3Comparer());
        }

        [Fact]
        public void YawDecay_跨派收敛走最短弧()
        {
            float from = 3.0f;                       // 接近 +π
            float to = -3.0f;                        // 接近 -π（最短弧跨 π，距离约 0.28）
            float result = ViewTransformMath.YawDecay(from, to, sharpness: 10f, deltaSeconds: 0.1f);

            float wrapped = ViewTransformMath.WrapPi(result);
            Assert.True(Math.Abs(wrapped) > 3.0f, $"应向 ±π 侧收敛，实得 {wrapped}");
        }

        // ---- ShouldSnap ----

        [Fact]
        public void ShouldSnap_超距硬切_界内不切()
        {
            var from = V(0f, 0f, 0f);

            Assert.False(ViewTransformMath.ShouldSnap(from, V(0.9f, 0f, 0f), 1f), "界内不硬切");
            Assert.True(ViewTransformMath.ShouldSnap(from, V(1.1f, 0f, 0f), 1f), "超距硬切");
            Assert.False(ViewTransformMath.ShouldSnap(from, V(1f, 0f, 0f), 1f), "恰好等于阈值不切（严格大于）");
        }

        [Fact]
        public void ShouldSnap_三维距离_含Y轴()
        {
            var from = V(0f, 0f, 0f);
            Assert.True(ViewTransformMath.ShouldSnap(from, V(0f, 5f, 0f), 1f), "Y 轴偏差同样触发硬切（复活/传送）");
        }

        [Fact]
        public void ShouldSnap_阈值非正_永不硬切()
        {
            var from = V(0f, 0f, 0f);
            Assert.False(ViewTransformMath.ShouldSnap(from, V(1000f, 0f, 0f), 0f));
            Assert.False(ViewTransformMath.ShouldSnap(from, V(1000f, 0f, 0f), -5f));
        }

        // ---- PlanarDistance ----

        [Fact]
        public void PlanarDistance_忽略Y轴()
        {
            Assert.Equal(5f, ViewTransformMath.PlanarDistance(V(0f, 100f, 0f), V(3f, -100f, 4f)), 4);
            Assert.Equal(0f, ViewTransformMath.PlanarDistance(V(1f, 5f, 2f), V(1f, -9f, 2f)), 4);
        }

        /// <summary>SimVector3 未实现 IEquatable——逐分量比较器（xunit 默认按字段反射亦可，此处显式以便诊断）。</summary>
        private sealed class SimVector3Comparer : System.Collections.Generic.IEqualityComparer<SimVector3>
        {
            public bool Equals(SimVector3 a, SimVector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
            public int GetHashCode(SimVector3 v) => v.X.GetHashCode() ^ (v.Y.GetHashCode() << 2) ^ (v.Z.GetHashCode() >> 2);
        }
    }
}
