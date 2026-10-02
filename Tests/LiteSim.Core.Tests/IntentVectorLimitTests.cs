using System;
using Xunit;
using LiteClient;

namespace LiteSim.Tests
{
    /// <summary>
    /// 意图向量闸门边界守卫（<see cref="IntentVectorLimit"/>）的 L1 覆盖。
    ///
    /// 钉的契约：设备源归一化后的浮点舍入可能把长度平方落在 1+1ulp——服务器 <c>InputGate</c> 按
    /// "长度平方 &gt; 1f 整帧拒收"（R0-P0-3 数值边界），落在 +1ulp 一侧即每帧被拒、权威端按空输入
    /// 执行（"移不动/橡皮筋"的根因）。守卫只收贴限舍入噪声、不改写界内值、不掩盖大幅越界
    /// （那是设备源实现缺陷，应由闸门拒收暴露）。
    /// </summary>
    public class IntentVectorLimitTests
    {
        /// <summary>与 InputGate.Store 同式同序的闸门判据（长度平方，无超越函数）。</summary>
        private static float LengthSquared(float x, float z) => x * x + z * z;

        [Fact]
        public void 界内值_逐位不动()
        {
            float x = 0.3f, z = 0.4f;                    // 模拟量的部分偏移是真实输入，不得改写
            IntentVectorLimit.EnsureWithinLengthLimit(ref x, ref z);
            Assert.Equal(0.3f, x);
            Assert.Equal(0.4f, z);
        }

        [Fact]
        public void 零向量_安全且不动()
        {
            float x = 0f, z = 0f;
            IntentVectorLimit.EnsureWithinLengthLimit(ref x, ref z);
            Assert.Equal(0f, x);
            Assert.Equal(0f, z);
        }

        [Fact]
        public void 现场实锤向量_归一化舍入越界_收缩后过闸且幅度几乎不变()
        {
            // 编辑器实采：aim 归一化输出 (0.9973691, -0.0724914148)，长度平方 = 1.00000012——
            // 服务器闸门按 >1 整帧拒收，移动输入随之全灭（"移不动"根因的实锤样本）
            float x = 0.9973691f, z = -0.0724914148f;
            Assert.True(LengthSquared(x, z) > 1f);        // 前置：该值确实越界（守卫存在的理由）
            IntentVectorLimit.EnsureWithinLengthLimit(ref x, ref z);
            Assert.True(LengthSquared(x, z) <= 1f);
            Assert.True(LengthSquared(x, z) > 0.998f);    // 方向量语义：幅度收缩在 1e-6 级，不可观
        }

        [Fact]
        public void 全向扫描_单位方向对的舍入越界_归一化加守卫后一律过闸()
        {
            // cos/sin 的 float 对本身就有若干方向长度平方 > 1（相机 yaw 旋转后的单键移动同构）；
            // 设备源"归一化 + 守卫"两步之后必须全部落在限内——任何一个角度都不得再被闸门拒收。
            for (int i = 0; i < 720; i++)
            {
                double a = i * Math.PI / 360.0;
                float x = (float)Math.Cos(a), z = (float)Math.Sin(a);
                float m2 = LengthSquared(x, z);
                if (m2 > 0.000001f)
                {
                    float inv = 1f / (float)Math.Sqrt(m2);
                    x *= inv;
                    z *= inv;                             // 设备源的归一化（NewInputIntentSource 同式）
                }
                IntentVectorLimit.EnsureWithinLengthLimit(ref x, ref z);
                Assert.True(LengthSquared(x, z) <= 1f, $"angle={i} len2={LengthSquared(x, z):R}");
            }
        }

        [Fact]
        public void 大幅越界_不掩盖_仍留闸门拒收()
        {
            // 守卫不是归一化器：设备源产出 (3,4) 这类实现缺陷必须由闸门拒收暴露，不许被静默"修好"
            float x = 3f, z = 4f;
            IntentVectorLimit.EnsureWithinLengthLimit(ref x, ref z);
            Assert.True(LengthSquared(x, z) > 1f);
        }
    }
}
