namespace LiteSim
{
    /// <summary>
    /// 射线判定单源（射击系统与瞄准激光共用的几何求交件，零引擎依赖）：
    /// 水平射线（2.5D：方向 XZ、原点带眼高 oy）× 实体圆柱 / 静态障碍（圆/盒）取最近交点。
    ///
    /// **与 <see cref="ShootingSystem"/> 原内联实现的逐位一致性**：实体求交保持原式
    /// （y 区间闸 + XZ 射线-圆：m=C−O、b=m·D、c2=|m|²−b²、t=b−√(r²−c2)；遍历恒定升序 +
    /// <c>t &lt; hitT</c> 严格小于 ⇒ 并列取低槽位）；障碍盒用 XZ slab 步进，圆与圆柱同式；
    /// y 区间闸实体/障碍同口径（含端点：<c>oy &lt; 底</c> 或 <c>oy &gt; 底+高</c> → 不相交）。
    /// 全程 <see cref="SimMath"/>（融合安全累积 + 软件 sqrt）——跨运行时逐位确定。
    ///
    /// t 单位 = 射线方向向量长度（开火 Aim 向量 ≈ 单位化 ⇒ t≈米）；调用方以 maxT 约束射程；
    /// 起点在形状内 → t=0（与原实体判定的 <c>if (t &lt; 0f) t = 0f</c> 同口径）。
    /// </summary>
    public static class SimRaycast
    {
        /// <summary>
        /// slab 平行判定阈值：|d| ≤ 本值的轴视为垂直于该轴的平行轴。量级远小于
        /// 1/<see cref="CombatConfig.HitscanRange"/>——该量级下全程位移 &lt; 1e-6 m，
        /// 平行处理与精确求交在射程内不可分辨（直接除会产生 1e8 量级的伪 t）。
        /// </summary>
        public const float ParallelEpsilon = 1e-8f;

        /// <summary>
        /// 实体圆柱射线（活体且非尸体才可命中——与射击判定同语义；skipSlot 跳过射手自身）。
        /// 最近命中（并列取低槽位——遍历升序 + 严格小于，与原内联实现一致）。
        /// </summary>
        /// <param name="s">世界状态（只读）。</param>
        /// <param name="skipSlot">跳过的槽位（射手自身；&lt;0 = 不跳）。</param>
        /// <param name="ox">射线原点 X（射手 XZ 位的 X 分量）。</param>
        /// <param name="oy">射线原点高度（眼高——身位圆柱 y 区间闸基准）。</param>
        /// <param name="oz">射线原点 Z。</param>
        /// <param name="dx">射线方向 X（≈单位化）。</param>
        /// <param name="dz">射线方向 Z。</param>
        /// <param name="maxT">射程上界。</param>
        /// <param name="hitSlot">命中槽位（未命中 = -1）。</param>
        /// <param name="t">命中距离（未命中 = maxT）。</param>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            float ox, float oy, float oz, float dx, float dz, float maxT, out int hitSlot, out float t)
        {
            hitSlot = -1;
            float hitT = maxT;
            float r2 = CombatConfig.HitscanRadius * CombatConfig.HitscanRadius;

            for (int j = 0; j < SimConfig.MaxEntities; j++)
            {
                if (j == skipSlot) continue;                                  // lint-allow R3（整型等值，非浮点精度比较）
                if ((s.AliveBitmap[j >> 5] & (1u << (j & 31))) == 0u) continue;

                ref EntitySlot tgt = ref s.Entities[j];
                if (tgt.Hp <= 0) continue;                                    // 死亡目标不可命中（尸体非有效目标）

                // 圆柱 y 区间：射线在 [tgt.Pos.Y, tgt.Pos.Y + Height] 内才算
                if (oy < tgt.Pos.Y) continue;
                if (oy > tgt.Pos.Y + CombatConfig.HitscanHeight) continue;

                // XZ 平面射线-圆求交：m = C-O；b = m·D（前向投影）；c2 = |m|²-b²（垂距平方）
                float mx = tgt.Pos.X - ox;
                float mz = tgt.Pos.Z - oz;
                float b = SimMath.MulAdd2(mx, dx, mz, dz);
                if (b < 0f) continue;                                        // 目标在身后

                float c2 = SimMath.MulAddSub3(mx, mx, mz, mz, b, b);
                if (c2 > r2) continue;                                       // 垂距超出圆柱半径

                float tj = b - SimMath.Sqrt(r2 - c2);
                if (tj < 0f) tj = 0f;                                         // 起点已在圆柱内

                if (tj < hitT)
                {
                    hitT = tj;
                    hitSlot = j;
                }
            }

            t = hitT;
            return hitSlot >= 0;
        }

