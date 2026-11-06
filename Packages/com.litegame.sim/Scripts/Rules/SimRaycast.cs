namespace LiteSim
{
    /// <summary>
    /// 射线判定单源（射击系统与瞄准激光共用的几何求交件，零引擎依赖）： **实体求交为三维射线 × 双柱阶梯**（俯视角爆头判定用——《固定斜视角射击方案专项设计》§5）。
    /// </summary>
    public static class SimRaycast
    {
        /// <summary>
        /// slab 平行判定阈值：|d| ≤ 本值的轴视为垂直于该轴的平行轴。
        /// </summary>
        public const float ParallelEpsilon = 1e-8f;

        /// <summary>
        /// 实体圆闸**接触容差**（垂距平方，m²）：垂距允许超出半径至多 √(R²+本值)≈R+本值/(2R)（≈0.05mm）。
        /// </summary>
        public const float TouchEpsilon = 1e-5f;

        /// <summary>
        /// 实体双柱射线·**三维**（活体且非尸体才可命中——与射击判定同语义；skipSlot 跳过射手自身）。
        /// </summary>
        /// <param name="s">世界状态（只读）。</param>
        /// <param name="skipSlot">跳过的槽位（射手自身；&lt;0 = 不跳）。</param>
        /// <param name="ox">射线原点 X（射手 XZ 位的 X 分量）。</param>
        /// <param name="oy">射线原点高度（弹道出射高度 = 本体 + 烘焙枪口高）。</param>
        /// <param name="oz">射线原点 Z。</param>
        /// <param name="dx">射线方向 X（≈单位化，含 Y 分量时的水平投影）。</param>
        /// <param name="dz">射线方向 Z（同上）。</param>
        /// <param name="dy">射线方向 Y（**0 = 水平射线**，等价旧 2.5D 行为）。</param>
        /// <param name="maxT">射程上界（沿三维方向的长度）。</param>
        /// <param name="rules">本局测试规则（爆头带试验覆写随动；null = 烘焙导出值）。判定链经 SimStep 注入；
        /// <param name="hitSlot">命中槽位（未命中 = -1）。</param>
        /// <param name="t">命中距离（未命中 = maxT）。</param>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            float ox, float oy, float oz, float dx, float dz, float dy, float maxT,
            out int hitSlot, out float t, in CombatGeometry geometry)
        {
            hitSlot = -1;
            float hitT = maxT;

            // 双柱几何（几何来自本局 CombatValues.Geometry；机制面零静态读）
            float bodyR2 = geometry.HitscanRadius * geometry.HitscanRadius;
            float headR = geometry.HeadshotRadius;
            float headR2 = headR * headR;
            float headLine = geometry.HeadHitLine;
            float bodyTop = geometry.HitscanHeight;

            // 三维化关键：**XZ 平面内的参数化被 dy 拉伸**。
            float h2 = SimMath.MulAdd2(dx, dx, dz, dz);
            float h = SimMath.Sqrt(h2);
            bool vertical = h <= ParallelEpsilon;

            for (int j = 0; j < SimConfig.MaxEntities; j++)
            {
                if (j == skipSlot) continue;                                  // lint-allow R3（整型等值，非浮点精度比较）
                if ((s.AliveBitmap[j >> 5] & (1u << (j & 31))) == 0u) continue;

                ref EntitySlot tgt = ref s.Entities[j];
                if (tgt.Hp <= 0) continue;                                    // 死亡目标不可命中（尸体非有效目标）

                // 垂直射线：XZ 位置恒定 ⇒ 水平距离 = 原点到目标轴的垂距；逐柱判 Y 带内是否有交点。
                if (vertical)
                {
                    float vx = tgt.Pos.X - ox;
                    float vz = tgt.Pos.Z - oz;
                    float flat2 = SimMath.MulAdd2(vx, vx, vz, vz);

                    // 双柱分别求交（爆头柱=上带 [line, top]，身体柱=下带 [0, line)）；
                    // 只比较真实命中的柱（未命中柱的 out t 是占位 0，不得参与取早）。
                    bool vh = VerticalCylinderHit(flat2 <= headR2, tgt.Pos.Y + headLine, tgt.Pos.Y + bodyTop, oy, dy, out float tHeadV);
                    bool vb = VerticalCylinderHit(flat2 <= bodyR2, tgt.Pos.Y, tgt.Pos.Y + headLine, oy, dy, out float tBodyV);
                    if (vh | vb)
                        PickTwoCylinders(j, ref hitSlot, ref hitT, vh, tHeadV, vb, tBodyV);
                    continue;
                }

                // XZ 平面射线-圆求交（沿**水平距离**参数化）：m = C-O；b = m·D̂；垂距平方 c2。
                float invH = 1f / h;
                float ndx = dx * invH;
                float ndz = dz * invH;
                float mx = tgt.Pos.X - ox;
                float mz = tgt.Pos.Z - oz;
                float b = SimMath.MulAdd2(mx, ndx, mz, ndz);
                // **目标在身后**判定只在**两柱外**成立：起点已在命中形状内（|m| ≤ 命中柱半径）时水平投影 朝哪都算命中（近距枪口贴到目标柱内、准心压近弧下方点时，P−muzzle 的水平分量可能朝 "身后"——旧式一律 b<0 整条拒 = 贴脸打脚落空）。
                float m2 = SimMath.MulAdd2(mx, mx, mz, mz);
                if (b < 0f && m2 > bodyR2) continue;                          // 目标在身后且起点在两柱外

                float px = mx - b * ndx;
                float pz = mz - b * ndz;
                float c2 = SimMath.MulAdd2(px, px, pz, pz);

                // 双柱分别求 Y 带交集（XZ 圆区间 [tIn,tOut] 与该柱 Y 参数区间交叠）——
                // 爆头柱=上带 [line, top]、身体柱=下带 [0, line)（贴界射线两柱同刻命中时 Crit 语义优先）。
                float tHead, tBody;
                bool hitHead = SlabCylinderHit(c2, headR2, b, tgt.Pos.Y + headLine, tgt.Pos.Y + bodyTop, oy, dy, invH, out tHead);
                bool hitBody = SlabCylinderHit(c2, bodyR2, b, tgt.Pos.Y, tgt.Pos.Y + headLine, oy, dy, invH, out tBody);
                if (hitHead | hitBody)
                    PickTwoCylinders(j, ref hitSlot, ref hitT, hitHead, tHead, hitBody, tBody);
            }

            t = hitT;
            return hitSlot >= 0;
        }

        /// <summary>两柱命中归属：只比较真实命中的柱，取更早 t；同刻（贴界射线）判爆头柱
        /// （Crit 优先——与判定侧 relY ≥ 线闭区间一致）。</summary>
        private static void PickTwoCylinders(int slot, ref int hitSlot, ref float hitT,
            bool hitHead, float tHead, bool hitBody, float tBody)
        {
            bool headWins = hitHead && (!hitBody || tHead <= tBody);           // 爆头柱在双柱堆叠位上方——先撞即更早
            float best = headWins ? tHead : tBody;
            if (best < hitT)
            {
                hitT = best;
                hitSlot = slot;
            }
        }

        /// <summary>
        /// 斜射线 × 单柱的 Y 带 slab 求交：XZ 圆区间 <c>[tIn, tOut]</c>（由 c2/r2 解出）与 该柱 Y 区间 <c>[yBot, yTop]</c> 的参数区间求交——**有交集即命中**。
        /// </summary>
        private static bool SlabCylinderHit(float c2, float r2, float b,
            float yBot, float yTop, float oy, float dy, float invH, out float tHit)
        {
            tHit = 0f;
            if (c2 - r2 > TouchEpsilon) return false;                         // 垂距超出该柱半径（含接触容差）

            float sq = c2 < r2 ? SimMath.Sqrt(r2 - c2) : 0f;                  // 容差带内（c2≥r2）视为切线：tIn=tOut=b
            float tIn = b - sq;                                               // 进入该柱（水平距离参数）
            float tOut = b + sq;                                              // 离开该柱
            if (tIn < 0f) tIn = 0f;                                          // 起点已在该柱内
            if (tOut < tIn) return false;

            float k = dy * invH;                                             // dy / h：每单位水平距离的高度增量
            if (k == 0f)
            {
                // **水平射线**：高度恒为 oy —— 沿用"原点高度在带内"的旧闸，与二维旧实现
                // 逐位一致（这一支**不做区间化**，否则会放过"整条射线都在带外"的情形）。
                if (oy < yBot || oy > yTop) return false;
                tHit = tIn * invH;
                return true;
            }

            float lo = (k > 0f ? yBot - oy : yTop - oy) / k;                  // k<0 时上下界自动交换
            float hi = (k > 0f ? yTop - oy : yBot - oy) / k;
            if (hi < lo) return false;                                       // 防御（k≠0 时已按符号排好）

            float enter = tIn > lo ? tIn : lo;
            float leave = tOut < hi ? tOut : hi;
            if (leave < enter) return false;                                  // 区间不相交 ⇒ 从该柱顶/柱底掠过

            tHit = enter * invH;                                              // 折回三维参数（t_3d = t_xz / h）
            return true;
        }

        /// <summary>
        /// 垂直射线 × 单柱：水平垂距已在半径内（调用方先判）后，Y 带 <c>[yBot, yTop]</c> 与竖直行程求交。
        /// </summary>
        private static bool VerticalCylinderHit(bool withinRadius, float yBot, float yTop,
            float oy, float dy, out float tHit)
        {
            tHit = 0f;
            if (!withinRadius) return false;

            // 竖直射线穿过 Y 带的空间跨度：t_lo = (底-oy)/dy、t_hi = (顶-oy)/dy（dy>0 升序）
            float tLo, tHi;
            if (dy > 0f) { tLo = (yBot - oy) / dy; tHi = (yTop - oy) / dy; }
            else { float a = (yTop - oy) / dy; float b2 = (yBot - oy) / dy; tLo = a < b2 ? a : b2; tHi = a < b2 ? b2 : a; }
            if (tHi < 0f) return false;                                       // Y 带整体在射线背后
            tHit = tLo < 0f ? 0f : tLo;                                      // 起点已在柱内 → t=0（同口径）
            return true;
        }

        /// <summary>
        /// 实体圆柱射线·**二维便捷入口**（<c>dy = 0</c>，等价旧 2.5D 行为）。
        /// </summary>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            float ox, float oy, float oz, float dx, float dz, float maxT, out int hitSlot, out float t,
            in CombatGeometry geometry)
            => RaycastEntities(s, skipSlot, ox, oy, oz, dx, dz, 0f, maxT, out hitSlot, out t, geometry);

        /// <summary>
        /// 实体圆柱射线·**SimVector3 便捷入口**（三维）——**形参序不可能传错**：三维重载的 <c>(dx, dz, dy)</c> 邻位极易手误换位（实测事故：采集侧把方向按 <c>(x,y,z)</c> 顺序。）。
        /// </summary>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            in SimVector3 origin, in SimVector3 dir, float maxT, out int hitSlot, out float t,
            in CombatGeometry geometry)
            => RaycastEntities(s, skipSlot, origin.X, origin.Y, origin.Z, dir.X, dir.Z, dir.Y, maxT, out hitSlot, out t, geometry);

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

        /// <summary>单障碍求交：y 区间闸先判（射线原点高度在障碍带外 → 不相交），再按形状分派。</summary>
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
