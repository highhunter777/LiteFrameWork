using System;
using System.Text;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// **可见面命中一致性回归**（"准心在圆柱上却射不中"缺陷的钉子）：
    /// 复刻整链——相机射线（固定斜视角 rig：世界系偏移 (0,10,−8)）∩ 圆柱 → AimPoint → **从逻辑枪口
    /// 指向该点开火**；在全几何网格（距离/方位/可见弧/顶盖）上要求**弹道必命中目标**。
    ///
    /// **为什么需要**：AimPoint 是相机射线与同一圆柱的交点——**落在表面上**，命中线数学上必经表面；
    /// 只有擦边（c2−R² ≈ 0）时 float32 舍入会把它翻成"垂距超半径"整发拒掉（实测 18–24m 处
    /// 1.7e-5~7.8e-5 的虚假 c2−r2；旧 |m|²−b² 消减式 + 无容差 ⇒ 12/1566 例"瞄着轮廓边打不中"）。
    /// 修复 = 圆闸垂距直接分量计算 + <see cref="SimRaycast.TouchEpsilon"/> 接触容差——本用例锁死。
    /// </summary>
    public sealed class AimParallaxHitTests
    {
        [Fact]
        public void 可见面全采样_弹道必命中()
        {
            var sb = new StringBuilder();
            var camPos = new SimVector3(0f, 10f, -8f);
            float R = CombatConfig.HitscanRadius;
            float H = CombatConfig.HitscanHeight;

            float[] dists = { 1.2f, 1.6f, 2.5f, 4f, 6f, 9f, 13f, 18f, 24f };
            float[] bearingsDeg = { 0f, 45f, 90f, 135f, 180f, -90f };
            // 准心落点：可见面采样（世界 −Z 侧弧 + 顶盖）
            float[] arcHeights = { 0.15f, 0.8f, 1.5f, 1.78f };

            int cases = 0, misses = 0;
            foreach (float D in dists)
            foreach (float bDeg in bearingsDeg)
            {
                double br = bDeg * Math.PI / 180.0;
                float cx = (float)(Math.Cos(br) * D), cz = (float)(Math.Sin(br) * D);

                var w = new SimWorldState { RngState = 1UL };
                w.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int sh);
                w.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(cx, 0f, cz) }, out int tslot);
                float yaw = (float)Math.Atan2(cz - 0f, cx - 0f);   // 玩家面向目标

                // 可见弧采样（世界 −Z 侧半弧；相机在玩家 −Z 上方）
                foreach (float aDeg in new float[] { -80f, -40f, 0f, 40f, 80f })
                foreach (float hy in arcHeights)
                {
                    double a = aDeg * Math.PI / 180.0;
                    var Q = new SimVector3(cx + (float)(Math.Sin(a) * R), hy, cz - (float)(Math.Cos(a) * R));
                    cases++;
                    if (!TryAimCell(w, sh, camPos, Q, out SimVector3 P, out int camSlot, out _)) { continue; }

                    var muzzle = CombatConfig.MuzzleOrigin(new SimVector3(0f, 0f, 0f), yaw);
                    float bx = P.X - muzzle.X, by = P.Y - muzzle.Y, bz = P.Z - muzzle.Z;
                    Norm(ref bx, ref by, ref bz);
                    bool ok = SimRaycast.RaycastEntities(w, sh, muzzle.X, muzzle.Y, muzzle.Z, bx, bz, by,
                        CombatConfig.HitscanRange, out int bSlot, out _) && bSlot == tslot;
                    if (!ok)
                    {
                        misses++;
                        sb.AppendLine($"★ D={D,5} 方位={bDeg,4}° 弧={aDeg,4}° 高={hy,5}  P=({P.X:F3},{P.Y:F3},{P.Z:F3})  muzzle=({muzzle.X:F3},{muzzle.Y:F3},{muzzle.Z:F3})");
                        sb.AppendLine("    " + Diag(cx, cz, muzzle.X, muzzle.Y, muzzle.Z, bx, bz, by));
                    }
                }

                // 顶盖采样（y=H，xz 在圆内）
                foreach (float u in new float[] { -0.6f, 0f, 0.6f })
                foreach (float v in new float[] { -0.6f, 0f, 0.6f })
                {
                    var Q = new SimVector3(cx + u * R, H, cz + v * R);
                    cases++;
                    if (!TryAimCell(w, sh, camPos, Q, out SimVector3 P, out _, out _)) { continue; }

                    var muzzle = CombatConfig.MuzzleOrigin(new SimVector3(0f, 0f, 0f), yaw);
                    float bx = P.X - muzzle.X, by = P.Y - muzzle.Y, bz = P.Z - muzzle.Z;
                    Norm(ref bx, ref by, ref bz);
                    bool ok = SimRaycast.RaycastEntities(w, sh, muzzle.X, muzzle.Y, muzzle.Z, bx, bz, by,
                        CombatConfig.HitscanRange, out int bSlot, out _) && bSlot == tslot;
                    if (!ok)
                    {
                        misses++;
                        sb.AppendLine($"★盖 D={D,5} 方位={bDeg,4}° 偏=({u},{v})  P=({P.X:F3},{P.Y:F3},{P.Z:F3})");
                    }
                }
            }
            Assert.True(misses == 0,
                "可见面上有采样点弹道落空（" + misses + "/" + cases + "）——接触容差/圆闸数值回退？\n" + sb);
        }

        private static bool TryAimCell(SimWorldState w, int sh, SimVector3 camPos, SimVector3 Q,
            out SimVector3 P, out int camSlot, out float camT)
        {
            float dx = Q.X - camPos.X, dy = Q.Y - camPos.Y, dz = Q.Z - camPos.Z;
            Norm(ref dx, ref dy, ref dz);
            bool hit = SimRaycast.RaycastEntities(w, sh, camPos.X, camPos.Y, camPos.Z, dx, dz, dy,
                CombatConfig.HitscanRange, out camSlot, out camT);
            P = new SimVector3(camPos.X + dx * camT, camPos.Y + dy * camT, camPos.Z + dz * camT);
            return hit;
        }

        private static void Norm(ref float x, ref float y, ref float z)
        {
            float len = (float)Math.Sqrt(x * x + y * y + z * z);
            x /= len; y /= len; z /= len;
        }

        /// <summary>复刻 SimRaycast 对单个目标圆柱的内部量（诊断用）。</summary>
        private static string Diag(float cx, float cz, float ox, float oy, float oz,
            float dx, float dz, float dy)
        {
            float R = CombatConfig.HitscanRadius;
            float H = CombatConfig.HitscanHeight;
            float h = (float)Math.Sqrt(dx * dx + dz * dz);
            if (h <= 1e-8f) return "vertical";
            float ndx = dx / h, ndz = dz / h;
            float mx2 = cx - ox, mz2 = cz - oz;
            float b = mx2 * ndx + mz2 * ndz;
            float c2 = mx2 * mx2 + mz2 * mz2 - b * b;
            float r2 = R * R;
            float yBot = 0f, yTop = H;
            float k = dy / h;
            if (c2 > r2) return $"圆闸拒: b={b:F4} c2={c2:F6} r2={r2:F6} c2-r2={c2 - r2:E2}";
            float sq = (float)Math.Sqrt(r2 - c2);
            float tIn = b - sq, tOut = b + sq;
            if (tIn < 0f) tIn = 0f;
            float lo = (k > 0f ? yBot - oy : yTop - oy) / k;
            float hi = (k > 0f ? yTop - oy : yBot - oy) / k;
            float enter = Math.Max(tIn, lo), leave = Math.Min(tOut, hi);
            if (leave < enter) return $"Y带拒: tIn={tIn:F4} tOut={tOut:F4} lo={lo:F4} hi={hi:F4} enter={enter:F4} leave={leave:F4} k={k:F6} oy={oy:F5}";
            return "hit?";
        }
    }
}
