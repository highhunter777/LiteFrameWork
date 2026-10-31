using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LiteGame.Toolchain;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 身位圆柱烘焙工具（烘焙工具箱「身位圆柱」页，与 <see cref="MuzzleOffsetBaker"/> 同构）：把
    /// <see cref="BakeTargets.PrefabPath"/> 根上的 **CharacterController**（美术可调、可视、唯一）写成
    /// 判定几何表 `#combatgeom` 的 <c>body_radius</c>/<c>hitscan_height</c> 两列——服务端命中柱 /
    /// 移动去穿插 / 本地视图 CC 三方共用同一份表值（"改一次 CC = 判定联动"）。
    ///
    /// **语义映射**：CC 是胶囊（半球端），Sim 物理体是竖直圆柱 <c>[pos.Y, pos.Y + height]</c>——
    /// 烘焙要求 <c>center.y == height/2</c>（圆柱底 = 脚底）；不满足即 ABORT（不烘半个身位）。
    ///
    /// **夹具**：同一次动作把 radius/height 快照写到 <see cref="BakeTargets.BodyCcFixturePath"/>
    /// （入库，供三方对账与地图烘焙的出生点校验；**Assets 之外的普通文件**，不生成 .meta）。
    /// 顺序固定为**先写夹具、再写表**：夹具失败不碰表；表失败时报告会明示夹具已更新并给出还原方式。
    ///
    /// **口径**：**物理半径/高**是角色资产派生几何——CC 为单源（判定面经 `#combatgeom` 装载进
    /// <see cref="LiteSim.CombatValues.Geometry"/>）；**命中柱半径是另一根量**（`hitscan_radius`，
    /// 判定宽容裁决常量，**非本工具产物**）；头部带下沿 = <c>hitscan_height × head_ratio</c>
    /// （比例单源，随身高自动缩放）。
    /// </summary>
    public static class BodyCylinderBaker
    {
        public static string All()
        {
            return All(null);
        }

        public static string All(ToolWriteScope writeScope)
        {
            var sb = new StringBuilder();
            using (var scope = PrefabEditScope.Open())
            {
                if (scope == null) return "ABORT prefab 载入失败：" + BakeTargets.PrefabPath;
                var cc = scope.Contents.GetComponent<CharacterController>();
                if (cc == null) return "ABORT prefab 根无 CharacterController（身位烘焙的唯一来源）";

                float baseOffset = cc.center.y - cc.height * 0.5f;
                if (Mathf.Abs(baseOffset) > 1e-4f)
                    return $"ABORT CC center.y({cc.center.y:F4}) ≠ height/2({cc.height * 0.5f:F4})——圆柱底不在脚底，不烘（对齐后再跑）";

                bool dryRun = writeScope != null && writeScope.DryRun;
                string fixtureWrite;
                try
                {
                    fixtureWrite = WriteFixture(BuildFixture(cc.radius, cc.height), dryRun);
                }
                catch (Exception exception)
                {
                    return "ABORT 写身位夹具失败：" + BakeTargets.BodyCcFixturePath
                        + "（" + exception.Message + "）——未写表";
                }

                var sets = new[]
                {
                    new[] { "body_radius", cc.radius.ToString("R", CultureInfo.InvariantCulture) },
                    new[] { "hitscan_height", cc.height.ToString("R", CultureInfo.InvariantCulture) },
                };
                if (!CombatGeomTableWriter.TryWrite(sets, !dryRun, writeScope, out string tableReport, out string error))
                {
                    string fixtureNote = dryRun
                        ? string.Empty
                        : "\n身位夹具已更新：" + BakeTargets.BodyCcFixturePath
                            + "（还原方式：git checkout -- " + BakeTargets.BodyCcFixturePath + "）";
                    return "ABORT 写 #combatgeom 表失败：" + error + fixtureNote;
                }

                sb.AppendLine($"已烘焙：Radius={cc.radius:R} Height={cc.height:R}（center.y 校验通过）");
                sb.AppendLine($"来源：{BakeTargets.PrefabPath} 的 CharacterController");
                sb.AppendLine(fixtureWrite);
                sb.Append(tableReport);
            }
            return sb.ToString();
        }

        /// <summary>夹具 JSON（R 格式逐位往返；LF 行尾；UTF-8 无 BOM）。</summary>
        private static string BuildFixture(float radius, float height)
        {
            var inv = CultureInfo.InvariantCulture;
            return "{\"sourcePrefab\":\"" + BakeTargets.PrefabPath
                + "\",\"radius\":" + radius.ToString("R", inv)
                + ",\"height\":" + height.ToString("R", inv) + "}\n";
        }

        /// <summary>write-if-changed 夹具落盘（Assets 之外的普通文件）：内容一致不动文件；变化才写。</summary>
        private static string WriteFixture(string text, bool dryRun)
        {
            string path = CombatGeomTableWriter.FixtureFullPath;
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            bool exists = File.Exists(path);
            byte[] before = exists ? File.ReadAllBytes(path) : null;
            if (before != null && before.SequenceEqual(bytes)) return "up to date：" + BakeTargets.BodyCcFixturePath;
            if (dryRun) return "planned：" + BakeTargets.BodyCcFixturePath;

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, bytes);
            return "written：" + BakeTargets.BodyCcFixturePath;
        }
    }
}
