using System;

namespace LiteSim
{
    /// <summary>
    /// Sim 三角函数——查表 + 归约 + 线性插值（《M7 实施指导》§2.2）。
    ///
    /// 单位：弧度（与 SimMath 一致）。
    /// 表：<see cref="SimTrigTables.SinTable"/> 4096 项覆盖 [0, π/2]，线性插值。
    ///   - Sin/Cos：象限归约到 [0, π/2] 后查表；Cos(x) = Sin(x + π/2)。
    ///   - Atan2：八分圆（octant）归约 → 反查同一张表 → 线性插值（不引入 CORDIC，不复用第二张表）。
    ///   - Tan = Sin/Cos（分母容差保护）。
    /// 运行期严禁 Math./MathF. 超越函数：本文件只出现 IEEE 基本运算（+ - * / 与 SimMath.Sqrt）。
    /// 归约与插值只用加减法、比较、整型转换——跨运行时逐位确定。
    ///
    /// 生成物 SimTrigTables.cs 由 Editor 菜单「LiteSim/生成 SimTrig 查表」产出（勿手改）。
    /// </summary>
    public static class SimTrig
    {
        /// <summary>查表长度。</summary>
        public const int TableSize = 4096;

        // 角度常量（硬编码 float 字面量，运行期不调用 Math.PI）——生成器与运行期必须一致。
        public const float Pi = 3.14159265f;
        public const float TwoPi = 6.28318531f;
        public const float HalfPi = 1.57079633f;
        public const float QuarterPi = 0.78539816f;

        /// <summary>角度 → 表位置的比例（(TableSize-1) / (π/2)）。</summary>
        private const float AngleToTable = (TableSize - 1) / HalfPi;

        /// <summary>每个表格对应的角度（(π/2) / (TableSize-1)）。</summary>
        private const float TableStepAngle = HalfPi / (TableSize - 1);

        private const float InvTwoPi = 1f / TwoPi;

        /// <summary>正弦。NaN/Inf 输入返回 0（边界保护）。</summary>
        public static float Sin(float radians)
        {
            if (!IsFinite(radians)) return 0f;

            float r = radians - TwoPi * FloorToFloat(radians * InvTwoPi);
            if (r < 0f) r += TwoPi;

            if (r <= HalfPi) return SinQuadrant(r);
            if (r <= Pi) return SinQuadrant(Pi - r);
            if (r <= Pi + HalfPi) return -SinQuadrant(r - Pi);
            return -SinQuadrant(TwoPi - r);
        }

        /// <summary>余弦。Cos(x) = Sin(x + π/2)。</summary>
        public static float Cos(float radians)
        {
            return Sin(radians + HalfPi);
        }

        /// <summary>正切 = Sin/Cos；分母进入容差带时按容差钳制，避免 Inf/NaN 扩散。</summary>
        public static float Tan(float radians)
        {
            float s = Sin(radians);
            float c = Cos(radians);
            if (c >= 0f && c < SimMath.Tolerance) c = SimMath.Tolerance;
            else if (c < 0f && c > -SimMath.Tolerance) c = -SimMath.Tolerance;
            return s / c;
        }

        /// <summary>
        /// 反正切（两参数）。八分圆归约到第一八分圆 [0, π/4] → 反查 sin 表 → 插值 → 按象限回填。
        /// atan2(0,0) 定义为 0；NaN/Inf 输入返回 0。
        /// </summary>
        public static float Atan2(float y, float x)
        {
            if (!IsFinite(y) || !IsFinite(x)) return 0f;

            // atan2(0,0) 无定义，约定返回 0——与常量 0 的明确比较，豁免 R3（不适用 NearlyEqual）。
            if (x == 0f && y == 0f) return 0f; // lint-allow R3

            float ax = SimMath.Abs(x);
            float ay = SimMath.Abs(y);

            float theta;
            if (ax >= ay)
            {
                // 第一/第四八分圆：t = |y|/|x| ∈ [0,1]，角度 = atan(t) ∈ [0, π/4]
                theta = AtanFirstOctant(ay / ax);
            }
            else
            {
                // 第二/第三八分圆：t = |x|/|y| ∈ (0,1]，角度 = π/2 - atan(t) ∈ (π/4, π/2)
                theta = HalfPi - AtanFirstOctant(ax / ay);
            }

            if (x < 0f) theta = Pi - theta;
            if (y < 0f) theta = -theta;
            return theta;
        }

        // ---- 内部：查表与归约（全部 IEEE 基本运算） ----

        /// <summary>角度 a ∈ [0, π/2] → SinTable 线性插值。</summary>
        private static float SinQuadrant(float a)
        {
            float pos = a * AngleToTable;
            int i = (int)pos;
            if (i < 0) return SinTableAt(0);
            if (i >= TableSize - 1) return SinTableAt(TableSize - 1);
            float frac = pos - (float)i;
            float v0 = SinTableAt(i);
            float v1 = SinTableAt(i + 1);
            return v0 + (v1 - v0) * frac;
        }

        /// <summary>第一八分圆 atan：t ∈ [0,1] → [0, π/4]。用 sin(atan(t)) = t/√(1+t²) 反查表。</summary>
        private static float AtanFirstOctant(float t)
        {
            if (t <= 0f) return 0f;
            float s = t / SimMath.Sqrt(1f + t * t); // = sin(atan(t)) ∈ [0, 1/√2]
            return AsinViaTable(s);
        }

        /// <summary>反查 sin 表求 asin(s)：s ∈ [0,1] → [0, π/2]。二分找格 + 线性插值（表单调递增）。</summary>
        private static float AsinViaTable(float s)
        {
            if (s <= 0f) return 0f;
            if (s >= 1f) return HalfPi;

            int lo = 0;
            int hi = TableSize - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (SinTableAt(mid) < s) lo = mid + 1;
                else hi = mid;
            }

            int i1 = lo;
            if (i1 <= 0) return 0f;
            int i0 = i1 - 1;
            float v0 = SinTableAt(i0);
            float v1 = SinTableAt(i1);
            float frac = (v1 > v0) ? (s - v0) / (v1 - v0) : 0f;
            return ((float)i0 + frac) * TableStepAngle;
        }

        private static float SinTableAt(int index)
        {
            return SimTrigTables.SinTable[index];
        }

        /// <summary>向下取整（只用 IEEE 基本运算与整型转换；适用 |x| 远小于 2^63）。</summary>
        private static float FloorToFloat(float x)
        {
            long i = (long)x;            // 截断向零
            if ((float)i > x) i -= 1;    // 负数非整数时补一格 → 向下取整（用 > 而非 ==，规避 R3）
            return (float)i;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
