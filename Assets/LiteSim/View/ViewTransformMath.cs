using System;

namespace LiteSim.View
{
    /// <summary>
    /// 表现变换数学（《联机战斗演示专项设计》§2.4 C3：插值/衰减抽成纯函数
    /// （ViewTransformMath，无引擎依赖）→ 进 Tests/LiteSim.Core.Tests（L1 覆盖），避免"表现数学
    /// 只能靠肉眼验"）：
    ///
    /// **本文件零引擎依赖**——只用 <see cref="SimVector3"/> 与 System.Math。它同时被
    /// `LiteSim.View`（Unity 运行时）与 `Tests/LiteSim.Core.Tests`（L1，经 csproj Link 编译）使用，
    /// 保持单一来源（同一份语义不可能在两侧漂移）。
    ///
    /// 纪律（《状态同步专项设计》§6.2 / 红线）：本类**只做表现换算**，不携带任何判定——
    /// 表现位置永远只是权威/预测状态的**呈现**，不回写 Sim。
    /// </summary>
    public static class ViewTransformMath
    {
        /// <summary>视点帧插值是否可用（快照间隔内的渲染插值）：alpha ∈ [0,1]。
        /// 帧轴推进由调用方（SimView）按 <see cref="SimConfig.InterpFrames"/> 折算，本类只做数学。</summary>
        public static SimVector3 Lerp(in SimVector3 from, in SimVector3 to, float alpha)
        {
            if (alpha <= 0f) return from;
            if (alpha >= 1f) return to;
            return new SimVector3(
                from.X + (to.X - from.X) * alpha,
                from.Y + (to.Y - from.Y) * alpha,
                from.Z + (to.Z - from.Z) * alpha);
        }

        /// <summary>
        /// 朝向最短弧插值（rad）。直接线性插值 yaw 会在 ±π 处绕远路（转身"打转"）——
        /// 先归一化到 (-π, π] 再做带符号最短差。
        /// </summary>
        public static float YawLerp(float from, float to, float alpha)
        {
            if (alpha <= 0f) return from;
            if (alpha >= 1f) return to;
            float delta = WrapPi(to - from);
            return from + delta * alpha;
        }

        /// <summary>把角度归一化到 (-π, π]。</summary>
        public static float WrapPi(float radians)
        {
            const float twoPi = (float)(Math.PI * 2.0);
            float r = (float)Math.IEEERemainder(radians, twoPi);   // (-π, π] 区间（IEEERemainder 语义）
            return r;
        }

        /// <summary>
        /// 和解误差衰减（《状态同步专项设计》§6.2"和解时本地误差衰减"）：把表现位置从
        /// <paramref name="from"/> 向 <paramref name="to"/> 收敛——每帧按剩余比例的指数衰减，
        /// 帧率无关（半衰期式）。<paramref name="sharpness"/> 越大收敛越快；≤0 = 不动（保持 from）。
        ///
        /// 用 <c>1 - exp(-sharpness*dt)</c> 而非固定系数：抖动只与真实经过时间相关，
        /// 掉帧/变帧率不改变收敛手感（§6.2"本地修正不微抖"的实现口径）。
        /// </summary>
        public static SimVector3 Decay(in SimVector3 from, in SimVector3 to, float sharpness, float deltaSeconds)
        {
            if (sharpness <= 0f || deltaSeconds <= 0f) return from;
            float alpha = 1f - (float)Math.Exp(-sharpness * deltaSeconds);
            if (alpha >= 1f) return to;
            return Lerp(from, to, alpha);
        }

        /// <summary>衰减用的朝向收敛（与 <see cref="Decay"/> 同口径，取最短弧）。</summary>
        public static float YawDecay(float from, float to, float sharpness, float deltaSeconds)
        {
            if (sharpness <= 0f || deltaSeconds <= 0f) return from;
            float alpha = 1f - (float)Math.Exp(-sharpness * deltaSeconds);
            if (alpha >= 1f) return to;
            return YawLerp(from, to, alpha);
        }

        /// <summary>
        /// 硬切判据（《状态同步专项设计》§6.2"复活/传送允许硬切"）：剩余误差超过
        /// <paramref name="snapDistance"/> 时不再衰减而是瞬时对齐——衰减追不上大幅偏差，
        /// 拖尾观感差且长时间偏离权威位置。
        /// </summary>
        public static bool ShouldSnap(in SimVector3 from, in SimVector3 to, float snapDistance)
        {
            if (snapDistance <= 0f) return false;
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float dz = to.Z - from.Z;
            return dx * dx + dy * dy + dz * dz > snapDistance * snapDistance;
        }

        /// <summary>平面（XZ）距离——俯视角相机夹取/吸附判据用；Y 不参与。</summary>
        public static float PlanarDistance(in SimVector3 a, in SimVector3 b)
        {
            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
