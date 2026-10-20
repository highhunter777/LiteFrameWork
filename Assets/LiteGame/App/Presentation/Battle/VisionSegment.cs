using LiteSim;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 视野遮挡线段求交（《视野与遮挡专项设计》档 A1 的探测几何单源）：
    /// **角色眼位→目标身体采样**（显隐层）与**相机→目标柱顶**（相机补位渐隐）的有界线段与静态障碍
    /// 求交——真三维（盒＝三轴 slab；圆＝竖直圆柱＝XZ 圆求交＋Y 带 slab），不是 <see cref="SimRaycast"/>
    /// 的 2.5D 口径。
    ///
    /// **为什么不用 <see cref="SimRaycast.RaycastObstacles"/>**：那是弹道判定单源——XZ 射线＋
    /// "原点高度在障碍带内"闸，面向近水平弹道；视野探测线段从相机（≈10m 高）俯冲落到柱顶
    /// （1.8m），原点高度恒在低掩体带外，2.5D 闸会把所有低掩体判成"永不遮挡"——
    /// 而柱顶动态判据（蹲姿贴箱＝全遮蔽）正要判它们。故本件按**线段全程**求交，与弹道各司其职。
    ///
    /// **归档 A1 表现级边界**：只读 <see cref="SimMapData"/>（与瞄准激光同款只读源——地图单源
    /// 经 <c>BattleContext</c> 注入），判定/快照/协议零改动；求交结果只喂渲染开关与材质切换，
    /// 不进任何判定链（"掩体仍挡子弹"由 SimRaycast 原路径保证，本件不触）。
    /// 非确定性客户端瞬态（表现面），不走 <see cref="SimMath"/> 融合安全累积。
    /// </summary>
    public static class VisionSegment
    {
        /// <summary>
        /// 轴平行判定阈值（方向分量为单位向量）：|u| ≤ 本值视为垂直于该轴。
        /// 量级校准——10m 线段上该分量全程位移 &lt; 1e-5 m（≪ 毫米级量化精度），
        /// 平行分支与精确求交在探测语义上不可分辨；直接除会产生 1e6 量级伪 t。
        /// </summary>
        private const float AxisEpsilon = 1e-6f;

        /// <summary>退化线段判定（长度平方）：相机压在柱顶正上方时 a≈b——按"点在障碍内"口径处理。</summary>
        private const float DegenerateEpsilon2 = 1e-10f;

        /// <summary>
        /// 有界线段 [from, to] 是否被单个障碍挡住（起点在障碍内亦算挡住——与
        /// <see cref="SimRaycast"/> "起点在形状内 → t=0" 同口径）。
        /// </summary>
        public static bool Blocked(in SimObstacle obstacle, Vector3 from, Vector3 to)
        {
            float yBot = obstacle.Center.Y;
            float yTop = yBot + obstacle.Height;

            float dx = to.x - from.x;
            float dy = to.y - from.y;
            float dz = to.z - from.z;
            float len2 = dx * dx + dy * dy + dz * dz;
            if (len2 <= DegenerateEpsilon2)
                return PointInside(in obstacle, from, yBot, yTop);

            float len = Mathf.Sqrt(len2);
            float inv = 1f / len;
            float ux = dx * inv, uy = dy * inv, uz = dz * inv;   // 单位方向（t 参数 = 米）

            if (obstacle.Kind == SimObstacleKind.Circle)
            {
                return CylinderHit(from, to, ux, uy, uz, len,
                    obstacle.Center.X, obstacle.Center.Z, obstacle.Radius, yBot, yTop);
            }
            return BoxHit(from, ux, uy, uz, len, in obstacle, yBot, yTop);
        }

        /// <summary>
        /// 有界线段是否被图中任一障碍挡住（角色可见性/相机补位的求交口：不区分"哪个障碍挡"——
        /// 非渐隐障碍的遮挡由深度测试天然兜底，此处统一判定只影响渲染开关，不影响可见几何）。
        /// 空图（ObstacleCount=0）恒不挡。
        /// </summary>
        public static bool BlockedByAny(in SimMapData map, Vector3 from, Vector3 to)
        {
            for (int i = 0; i < map.ObstacleCount; i++)
                if (Blocked(in map.Obstacles[i], from, to)) return true;
            return false;
        }

        /// <summary>
        /// 线段 × 盒（三轴 slab，t∈[0,len]）：逐轴收敛 [tmin, tmax]；平行轴只判"原点在不在
        /// 该轴区间内"。区间收缩为空 ⇒ 从盒外掠过（含从顶/底掠过——这正是"站姿对低掩体
        /// 不渐隐"的几何落点：线段在盒顶上方穿过，Y slab 先拒）。
        /// </summary>
        private static bool BoxHit(Vector3 a, float ux, float uy, float uz, float len,
            in SimObstacle o, float yBot, float yTop)
        {
            float tmin = 0f;
            float tmax = len;

            // X 轴 slab
            if (Mathf.Abs(ux) > AxisEpsilon)
            {
                float invX = 1f / ux;
                float t1 = (o.Center.X - o.HalfX - a.x) * invX;
                float t2 = (o.Center.X + o.HalfX - a.x) * invX;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
            else if (a.x < o.Center.X - o.HalfX || a.x > o.Center.X + o.HalfX)
            {
                return false;                                        // 平行且在区间外——永不进入
            }

            // Z 轴 slab
            if (Mathf.Abs(uz) > AxisEpsilon)
            {
                float invZ = 1f / uz;
                float t1 = (o.Center.Z - o.HalfZ - a.z) * invZ;
                float t2 = (o.Center.Z + o.HalfZ - a.z) * invZ;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
            else if (a.z < o.Center.Z - o.HalfZ || a.z > o.Center.Z + o.HalfZ)
            {
                return false;
            }

            // Y 轴 slab（真三维化的关键——相机高起点俯冲线段在此与水平弹道分口径）
            if (Mathf.Abs(uy) > AxisEpsilon)
            {
                float invY = 1f / uy;
                float t1 = (yBot - a.y) * invY;
                float t2 = (yTop - a.y) * invY;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
            else if (a.y < yBot || a.y > yTop)
            {
                return false;
            }

            return tmin <= tmax;                                     // tmin≥0、tmax≤len 已由初值保证
        }

        /// <summary>
        /// 线段 × 竖直圆柱（XZ 圆求交＋Y 带 slab，t∈[0,len]）：XZ 求交按水平单位方向解出
        /// 进入/离开的水平距离后折回三维参数（t = t_xz / h——与 <see cref="SimRaycast"/>
        /// 障碍折回同式）；Y 带与圆区间有交即命中。水平线段（uy=0）沿用"原点高度在带内"闸。
        /// 垂直线段（相机正上方）XZ 恒定——垂距在半径内且 y 区间相交即命中。
        /// </summary>
        private static bool CylinderHit(Vector3 a, Vector3 b, float ux, float uy, float uz, float len,
            float cx, float cz, float radius, float yBot, float yTop)
        {
            float h2 = ux * ux + uz * uz;
            float mx = cx - a.x;
            float mz = cz - a.z;

            if (h2 <= AxisEpsilon * AxisEpsilon)
            {
                // 垂直线段：XZ 恒定（相机压在柱顶正上方）
                if (mx * mx + mz * mz > radius * radius) return false;
                float lo = a.y < b.y ? a.y : b.y;
                float hi = a.y < b.y ? b.y : a.y;
                return hi >= yBot && lo <= yTop;
            }

            float h = Mathf.Sqrt(h2);
            float invH = 1f / h;
            float ndx = ux * invH;                                   // 水平单位方向（圆求交按水平距离参数）
            float ndz = uz * invH;

            float proj = mx * ndx + mz * ndz;                        // 最近点参数（水平距离量纲）
            float m2 = mx * mx + mz * mz;
            float r2 = radius * radius;
            if (proj < 0f && m2 > r2) return false;                   // 圆心在身后且起点在柱外

            // 垂距直接算分量（p = m − proj·n̂，不用 |m|²−proj² 的大数消减——同 SimRaycast 的误差治理）
            float px = mx - proj * ndx;
            float pz = mz - proj * ndz;
            float c2 = px * px + pz * pz;
            if (c2 > r2) return false;

            float sq = Mathf.Sqrt(Mathf.Max(r2 - c2, 0f));
            float tIn = (proj - sq) * invH;                          // 折回三维参数
            float tOut = (proj + sq) * invH;
            if (tIn < 0f) tIn = 0f;                                  // 起点已在柱内（t=0 命中同口径）

            float tLo, tHi;
            if (uy > 0f) { tLo = (yBot - a.y) / uy; tHi = (yTop - a.y) / uy; }
            else if (uy < 0f) { tLo = (yTop - a.y) / uy; tHi = (yBot - a.y) / uy; }
            else
            {
                if (a.y < yBot || a.y > yTop) return false;          // 水平线段：原点高度在带内才可能有交
                tLo = float.MinValue;
                tHi = float.MaxValue;
            }

            float enter = tIn > tLo ? tIn : tLo;
            float leave = tOut < tHi ? tOut : tHi;
            return enter <= leave && enter <= len && leave >= 0f;
        }

        /// <summary>退化线段（a≈b）的点判定：XZ 落在截面内且 y 在障碍带内（起点在障碍内 = 挡）。</summary>
        private static bool PointInside(in SimObstacle o, Vector3 p, float yBot, float yTop)
        {
            if (p.y < yBot || p.y > yTop) return false;
            if (o.Kind == SimObstacleKind.Circle)
            {
                float dx = p.x - o.Center.X;
                float dz = p.z - o.Center.Z;
                return dx * dx + dz * dz <= o.Radius * o.Radius;
            }
            return Mathf.Abs(p.x - o.Center.X) <= o.HalfX
                && Mathf.Abs(p.z - o.Center.Z) <= o.HalfZ;
        }
    }
}
