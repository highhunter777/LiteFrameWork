using System;

namespace LiteSim
{
    /// <summary>
    /// Sim 数值基础件（LiteSim，零依赖纯 C#）。
    /// </summary>
    public static class SimMath
    {
        /// <summary>逻辑帧步长（秒）。</summary>
        public const float Dt = 1f / 60f;

        /// <summary>浮点比较绝对容差。</summary>
        public const float Tolerance = 1e-6f;

        /// <summary>绝对容差比较：|a - b| &lt;= <see cref="Tolerance"/>。</summary>
        public static bool NearlyEqual(float a, float b)
        {
            return Abs(a - b) <= Tolerance;
        }

        public static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static float Clamp01(float v)
        {
            return Clamp(v, 0f, 1f);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        public static float Abs(float v)
        {
            return v < 0f ? -v : v;
        }

        public static int Abs(int v)
        {
            return v < 0 ? -v : v;
        }

        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        /// <summary>
        /// 平方根——**自研 software sqrt**：纯整数/位运算 + 正确舍入， 跨运行时逐位确定。
        /// </summary>
        public static float Sqrt(float v)
        {
            int bits = BitConverter.SingleToInt32Bits(v);

            // ① 特例（顺序：NaN → 负 → 零 → +∞；NaN 须最先，否则被负数分支截走符号与载荷）
            int expField = (bits >> 23) & 0xFF;
            if (expField == 0xFF && (bits & 0x7FFFFF) != 0)                  // lint-allow R3（整数比较，非浮点）
                return BitConverter.Int32BitsToSingle(bits | 0x00400000);   // NaN（任意符号）→ 静默化（置 quiet 位）
            if (bits < 0)                                                   // 负号位置 1
            {
                if (bits == unchecked((int)0x80000000)) return v;            // lint-allow R3（整数比较）：-0（sqrt(-0) = -0）
                return float.NaN;                                            // 负数 / -∞ → NaN
            }
            if (bits == 0) return v;                                        // +0
            if (expField == 0xFF) return v;                                 // lint-allow R3（整数比较）：+∞

            // ② 归一化：x = S·2^E，S ∈ [2^23, 2^25)，E 偶（expField 已在 ① 取好）
            uint sig;
            int e;
            if (expField == 0)
            {
                sig = (uint)(bits & 0x7FFFFF);                          // 次正规
                int sh = 0;
                while ((sig & 0x800000u) == 0) { sig <<= 1; sh++; }     // 左移到 bit23（≤23 次，确定）
                e = -126 - sh;
            }
            else
            {
                sig = (uint)(bits & 0x7FFFFF) | 0x800000u;
                e = expField - 127;
            }

            ulong S = sig;
            int E = e - 23;
            if ((E & 1) != 0) { S <<= 1; E--; }                          // 使 E 为偶
            int halfE = E / 2;

            // ③ shift：S ∈ [2^23,2^24) → 12；S ∈ [2^24,2^25) → 11
            int shift = S < (1UL << 24) ? 12 : 11;

            // ④ V = ⌊sqrt(S·2^(2·shift+2))⌋（S<<26 < 2^51 ✓ ulong 足够）
            ulong T = S << (2 * shift + 2);
            ulong V = IntegerSqrt(T);

            // ⑤ 正确舍入到 24 位（round-to-nearest-even）： 设 u = sqrt(S)·2^shift，V = ⌊2u⌋。
            ulong M = V >> 1;                                           // 24 位尾数
            if ((V & 1) != 0)
            {
                bool exactTie = T == V * V;                                  // lint-allow R3（ulong 整数比较）
                if (!exactTie || (M & 1) != 0) M++;
            }
            if (M == (1UL << 24)) { M >>= 1; halfE++; }                 // 进位 → 规格化

            // ⑥ 组装：value = M·2^(halfE−shift)，M = 1.f·2^23 → 指数字段 = halfE−shift+150
            int resultExp = halfE - shift + 150;
            if (resultExp >= 255) return float.PositiveInfinity;        // 溢出（x 已排除 inf/NaN）
            if (resultExp <= 0) return 0f;                              // 理论不可达（次正规的 sqrt 仍为正规）

            uint resultBits = (uint)(resultExp << 23) | (uint)(M & 0x7FFFFFUL);
            return BitConverter.Int32BitsToSingle(unchecked((int)resultBits));
        }

        /// <summary>整数逐位开方（restoring 法）：只用 `+ - * 移位/比较`，确定且精确。</summary>
        private static ulong IntegerSqrt(ulong n)
        {
            ulong res = 0;
            ulong bit = 1UL << 62;                                       // 不超过 2^64 的最大 4 的幂
            while (bit > n) bit >>= 2;
            while (bit != 0)
            {
                if (n >= res + bit)
                {
                    n -= res + bit;
                    res = (res >> 1) + bit;
                }
                else
                {
                    res >>= 1;
                }
                bit >>= 2;
            }
            return res;
        }

        // ───────────────────────────────────────────────────────────────────── 融合安全算术 **真因**：Mono(Unity 2022.3) 的 JIT 会把 `a*b + c*d` 之类**自动融合成 FMA**（。）。

        /// <summary>融合安全：<c>a0*b0 + a1*b1</c>（双精度累积，单次舍入）。</summary>
        public static float MulAdd2(float a0, float b0, float a1, float b1)
        {
            return (float)((double)a0 * b0 + (double)a1 * b1);
        }

        /// <summary>融合安全：<c>a0*b0 - a1*b1</c>（叉乘分量用）。</summary>
        public static float MulSub2(float a0, float b0, float a1, float b1)
        {
            return (float)((double)a0 * b0 - (double)a1 * b1);
        }

        /// <summary>融合安全：<c>a0*b0 + a1*b1 + a2*b2</c>（点积 / 长度平方用）。</summary>
        public static float MulAdd3(float a0, float b0, float a1, float b1, float a2, float b2)
        {
            return (float)((double)a0 * b0 + (double)a1 * b1 + (double)a2 * b2);
        }

        /// <summary>融合安全：<c>a0*b0 + a1*b1 - a2*b2</c>（二次方程判别式类）。</summary>
        public static float MulAddSub3(float a0, float b0, float a1, float b1, float a2, float b2)
        {
            return (float)((double)a0 * b0 + (double)a1 * b1 - (double)a2 * b2);
        }

        /// <summary>融合安全：<c>a*b + c</c>。</summary>
        public static float MulAdd(float a, float b, float c)
        {
            return (float)((double)a * b + c);
        }

        /// <summary>融合安全：<c>a*b - c</c>。</summary>
        public static float MulSub(float a, float b, float c)
        {
            return (float)((double)a * b - c);
        }
    }
}

// rebuild-marker: forced-rebuild（变更文件尺寸，绕开编译器服务器缓存）
