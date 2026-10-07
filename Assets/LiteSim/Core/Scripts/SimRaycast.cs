namespace LiteSim
{
    /// <summary>
    /// 射线判定单源（射击系统与瞄准激光共用的几何求交件，零引擎依赖）：
    /// **实体求交为三维射线 × 双柱阶梯**（俯视角爆头判定用——《固定斜视角射击方案专项设计》§5）；
    /// 静态障碍**保持 2.5D**（XZ 射线 + y 区间闸——关卡障碍均为地面级圆/盒，三维化收益为零而破坏面大）。
    ///
    /// **双柱判定几何（爆头柱/非爆头柱）**：实体命中形状是**竖直堆叠的两根圆柱**——
    /// 身体柱 <c>[Pos.Y, Pos.Y+HeadHitLine) × HitscanRadius</c>（普通命中）＋
    /// 爆头柱 <c>[Pos.Y+HeadHitLine, Pos.Y+HitscanHeight] × HeadshotRadius</c>（Crit 区）。
    /// **为什么是几何双柱而不是"命中柱 + 事后水平闸"**：AimPoint 是相机射线 ∩ 命中形状的**表面点**，
    /// 单柱下瞄头的射线在 0.45 柱面取点（水平距恒 ≈0.45）——任何事后水平闸都会把这些"瞄头"的点拒成
    /// 普通命中（实测"准心瞄头打中却是白字"）；把爆头区做进几何后，瞄头的射线穿透肩侧空气（该处无形状）
    /// 在爆头柱面上取点（水平距 ≤ 半径）——"命中爆头柱"即 Crit，所见即所判。
    /// 带高处 [HeadshotRadius, HitscanRadius) 的环状空隙按裁决为**不可命中**（装饰性肩沿/发侧放过）。
    ///
    /// **三维化口径（实体）**：射线方向含 Y 分量（单位向量），圆柱为**有限高**竖直圆柱。
    /// 求交分两步（XZ 圆 + Y 带），**等价于三维圆柱求交**但复用既有的 XZ 圆式与 y 闸口径——
    /// 逐位一致性由此保持（遍历恒定升序 + <c>t &lt; hitT</c> 严格小于 ⇒ 并列取低槽位；双柱间取更早 t，
    /// 同时命中两柱的贴界射线判爆头柱——Crit 语义优先，与 <c>relY ≥ HeadHitLine</c> 闭区间口径一致）。
    /// **确定性**：全部新增运算走 <see cref="SimMath"/>（融合安全累积 + 软件 sqrt），跨运行时逐位确定。
    ///
    /// **Y 带闸＝区间判定（斜射线正确性）**：XZ 圆交出进入/离开区间 <c>[tIn, tOut]</c>，射线高度
    /// 满足 <c>y(t) = oy + k·t</c>（<c>k = dy/h</c>，t 为水平距离），落在该柱 Y 区间
    /// 的参数区间是 <c>[lo, hi]</c>；**两区间有交集即命中**，命中距离取交集首次进入点。
    /// 单点闸（只看进入瞬间的高度）在**大俯角 + 近距离**下会误杀——俯视相机射线陡斜向下，进入圆柱处
    /// 已在柱顶之上，但它继续下降确实穿过柱体下段（实测俯角 50°/相机 10m/目标 8m 时完全打不中）。
    /// <c>dy = 0</c>（水平射线）保留"原点高度在带内"的旧闸，**与二维旧实现逐位一致**。
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
        /// 实体圆闸**接触容差**（垂距平方，m²）：垂距允许超出半径至多 √(R²+本值)≈R+本值/(2R)（≈0.05mm）。
        /// **为什么需要**：准心解算出的 AimPoint 就落在圆柱**表面上**（相机射线 ∩ 同一几何）——从枪口指向该点
        /// 的命中线数学上必经表面，只有**擦边**时 c2−R² ≈ 0；float32 经输入/坐标/方向多处舍入后该差值
        /// 在 ±1e-5 量级随机翻转（实测 18–24m 处 1.7e-5~7.8e-5），表现为"准心压在柱的轮廓边却打不中"。
        /// 本容差把"接触"判为命中（切线：tIn=tOut=b）。**只放宽 0.05mm 级几何**，不影响任何正常距离判定。
        /// </summary>
        public const float TouchEpsilon = 1e-5f;

        /// <summary>
        /// 实体双柱射线·**三维**（活体且非尸体才可命中——与射击判定同语义；skipSlot 跳过射手自身）。
        /// 最近命中（并列取低槽位——遍历升序 + 严格小于，与旧二维实现一致；同一目标的两柱取更早者，
        /// 同时贴界优先爆头柱）。
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
        /// <param name="hitSlot">命中槽位（未命中 = -1）。</param>
        /// <param name="t">命中距离（未命中 = maxT）。</param>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            float ox, float oy, float oz, float dx, float dz, float dy, float maxT,
            out int hitSlot, out float t)
        {
            hitSlot = -1;
            float hitT = maxT;

            // 双柱几何（每目标同刻取值——测试模式滑杆覆写随动）
            float bodyR2 = CombatConfig.HitscanRadius * CombatConfig.HitscanRadius;
            float headR2 = CombatConfig.HeadshotRadiusLive * CombatConfig.HeadshotRadiusLive;
            float headLine = CombatConfig.HeadHitLineLive;
            float bodyTop = CombatConfig.HitscanHeight;

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
                //
                // **c2 直接算垂线分量（p = m − b·D̂），不用 |m|²−b²**：后者是两个 ~D² 量级的
                // 大数相减——float32 消减误差随距离放大（实测 18–24m 达 ~8e-5），把"弹道擦过
                // 圆柱面"的判定变成随机翻转（准心解算出的 AimPoint 就落在圆柱**表面上**——
                // 相机射线与同一几何求交所得 ⇒ 命中线数学上必经表面，只是擦边时 c2−R² ≈ 0）。
                // 直接分量式把 c2 的误差压到 ~R² 量级浮点精度，再配 <see cref="TouchEpsilon"/>
                // 容差，保证"瞄到的表面点必可命中"。
                float invH = 1f / h;
                float ndx = dx * invH;
                float ndz = dz * invH;
                float mx = tgt.Pos.X - ox;
                float mz = tgt.Pos.Z - oz;
                float b = SimMath.MulAdd2(mx, ndx, mz, ndz);
                // **目标在身后**判定只在**两柱外**成立：起点已在命中形状内（|m| ≤ 命中柱半径）时水平投影
                // 朝哪都算命中（近距枪口贴到目标柱内、准心压近弧下方点时，P−muzzle 的水平分量可能朝
                // "身后"——旧式一律 b<0 整条拒 = 贴脸打脚落空）。柱内起点由下方 tIn 钳 0 落成 t=0 命中（同契约）。
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
        /// 斜射线 × 单柱的 Y 带 slab 求交：XZ 圆区间 <c>[tIn, tOut]</c>（由 c2/r2 解出）与
        /// 该柱 Y 区间 <c>[yBot, yTop]</c> 的参数区间求交——**有交集即命中**，t 取交集首次进入点
        /// （折回三维参数）。水平射线（k=0）沿用"原点高度在带内"的旧闸（逐位等价旧实现）。
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
        /// 既有二维调用方（瞄准激光、沙盒、L1 用例）走此口——**行为与三维版本逐位一致**，
        /// 无需改调用点（新增三维能力的入口见上方重载）。
        /// </summary>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            float ox, float oy, float oz, float dx, float dz, float maxT, out int hitSlot, out float t)
            => RaycastEntities(s, skipSlot, ox, oy, oz, dx, dz, 0f, maxT, out hitSlot, out t);

        /// <summary>
        /// 实体圆柱射线·**SimVector3 便捷入口**（三维）——**形参序不可能传错**：三维重载的
        /// <c>(dx, dz, dy)</c> 邻位极易手误换位（实测事故：采集侧把方向按 <c>(x,y,z)</c> 顺序直传
        /// ⇒ y/z 对调、水平方向整体错位、实体求交恒 miss → 落地面兜底——"准心在敌人身上打不中/
        /// 激光瞄地"）。凡持方向的调用方一律走本入口；语义与本类三维重载逐项一致。
        /// </summary>
        public static bool RaycastEntities(SimWorldState s, int skipSlot,
            in SimVector3 origin, in SimVector3 dir, float maxT, out int hitSlot, out float t)
            => RaycastEntities(s, skipSlot, origin.X, origin.Y, origin.Z, dir.X, dir.Z, dir.Y, maxT, out hitSlot, out t);

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
