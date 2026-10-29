using System;
using System.Globalization;

namespace LiteSim
{
    /// <summary>
    /// Sim 三维向量（x, y, z；y 轴 = 2.5D 向上）。
    ///
    /// 值类型（无装箱）；只用 IEEE 基本运算与 <see cref="SimMath.Sqrt"/>，跨运行时逐位确定。
    /// 语义与 UnityEngine.Vector3 对齐，便于 Sim 层与 View 层对照（但本程序集零引擎依赖）。
    /// </summary>
    public struct SimVector3
    {
        public float X;
        public float Y;
        public float Z;

        public SimVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly SimVector3 Zero = new SimVector3(0f, 0f, 0f);

        public static SimVector3 operator +(SimVector3 a, SimVector3 b)
        {
            return new SimVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static SimVector3 operator -(SimVector3 a, SimVector3 b)
        {
            return new SimVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static SimVector3 operator -(SimVector3 a)
        {
            return new SimVector3(-a.X, -a.Y, -a.Z);
        }

        public static SimVector3 operator *(SimVector3 a, float s)
        {
            return new SimVector3(a.X * s, a.Y * s, a.Z * s);
        }

        public static SimVector3 operator *(float s, SimVector3 a)
        {
            return new SimVector3(a.X * s, a.Y * s, a.Z * s);
        }

        public static SimVector3 operator /(SimVector3 a, float s)
        {
            return new SimVector3(a.X / s, a.Y / s, a.Z / s);
        }

        public static float Dot(SimVector3 a, SimVector3 b)
        {
            // 融合安全：`a.X*b.X + a.Y*b.Y + a.Z*b.Z` 是 Mono 自动 FMA 的典型形态（实测差 1 ulp）
            return SimMath.MulAdd3(a.X, b.X, a.Y, b.Y, a.Z, b.Z);
        }

        public static SimVector3 Cross(SimVector3 a, SimVector3 b)
        {
            // 融合安全（MulSub2/MulAddSub3 走双精度累积）：Mono 会把 `a*b - c*d` 自动融合成 FMA，.NET 不会
            return new SimVector3(
                SimMath.MulSub2(a.Y, b.Z, a.Z, b.Y),
                SimMath.MulSub2(a.Z, b.X, a.X, b.Z),
                SimMath.MulSub2(a.X, b.Y, a.Y, b.X));
        }

        public float LengthSquared
        {
            // 融合安全：`X*X + Y*Y + Z*Z` 是 Mono 自动 FMA 的典型形态（实测差 1 ulp，见 SimMath 注释）
            get { return SimMath.MulAdd3(X, X, Y, Y, Z, Z); }
        }

        public float Length
        {
            get { return SimMath.Sqrt(LengthSquared); }
        }

        public static float Distance(SimVector3 a, SimVector3 b)
        {
            return (a - b).Length;
        }

        /// <summary>单位化；长度小于容差时返回零向量（禁 NaN/Inf 扩散）。</summary>
        public SimVector3 Normalized()
        {
            float len = Length;
            if (len < SimMath.Tolerance) return Zero;
            float inv = 1f / len;
            return new SimVector3(X * inv, Y * inv, Z * inv);
        }

        public override string ToString()
        {
            return "(" + X.ToString("G9", CultureInfo.InvariantCulture)
                + ", " + Y.ToString("G9", CultureInfo.InvariantCulture)
                + ", " + Z.ToString("G9", CultureInfo.InvariantCulture) + ")";
        }
    }
}
