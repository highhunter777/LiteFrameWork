namespace LiteSim
{
    /// <summary>
    /// 移动/重力系统（§3.3 顺序第 2 位，§3.5 2.5D）：
    /// XZ 平面位移 + y 轴重力积分 + 地面钳制 + 世界边界钳制 + **静态障碍去穿插**。
    ///
    /// 障碍碰撞：
    /// - 判定半 = <see cref="SimObstacle"/>（圆/盒，XZ 平面 + y 区间闸）；视觉网格不参与判定（§18 视觉半）；
    /// - 身体圆柱 = **物理半径**（<see cref="CombatConfig.BodyRadius"/>，烘焙自 prefab CC）与
    ///   站姿高 <see cref="CombatConfig.HitscanHeight"/>——与命中柱（<see cref="CombatConfig.HitscanRadius"/>，
    ///   判定宽容裁决常量）**解耦**：推挡/贴墙贴的是身体，不是判定宽容度；
    /// - 解析 = 逐障碍**去穿插**（推出而非预先阻挡）：速度不衰减，下帧输入照写——贴墙滑行是
    ///   积分与去穿插的固有产物，无需显式滑移逻辑；
    /// - 确定性：先全体积分、再统一解析（第二个循环——避免"谁先解析谁占便宜"的顺序耦合），
    ///   实体升序 × 障碍升序、每帧单圈；距离算术走 <see cref="SimMath"/>（软件 sqrt/MulAdd2，跨运行时逐位一致）。
    /// </summary>
    public static class MovementSystem
    {
        public static void Run(EntitySlot[] entities, uint[] aliveBitmap, in SimMapData map, in CombatValues values)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((aliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;

                ref EntitySlot e = ref entities[i];

                // 最大速度硬上限（水平合速度）——服务器代码兜底护栏（"配置只做
                // 软上限"）：配置错误/增益叠加/未来机制 bug 也不会让实体超速吹飞；用 SimMath 位级
                // 确定原语（MulAdd2 + Sqrt），护栏本身不引运行时差异。
                float velSq = SimMath.MulAdd2(e.Vel.X, e.Vel.X, e.Vel.Z, e.Vel.Z);
                float hardSq = CombatConfig.HardMaxSpeed * CombatConfig.HardMaxSpeed;
                if (velSq > hardSq)
                {
                    float scale = CombatConfig.HardMaxSpeed / SimMath.Sqrt(velSq);
                    e.Vel.X *= scale;
                    e.Vel.Z *= scale;
                }

                // XZ 平面位移（速度由 InputSystem 写入）
                e.Pos.X += e.Vel.X * SimConfig.Dt;
                e.Pos.Z += e.Vel.Z * SimConfig.Dt;

                // y 轴：重力积分 + 地面钳制（§3.5 原式）
                e.Vel.Y += values.Gravity * SimConfig.Dt;
                e.Pos.Y += e.Vel.Y * SimConfig.Dt;
                if (e.Pos.Y <= map.GroundY)
                {
                    e.Pos.Y = map.GroundY;
                    e.Vel.Y = 0f;
                }

                // 世界边界钳制（地图尺寸是判定半的一部分）
                if (e.Pos.X < -map.HalfWidth) e.Pos.X = -map.HalfWidth;
                if (e.Pos.X > map.HalfWidth) e.Pos.X = map.HalfWidth;
                if (e.Pos.Z < -map.HalfDepth) e.Pos.Z = -map.HalfDepth;
                if (e.Pos.Z > map.HalfDepth) e.Pos.Z = map.HalfDepth;
            }

            // 静态障碍去穿插（见类注释；无障碍时零成本短路）
            int obstacleCount = map.ObstacleCount;
            if (obstacleCount <= 0) return;

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((aliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;

                ref EntitySlot e = ref entities[i];
                for (int o = 0; o < obstacleCount; o++)
                {
                    ResolveObstacle(ref e, map.Obstacles[o]);
                }
            }
        }

        /// <summary>单个实体 × 单个障碍的 XZ 去穿插。y 区间闸先判（悬空/贴顶不碰撞）；
        /// 圆/盒分派走 switch——LiteSim 禁 == 比较（R3），枚举判别同房内惯例（RoomRuntime.Execute 同款）。</summary>
        private static void ResolveObstacle(ref EntitySlot e, in SimObstacle ob)
        {
            float bodyTop = e.Pos.Y + CombatConfig.HitscanHeight;
            float obTop = ob.Center.Y + ob.Height;
            if (e.Pos.Y >= obTop || bodyTop <= ob.Center.Y) return;

            float r = CombatConfig.BodyRadius;

            switch (ob.Kind)
            {
                case SimObstacleKind.Circle:
                {
                    float dx = e.Pos.X - ob.Center.X;
                    float dz = e.Pos.Z - ob.Center.Z;
                    float minDist = ob.Radius + r;
                    float d2 = SimMath.MulAdd2(dx, dx, dz, dz);
                    if (d2 >= minDist * minDist) return;            // 分离

                    float dist = SimMath.Sqrt(d2);
                    if (dist > 0.0000001f)
                    {
                        float scale = minDist / dist;              // 一步推到圆面（方向 = 障碍圆心 → 实体）
                        e.Pos.X = ob.Center.X + dx * scale;
                        e.Pos.Z = ob.Center.Z + dz * scale;
                    }
                    else
                    {
                        e.Pos.X = ob.Center.X + minDist;           // 圆心重合：确定性兜底，沿 +X 推出
                    }
                    return;
                }
                default:
                {
                    // Box：最近点法（盒外 → 沿法线推出到面外 r；盒内 → 最浅轴推出，X 优先固定 tie-break）
                    float px = SimMath.Clamp(e.Pos.X, ob.Center.X - ob.HalfX, ob.Center.X + ob.HalfX);
                    float pz = SimMath.Clamp(e.Pos.Z, ob.Center.Z - ob.HalfZ, ob.Center.Z + ob.HalfZ);
                    float dx2 = e.Pos.X - px;
                    float dz2 = e.Pos.Z - pz;
                    float d2b = SimMath.MulAdd2(dx2, dx2, dz2, dz2);
                    if (d2b >= r * r) return;                      // 与盒分离

                    if (d2b > 0f)
                    {
                        float dist = SimMath.Sqrt(d2b);
                        float scale = r / dist;
                        e.Pos.X = px + dx2 * scale;                 // 推到最近点外沿身位半径
                        e.Pos.Z = pz + dz2 * scale;
                        return;
                    }

                    // 中心在盒内：最浅穿透轴推出（相等时走 X——固定裁决，不得引入运行时差异）
                    float overlapX = ob.HalfX - SimMath.Abs(e.Pos.X - ob.Center.X);
                    float overlapZ = ob.HalfZ - SimMath.Abs(e.Pos.Z - ob.Center.Z);
                    if (overlapX <= overlapZ)
                    {
                        e.Pos.X = ob.Center.X + (e.Pos.X >= ob.Center.X ? ob.HalfX + r : -(ob.HalfX + r));
                    }
                    else
                    {
                        e.Pos.Z = ob.Center.Z + (e.Pos.Z >= ob.Center.Z ? ob.HalfZ + r : -(ob.HalfZ + r));
                    }
                    return;
                }
            }
        }
    }
}
