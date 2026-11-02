namespace LiteSim
{
    /// <summary>
    /// 散布锥偏转（射击判定的确定性弹道扰动单源——《游戏业务系统总设计》§5 散布）：
    /// 把瞄准方向在以其为轴的立体角锥内偏转。**随机数不在本类**——调用方（<see cref="ShootingSystem"/>）
    /// 从 RngState 消费后以（偏转角, 方位角）传入，本类只做纯几何（RngState 审计口径留在射击系统签名上）。
    ///
    /// - **偏转角** ∈ [0°, spread]（表值 = 瞄准态最大偏转角；腰射 = 表值 ×
    ///   <see cref="CombatConfig.HipSpreadFactor"/>——由调用方算好传入）；
    /// - **方位角** ∈ [0°, 360°)：偏转方向绕瞄准轴的均匀分布；
    /// - **正交基**：以瞄准方向为轴取 u = 归一(上轴 × dir)（水平闭式 (dz, 0, −dx)/h——俯视角弹道
    ///   恒非竖直，此式恒垂直于 dir 的水平投影且与 dir 正交），v = dir × u；
    ///   dir 近竖直（水平投影退化）时退用 x 轴参考轴；
    /// - **合成**：dir' = cos(a)·dir + sin(a)·(cos(φ)·u + sin(φ)·v)，再重归一
    ///   （SimTrig 查表插值使 cos²+sin² 带 ~1e-8 误差——归一后单位性保持，maxT 沿方向长度语义不变）；
    /// - **零偏转角 = 恒等**（返回原方向，不进合成路径——spread=0 的武器由调用方跳过随机消费，
    ///   本守卫双保险保证位级恒等）。
    ///
    /// **确定性**：只用 SimTrig 查表三角与 IEEE 基本运算（禁 Math.*——R1）；同输入跨运行时逐位同输出。
    /// </summary>
    public static class SimSpread
    {
        /// <summary>度 → 弧度（表列以度落值；常量折叠，与 SimTrig.Pi 同源）。</summary>
        public const float DegToRad = SimTrig.Pi / 180f;

        /// <summary>瞄准方向近竖直阈值（水平投影长度 ≤ 本值时换 x 轴参考——(dz,0,−dx) 在竖直向退化）。</summary>
        private const float HorizontalEpsilon = 1e-6f;

        /// <summary>
        /// 偏转方向。零偏转角返回原方向（恒等）。<paramref name="deflectionAngle"/>/万 <paramref name="azimuth"/>
        /// 均为弧度；两值同量级时结果只依赖两角与 dir（同输入逐位同输出）。
        /// </summary>
        public static SimVector3 Deflect(in SimVector3 dir, float deflectionAngle, float azimuth)
        {
            if (deflectionAngle <= 0f) return dir;

            float h2 = SimMath.MulAdd2(dir.X, dir.X, dir.Z, dir.Z);
            float ux, uy, uz;
            if (h2 > HorizontalEpsilon * HorizontalEpsilon)
            {
                float invH = 1f / SimMath.Sqrt(h2);                 // 上轴参考：u = 归一(上 × dir) = (dz, 0, −dx)/h
                ux = dir.Z * invH;
                uy = 0f;
                uz = -dir.X * invH;
            }
            else
            {
                // 近竖直：x 轴参考——u = 归一(x轴 × dir) = (0, −dz, dy)/|·|（竖直向时 |·| ≈ 1）
                float l2 = SimMath.MulAdd2(dir.Y, dir.Y, dir.Z, dir.Z);
                if (l2 <= HorizontalEpsilon * HorizontalEpsilon) return dir;   // 零向量防御：不偏转
                float invL = 1f / SimMath.Sqrt(l2);
                ux = 0f;
                uy = -dir.Z * invL;
                uz = dir.Y * invL;
            }

            // v = dir × u（u 单位且 ⟂ dir ⇒ v 单位）
            float vx = dir.Y * uz - dir.Z * uy;
            float vy = dir.Z * ux - dir.X * uz;
            float vz = dir.X * uy - dir.Y * ux;

            float sa = SimTrig.Sin(deflectionAngle);
            float ca = SimTrig.Cos(deflectionAngle);
            float sf = SimTrig.Sin(azimuth);
            float cf = SimTrig.Cos(azimuth);

            // 偏转平面内合成：dir' = ca·dir + sa·(cf·u + sf·v)
            float px = ca * dir.X + sa * (cf * ux + sf * vx);
            float py = ca * dir.Y + sa * (cf * uy + sf * vy);
            float pz = ca * dir.Z + sa * (cf * uz + sf * vz);

            // 重归一（查表三角的 cos²+sin² 误差 ~1e-8——归一后单位方向，maxT 沿向长度语义保持）
            float len = SimMath.Sqrt(SimMath.MulAdd3(px, px, py, py, pz, pz));
            if (len <= 0f) return dir;
            float inv = 1f / len;
            return new SimVector3(px * inv, py * inv, pz * inv);
        }
    }
}
