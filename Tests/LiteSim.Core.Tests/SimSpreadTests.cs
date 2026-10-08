using System;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 散布锥偏转（<see cref="SimSpread"/>）几何用例：纯几何件，随机源由调用方传入——
    /// 本组直接喂（偏转角, 方位角）验证：恒等性 / 锥角上界 / 单位性 / 竖直退化 / 逐位确定性。
    /// 与射击系统集成（随机消费口径/多弹丸）见 <see cref="SpreadShootingTests"/>。
    /// </summary>
    public sealed class SimSpreadTests
    {
        private static readonly SimMapData NoMap = new SimMapData();

        /// <summary>两向量夹角（弧度；测试侧可用 System.Math——Sim 机制侧才受 R1 纪律把守）。</summary>
        private static double AngleBetween(in SimVector3 a, in SimVector3 b)
        {
            double dot = (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
            double cross = Math.Sqrt(
                Math.Pow((double)a.Y * b.Z - (double)a.Z * b.Y, 2) +
                Math.Pow((double)a.Z * b.X - (double)a.X * b.Z, 2) +
                Math.Pow((double)a.X * b.Y - (double)a.Y * b.X, 2));
            return Math.Atan2(cross, dot);
        }

        [Fact]
        public void 零偏转角_恒等返回原方向()
        {
            var dir = new SimVector3(0.6f, 0.1f, 0.7938f);     // 非单位、非轴对齐——恒等必须位级
            var out0 = SimSpread.Deflect(in dir, 0f, 0f);
            Assert.Equal(dir.X, out0.X);
            Assert.Equal(dir.Y, out0.Y);
            Assert.Equal(dir.Z, out0.Z);
        }

        [Theory]
        [InlineData(0.0f)]        // 水平弹道（俯视角常态）
        [InlineData(0.5235988f)]  // 俯角 30°
        public void 锥角上界_偏转后与轴夹角不超偏转角(float pitch)
        {
            var dir = new SimVector3((float)Math.Cos(pitch), (float)-Math.Sin(pitch), 0f);
            const float Deflection = 0.15f;                    // ≈8.6°

            // 方位角全环扫：任何方位的偏转都落在锥内（夹角 = 偏转角，容差 = SimTrig 表插值精度）
            for (int i = 0; i < 32; i++)
            {
                float azimuth = i * (SimTrig.TwoPi / 32f);
                SimVector3 d = SimSpread.Deflect(in dir, Deflection, azimuth);
                double angle = AngleBetween(in dir, in d);
                Assert.True(angle <= Deflection + 1e-3, $"方位 {azimuth:F4} 偏转越锥：{angle:F6} > {Deflection}");
                Assert.True(angle >= Deflection - 1e-3, $"方位 {azimuth:F4} 偏转不足：{angle:F6}（锥面语义）");
            }
        }

        [Fact]
        public void 输出单位性_重归一后长度为1()
        {
            var rng = new SimRng(123UL);
            for (int i = 0; i < 100; i++)
            {
                float ax = rng.NextFloat01() * SimTrig.TwoPi;
                float ay = (rng.NextFloat01() - 0.5f) * 0.9f;
                float az = rng.NextFloat01() * SimTrig.TwoPi;
                float len = (float)Math.Sqrt((double)ax * ax + (double)ay * ay + (double)az * az);
                if (len < 1e-3f) continue;
                var dir = new SimVector3(ax / len, ay / len, az / len);

                SimVector3 d = SimSpread.Deflect(in dir, rng.NextFloat01() * 0.3f, rng.NextFloat01() * SimTrig.TwoPi);
                double l = Math.Sqrt((double)d.X * d.X + (double)d.Y * d.Y + (double)d.Z * d.Z);
                Assert.InRange(l, 1.0 - 1e-4, 1.0 + 1e-4);
            }
        }

        [Fact]
        public void 近竖直方向_退化参考轴不产出NaN()
        {
            var dir = new SimVector3(0f, 1f, 0f);
            SimVector3 d = SimSpread.Deflect(in dir, 0.2f, 0.7f);
            Assert.True(float.IsFinite(d.X) && float.IsFinite(d.Y) && float.IsFinite(d.Z));
            Assert.InRange(Math.Sqrt((double)d.X * d.X + (double)d.Y * d.Y + (double)d.Z * d.Z), 0.99, 1.01);

            // 正竖直反向同理
            var down = new SimVector3(0f, -1f, 0f);
            SimVector3 d2 = SimSpread.Deflect(in down, 0.1f, 2.5f);
            Assert.True(float.IsFinite(d2.X) && float.IsFinite(d2.Y) && float.IsFinite(d2.Z));
        }

        [Fact]
        public void 同输入逐位确定_异角必分岔()
        {
            var dir = new SimVector3(0.8f, -0.2f, 0.5f);
            var a1 = SimSpread.Deflect(in dir, 0.1f, 0.3f);
            var a2 = SimSpread.Deflect(in dir, 0.1f, 0.3f);
            Assert.Equal(a1.X, a2.X);
            Assert.Equal(a1.Y, a2.Y);
            Assert.Equal(a1.Z, a2.Z);

            SimVector3 b = SimSpread.Deflect(in dir, 0.1f, 0.9f);
            Assert.NotEqual(a1.X, b.X);   // 方位不同 ⇒ 方向必不同（统计面：随机方向命中不同象限）
        }

        [Fact]
        public void 无地图关联_纯函数无状态漂移()
        {
            // 连续调用不改输入（in 语义直证）；同参数重复调用稳定
            var dir = new SimVector3(0.7071f, 0f, 0.7071f);
            SimVector3 d = SimSpread.Deflect(in dir, 0.25f, 1.2f);
            Assert.Equal(0.7071f, dir.X);      // 输入未被就地修改
            Assert.Equal(0.7071f, dir.Z);
            Assert.Equal(d.X, SimSpread.Deflect(in dir, 0.25f, 1.2f).X);
            _ = NoMap;
        }
    }
}