        /// <summary>
        /// 静态障碍射线（XZ 平面 + y 区间闸）：最近交点；起点在障碍内 → t=0。
        /// </summary>
        /// <returns>是否在 maxT 内命中障碍。</returns>
        public static bool RaycastObstacles(in SimMapData map,
            float ox, float oy, float oz, float dx, float dz, float maxT, out float t)
        {
            float hitT = maxT;
            bool found = false;

            for (int o = 0; o < map.ObstacleCount; o++)
            {
                if (!IntersectObstacle(ox, oy, oz, dx, dz, in map.Obstacles[o], out float to)) continue;

                if (to < hitT)                          // 与实体同式：hitT 初值 = maxT ⇒ 射程外的障碍自然出局
                {
                    hitT = to;
                    found = true;
                }
            }

            t = hitT;
            return found;
        }

        /// <summary>单障碍求交：y 区间闸先判（射线眼高在障碍带外 → 不相交），再按形状分派。</summary>
        private static bool IntersectObstacle(float ox, float oy, float oz, float dx, float dz,
            in SimObstacle ob, out float t)
        {
            if (oy < ob.Center.Y) { t = 0f; return false; }
            if (oy > ob.Center.Y + ob.Height) { t = 0f; return false; }

            switch (ob.Kind)
            {
                case SimObstacleKind.Circle:
                    return IntersectCircle(ox, oz, dx, dz, ob.Center.X, ob.Center.Z, ob.Radius, out t);
                default:
                    return IntersectBox(ox, oz, dx, dz, in ob, out t);
            }
        }

        /// <summary>XZ 射线-圆（与实体圆柱同式；y 闸由调用方先判）。</summary>
        private static bool IntersectCircle(float ox, float oz, float dx, float dz,
            float cx, float cz, float radius, out float t)
        {
            float mx = cx - ox;
            float mz = cz - oz;
            float b = SimMath.MulAdd2(mx, dx, mz, dz);
            if (b < 0f) { t = 0f; return false; }                             // 圆心在身后

            float r2 = radius * radius;
            float c2 = SimMath.MulAddSub3(mx, mx, mz, mz, b, b);
            if (c2 > r2) { t = 0f; return false; }                            // 垂距超出半径

            float tj = b - SimMath.Sqrt(r2 - c2);
            if (tj < 0f) tj = 0f;                                             // 起点已在圆内
            t = tj;
            return true;
        }

        /// <summary>
        /// XZ 射线-盒（slab 步进）：逐轴收敛 [tmin, tmax]；平行轴（|d| ≤ <see cref="ParallelEpsilon"/>）
        /// 只做"原点在不在该轴区间内"判定。命中区间须与射线前向相交（tmax ≥ 0）；起点在盒内 → t=0。
        /// </summary>
        private static bool IntersectBox(float ox, float oz, float dx, float dz, in SimObstacle ob, out float t)
        {
            float minX = ob.Center.X - ob.HalfX;
            float maxX = ob.Center.X + ob.HalfX;
            float minZ = ob.Center.Z - ob.HalfZ;
            float maxZ = ob.Center.Z + ob.HalfZ;

            float tmin = 0f;
            float tmax = float.MaxValue;

            // X 轴 slab
            if (SimMath.Abs(dx) > ParallelEpsilon)
            {
                float inv = 1f / dx;
                float t1 = (minX - ox) * inv;
                float t2 = (maxX - ox) * inv;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) { t = 0f; return false; }
            }
            else if (ox < minX || ox > maxX)
            {
                t = 0f; return false;                                        // 平行且在区间外——永不进入
            }

            // Z 轴 slab
            if (SimMath.Abs(dz) > ParallelEpsilon)
            {
                float inv = 1f / dz;
                float t1 = (minZ - oz) * inv;
                float t2 = (maxZ - oz) * inv;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) { t = 0f; return false; }
            }
            else if (oz < minZ || oz > maxZ)
            {
                t = 0f; return false;                                        // 平行且在区间外——永不进入
            }

            if (tmax < 0f) { t = 0f; return false; }                          // 盒整体在身后
            if (tmin < 0f) tmin = 0f;                                        // 起点在盒内
            t = tmin;
            return true;
        }
    }
}
