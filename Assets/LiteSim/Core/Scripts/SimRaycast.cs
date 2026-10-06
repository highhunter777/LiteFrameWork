namespace LiteSim
{
    /// <summary>
    /// 射线判定单源（射击系统与瞄准激光共用的几何求交件，零引擎依赖）：
    /// **实体求交为三维射线 × 竖直圆柱**（俯视角爆头判定用——《俯视角三维命中与爆头判定专项设计》§4.1）；
    /// 静态障碍**保持 2.5D**（XZ 射线 + y 区间闸——关卡障碍均为地面级圆/盒，三维化收益为零而破坏面大）。
    ///
    /// **三维化口径（实体）**：射线方向含 Y 分量（单位向量），圆柱为**有限高**竖直圆柱
    /// （半径 <see cref="CombatConfig.HitscanRadius"/>、高 <see cref="CombatConfig.HitscanHeight"/>）。
    /// 求交分两步（XZ 圆 + Y 带），**等价于三维圆柱求交**但复用既有的 XZ 圆式与 y 闸口径——
    /// 逐位一致性由此保持（遍历恒定升序 + <c>t &lt; hitT</c> 严格小于 ⇒ 并列取低槽位）。
    /// **确定性**：全部新增运算走 <see cref="SimMath"/>（融合安全累积 + 软件 sqrt），跨运行时逐位确定。
    ///
    /// **二维入口（<c>dy = 0</c>）**：既有两参数重载委托到三维版本并传 <c>dy = 0</c>——
    /// 数学上**逐位等价于旧实现**（Y 闸恒判原点高度、方向无 Y），故既有二维调用方
    /// （瞄准激光/沙盒/45 处 L1 用例）**零改动、行为不变**。
    ///
    /// t 单位 = **射线方向向量长度（三维单位化 ⇒ t≈米）**；调用方以 maxT 约束射程；
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
        /// 实体圆柱射线·**三维**（活体且非尸体才可命中——与射击判定同语义；skipSlot 跳过射手自身）。
        /// 最近命中（并列取低槽位——遍历升序 + 严格小于，与旧二维实现一致）。
        /// </summary>
        /// <param name="s">世界状态（只读）。</param>
        /// <param name="skipSlot">跳过的槽位（射手自身；&lt;0 = 不跳）。</param>
        /// <param name="ox">射线原点 X（射手 XZ 位的 X 分量）。</param>
        /// <param name="oy">射线原点高度（眼高）。</param>
        /// <param name="oz">射线原点 Z。</param>
        /// <param name="dx">射线方向 X（≈单位化，含 Y 分量时的水平投影）。</param>
        /// <param name="dz">射线方向 Z（同上）。</param>
        /// <param name="dy">射线方向 Y（**0 = 水平射线**，等价旧 2.5D 行为）。</param>
        /// <param name="maxT">射程上界（沿三维方向的长度）。</param>
        /// <param name="hitSlot">命中槽位（未命中 = -1）。</param>
        /// <param name="t">命中距离（未命中 = maxT）。</param>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            float ox, float oy, float oz, float dx, float dz, float dy, float maxT,
            out int hitSlot, out float t)
        {
            hitSlot = -1;
            float hitT = maxT;
            float r2 = CombatConfig.HitscanRadius * CombatConfig.HitscanRadius;

            // 三维化关键：**XZ 平面内的参数化被 dy 拉伸**。方向 (dx,dy,dz) 单位化后，
            // 其水平投影长度 h = √(dx²+dz²) ≤ 1 —— 前进 h 单位三维度，水平只走了 1 单位。
            // 故 XZ 求交须按"每单位水平距离"解，再折回沿三维方向的参数 t（t_3d = t_xz / h）。
            // h = 0（近乎垂直朝天/朝地）时水平投影退化，另行判定。
            float h2 = SimMath.MulAdd2(dx, dx, dz, dz);
            float h = SimMath.Sqrt(h2);
            bool vertical = h <= ParallelEpsilon;

            for (int j = 0; j < SimConfig.MaxEntities; j++)
            {
                if (j == skipSlot) continue;                                  // lint-allow R3（整型等值，非浮点精度比较）
                if ((s.AliveBitmap[j >> 5] & (1u << (j & 31))) == 0u) continue;

                ref EntitySlot tgt = ref s.Entities[j];
                if (tgt.Hp <= 0) continue;                                    // 死亡目标不可命中（尸体非有效目标）

                // 垂直射线：XZ 位置恒定 ⇒ 水平距离 = 原点到目标轴的垂距；再判Y 带内是否有交点。
                // 水平射线（dy=0）：退化为旧 y 闸（判原点高度），逐位等价旧实现。
                float tHit;
                if (vertical)
                {
                    float vx = tgt.Pos.X - ox;
                    float vz = tgt.Pos.Z - oz;
                    float flat2 = SimMath.MulAdd2(vx, vx, vz, vz);
                    if (flat2 > r2) continue;                // 垂距超出圆柱半径 ⇒ 整条射线都在柱外

                    // 竖直射线穿过 Y 带的空间跨度：t_lo = (底-oy)/dy、t_hi = (顶-oy)/dy（dy>0 升序）
                    float yBottom = tgt.Pos.Y;
                    float yTop = tgt.Pos.Y + CombatConfig.HitscanHeight;
                    float tLo, tHi;
                    if (dy > 0f) { tLo = (yBottom - oy) / dy; tHi = (yTop - oy) / dy; }
                    else { float a = (yTop - oy) / dy; float b2 = (yBottom - oy) / dy; tLo = a < b2 ? a : b2; tHi = a < b2 ? b2 : a; }
                    if (tHi < 0f) continue;                                  // Y 带整体在射线背后
                    tHit = tLo < 0f ? 0f : tLo;                             // 起点已在柱内 → t=0（同口径）
                }
                else
                {
                    // XZ 平面射线-圆求交（沿**水平距离**参数化）：m = C-O；b = m·D̂；c2 = |m|²-b²（垂距平方）
                    float invH = 1f / h;
                    float ndx = dx * invH;
                    float ndz = dz * invH;
                    float mx = tgt.Pos.X - ox;
                    float mz = tgt.Pos.Z - oz;
                    float b = SimMath.MulAdd2(mx, ndx, mz, ndz);
                    if (b < 0f) continue;                                    // 目标在身后

                    float c2 = SimMath.MulAddSub3(mx, mx, mz, mz, b, b);
                    if (c2 > r2) continue;                                   // 垂距超出圆柱半径

                    float tFlat = b - SimMath.Sqrt(r2 - c2);
                    if (tFlat < 0f) tFlat = 0f;                               // 起点已在圆柱内

                    // 折回三维参数：t_3d = tFlat / h（水平距离 ÷ 水平投影长度）
                    tHit = tFlat * invH;

                    // **Y 带闸（爆头判定的物理面）**：沿三维射线在 tHit 处的高度须落在身位区间内。
                    // 旧 2.5D 是「原点高度在带内」的常量闸；三维化后是**随距离变化**的闸——
                    // 这正是平地可爆头的物理来源（射线朝上 ⇒ 远处命中点抬高进头部带）。
                    // **闭区间 [底, 顶]**（与旧 2.5D 口径**逐位一致**）：`oy > 顶` 才排除。
                    // 边界口径不得单方面收紧——既有用例「高差位命中头部带」（射手 Y=1⇒ 眼高恰=
                    // 身位顶 2.0）依赖此含端点语义；收紧上界会把该命中整体判为未命中（实测回归）。
                    float yHit = oy + dy * tHit;
                    if (yHit < tgt.Pos.Y) continue;
                    if (yHit > tgt.Pos.Y + CombatConfig.HitscanHeight) continue;
                }

                if (tHit < hitT)
                {
                    hitT = tHit;
                    hitSlot = j;
                }
            }

            t = hitT;
            return hitSlot >= 0;
        }

        /// <summary>
        /// 实体圆柱射线·**二维便捷入口**（<c>dy = 0</c>，等价旧 2.5D 行为）。
        /// 既有二维调用方（瞄准激光、沙盒、L1 用例）走此口——**行为与三维版本逐位一致**，
        /// 无需改调用点（新增三维能力的入口见上方重载）。
        /// </summary>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            float ox, float oy, float oz, float dx, float dz, float maxT, out int hitSlot, out float t)
            => RaycastEntities(s, skipSlot, ox, oy, oz, dx, dz, 0f, maxT, out hitSlot, out t);

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
