using System;

namespace LiteSim
{
    /// <summary>
    /// Sim 数值基础件（LiteSim.Core，零依赖纯 C#）。
    ///
    /// 单位约定（§1-9）：长度 = 米(m)，时间 = 秒(s)，角度 = 弧度(rad)，
    /// y 轴 = 2.5D 向上（《状态同步实施方案》§3.5）；逻辑帧步长 <see cref="Dt"/> = 1/60（与 SimConfig 同源）。
    ///
    /// IEEE 边界纪律（《状态同步实施方案》§2.1；由 Editor 纪律扫描 R1~R3 执行）：
    ///   1. 禁超越函数（Sin/Cos/Tan/Atan2/Exp/Log/Pow）——一律走 SimTrig 查表；
    ///   2. 禁 FMA 显式写法（FusedMultiplyAdd）；
    ///   3. float 禁精度依赖比较（==/!=），一律用 <see cref="NearlyEqual"/>（绝对容差 1e-6）。
    /// 只使用 IEEE 基本运算（+ - * / 与 sqrt），保证 .NET 与 Unity 逐位一致。
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
        /// 平方根——**自研 software sqrt**：纯整数/位运算 + 正确舍入，
        /// 跨运行时逐位确定。
        ///
        /// 为什么不再用 BCL `<c>(float)Math.Sqrt</c>`：Unity(2022.3/Mono) 与 .NET 8 的
        /// <c>Math.Sqrt(double)</c> 实测存在 **ulp 级差异**（116 条逐值样本一致，但 10k 步运算链
        /// checksum 不同：.NET 2896875742 / Unity 3683559206）——被长链放大后直接抬高和解率。
        /// ≤ sqrt 属 IEEE 基本运算、要求正确舍入；"BCL 会给出唯一正确结果"的推论**在 Mono 上不成立**，
        /// 故与查表三角函数同策：**自己实现唯一实现**。
        ///
        /// 算法（结果与正确舍入的 IEEE binary32 sqrt 逐位一致，等价于主流 libm）：
        ///   ① 特例：+0/-0 → 原值；负数 → NaN；+∞/NaN → 原值；
        ///   ② 归一化取 S ∈ [2^23, 2^25) 与偶数 E，使 x = S·2^E（次正规移位归一化）；
        ///   ③ 取 shift（12 或 11）使 sqrt(S)·2^shift ∈ [2^23, 2^24)；
        ///   ④ 整数逐位开方求 V = ⌊sqrt(S<<(2·shift+2))⌋（V ≈ 2·M，多 1 位冗余）；
        ///   ⑤ 判进位：T &gt; V²+V ⟺ sqrt(T) ≥ V+0.5 → 进位（**精确比较，非 sticky**）；
        ///   ⑥ 组装指数：结果 = M·2^(E/2−shift)，溢出 → +∞。
        /// 全流程只用整数与位运算（+ - * / 与移位/比较）——无 BCL 数学调用，跨运行时必然一致。
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

            // ⑤ 正确舍入到 24 位（round-to-nearest-even）：
            //   设 u = sqrt(S)·2^shift，V = ⌊2u⌋。则 floor(u) = V>>1，小数部分 = (V&1)/2 + r'/2（r' = 2u−V）。
            //   V 偶 → 小数 ≤ 0.5−ε，恒不进位；V 奇 → 小数 ≥ 0.5，仅当 2u 恰为整数（T = V²，精确半值）
            //   且 M0 为偶时保留（tie→even），否则进位。
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

        // ─────────────────────────────────────────────────────────────────────
        // 融合安全算术
        //
        // **真因**：Mono(Unity 2022.3) 的 JIT 会把 `a*b + c*d` 之类**自动融合成 FMA**（单次舍入），
        // 而 .NET 8 严格按 IEEE 每步舍入 → 两侧差 1 ulp。实测证据（同一组分量、同一源码）：
        //   `x*x + y*y + z*z` → .NET 1075435771（逐步）/ Unity 1075435770（融合）；
        //   显式拆成局部变量**不能**阻止 Mono 融合（实测相同）。
        //
        // **修法**：多乘加一律**双精度累积 + 单次舍入**。依据：float×float 的积在 double 里**精确**
        // （24+24 ≤ 53 位），故任何融合都等于不融合 —— 融合敏感性被彻底消除；
        // 且两侧都变成"精确和 → 舍入一次"，语义唯一。**只用于中间累积，不落状态**（状态仍只 float + 整型）。
        // 纪律：Sim 内出现 `a*b + c*d`、`a*b + c`、`a*b - c*d` 一律改用下列件；纯 `+ - * /` 不受影响。
        // ─────────────────────────────────────────────────────────────────────

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
