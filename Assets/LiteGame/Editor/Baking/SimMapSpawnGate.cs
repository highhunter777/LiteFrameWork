using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LiteSim;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 地图烘焙的**身位几何闸**：身位尺寸三方对账（prefab CC ≡ `#combatgeom` 表行 ≡ 入库夹具） 与出生点合法性校验（不落障碍 / 离障碍不小于身位半径 / 不越边界）。
    /// </summary>
    internal static class SimMapSpawnGate
    {
        /// <summary>
        /// 身位几何三方对账：prefab CharacterController ≡ `#combatgeom` 表行（body_radius/hitscan_height） ≡ 入库夹具 <see cref="BakeTarge。
        /// </summary>
        public static bool TryRead(out float radius, out float height, out string error)
        {
            radius = 0f;
            height = 0f;
            error = null;

            using (var scope = PrefabEditScope.Open(BakeTargets.PrefabPath))
            {
                if (scope == null)
                {
                    error = "缺玩家 prefab：" + BakeTargets.PrefabPath + "（本地还原后重试）";
                    return false;
                }
                var cc = scope.Contents.GetComponent<CharacterController>();
                if (cc == null)
                {
                    error = "prefab 根无 CharacterController：" + BakeTargets.PrefabPath;
                    return false;
                }
                radius = cc.radius;
                height = cc.height;
            }
            if (!(radius > 0f) || !(height > 0f))
            {
                error = "prefab CharacterController 的半径/高非正";
                return false;
            }

            if (!CombatGeomTableWriter.TryReadRow(out Dictionary<string, string> row, out error)) return false;
            if (!CombatGeomTableWriter.TryGetFloat(row, "body_radius", out float tableRadius, out error)) return false;
            if (!CombatGeomTableWriter.TryGetFloat(row, "hitscan_height", out float tableHeight, out error)) return false;
            if (!CombatGeomTableWriter.TryReadBodyCcFixture(out string sourcePrefab,
                    out float fixtureRadius, out float fixtureHeight, out error))
                return false;

            string prefabText = "prefab CC radius=" + radius.ToString("R", CultureInfo.InvariantCulture)
                + " height=" + height.ToString("R", CultureInfo.InvariantCulture);
            if (tableRadius != radius || tableHeight != height)
            {
                error = "身位几何与 #combatgeom 表不一致（" + prefabText
                    + "；表行 body_radius=" + tableRadius.ToString("R", CultureInfo.InvariantCulture)
                    + " hitscan_height=" + tableHeight.ToString("R", CultureInfo.InvariantCulture)
                    + "）——先跑身位圆柱烘焙";
                return false;
            }
            if (fixtureRadius != radius || fixtureHeight != height)
            {
                error = "身位几何与入库夹具不一致（" + prefabText
                    + "；夹具 radius=" + fixtureRadius.ToString("R", CultureInfo.InvariantCulture)
                    + " height=" + fixtureHeight.ToString("R", CultureInfo.InvariantCulture)
                    + "）——先跑身位圆柱烘焙";
                return false;
            }
            if (!string.Equals(sourcePrefab, BakeTargets.PrefabPath, StringComparison.Ordinal))
            {
                error = "身位夹具来源 prefab 不匹配（夹具 " + sourcePrefab + " ≠ " + BakeTargets.PrefabPath
                    + "）——先跑身位圆柱烘焙";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 出生点合法性（**布点可调的安全闸**——多图批；**逐图通用，不依赖作者记得留余量**）： ① **不落障碍内**（含 y 区间的 2.5D 判据：出生点站立区间与障碍 y 区间相交且 XZ 落在圆/AABB 内 = 。）。
        /// </summary>
        public static void Validate(List<(string Name, SimVector3 Pos)> spawns,
            List<SimMapBaker.ObstacleDraft> obstacles, float groundY, float halfWidth, float halfDepth,
            float bodyRadius, float bodyHeight, StringBuilder errors)
        {
            for (int i = 0; i < spawns.Count; i++)
            {
                (string name, SimVector3 p) = spawns[i];
                if (Mathf.Abs(p.X) > halfWidth || Mathf.Abs(p.Z) > halfDepth)
                {
                    errors.Append(name).Append("：出生点越界（").Append(p.X).Append(',').Append(p.Z)
                          .Append(" 超出半宽 ").Append(SimMapDigest.Fmt(halfWidth))
                          .Append(" / 半深 ").Append(SimMapDigest.Fmt(halfDepth)).Append("）\n");
                    continue;
                }

                // 索引读取（List 索引器返回值不是变量——readonly struct 字段取址需先落局部）
                bool blocked = false;
                for (int o = 0; o < obstacles.Count; o++)
                {
                    SimMapBaker.ObstacleDraft draft = obstacles[o];
                    if (!OverlapsObstacle(in p, groundY, in draft.Obstacle, 0f, bodyHeight)) continue;
                    errors.Append(name).Append("：出生点落在障碍内（").Append(draft.Name)
                          .Append("；出生点 y=").Append(SimMapDigest.Fmt(p.Y)).Append("）\n");
                    blocked = true;
                    break;
                }
                if (blocked) continue;                                 // 已点名：不再重复报"离障碍过近"

                for (int o = 0; o < obstacles.Count; o++)
                {
                    SimMapBaker.ObstacleDraft draft = obstacles[o];
                    if (!OverlapsObstacle(in p, groundY, in draft.Obstacle, bodyRadius, bodyHeight)) continue;
                    errors.Append(name).Append("：出生点离障碍过近（").Append(draft.Name)
                          .Append("；净距 < 身位半径 ").Append(SimMapDigest.Fmt(bodyRadius))
                          .Append("m——出生首帧会被去穿插推出）\n");
                    break;
                }
            }
        }

        /// <summary>出生点是否与障碍相交（2.5D：站立占用 [y, y+<paramref name="bodyHeight"/>] 与障碍 y 区间相交
        /// ＋ XZ 距离小于 <paramref name="margin"/>）：margin=0 = "落在截面内"；margin=身位半径 = "出生即被
        /// 移动去穿插推出"。盒按最近点欧氏距离（角点精确、不是 AABB 各轴放宽），圆按半径外扩；
        /// 站立高度取身位圆柱（prefab CC 单源，经表行/夹具对账后传入）。</summary>
        public static bool OverlapsObstacle(in SimVector3 p, float groundY, in SimObstacle o,
            float margin, float bodyHeight)
        {
            float bottom = p.Y - groundY;                  // 相对地面高度（地面非 0 的地图同口径）
            float top = bottom + bodyHeight;
            if (top <= o.Center.Y || bottom >= o.Center.Y + o.Height) return false;   // y 区间不相交
            if (o.Kind == SimObstacleKind.Circle)
            {
                float dx = p.X - o.Center.X, dz = p.Z - o.Center.Z;
                float r = o.Radius + margin;
                return dx * dx + dz * dz < r * r;
            }
            float ex = Mathf.Max(Mathf.Abs(p.X - o.Center.X) - o.HalfX, 0f);
            float ez = Mathf.Max(Mathf.Abs(p.Z - o.Center.Z) - o.HalfZ, 0f);
            if (ex <= 0f && ez <= 0f) return true;         // 点已在盒内（margin 任意）
            return ex * ex + ez * ez < margin * margin;    // 盒外：最近点距离 < margin
        }
    }
}
