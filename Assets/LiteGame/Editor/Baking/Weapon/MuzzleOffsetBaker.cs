using System.Globalization;
using System.Text;
using LiteGame.Toolchain;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 逻辑枪口偏移烘焙工具（烘焙工具箱「枪口偏移」页，或 run_script 调用；《固定斜视角射击方案专项设计》： "逻辑枪口 = 本体 + 朝向系常量偏移"，常量来源从手调改为**烘焙 prefab 锚点**）。
    /// </summary>
    public static class MuzzleOffsetBaker
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
                Transform rootT = scope.Contents.transform;
                Transform muzzle = BakeTargets.FindDeep(rootT, "Muzzle");
                if (muzzle == null) return "ABORT prefab 中未找到 Muzzle 锚点（Weapon_Rifle 下）";

                AnimationClip clip = BakeTargets.LoadReferenceClip();
                if (clip == null) return "ABORT 控制器载入失败或缺片段 '" + BakeTargets.ReferenceClip + "'（" + BakeTargets.ControllerPath + "）";

                clip.SampleAnimation(scope.Contents, 0f);      // t=0 净姿态（避开呼吸/后坐段）
                Vector3 off = rootT.InverseTransformPoint(muzzle.position);

                var inv = CultureInfo.InvariantCulture;
                var sets = new[]
                {
                    new[] { "muzzle_fwd", off.z.ToString("R", inv) },
                    new[] { "muzzle_right", off.x.ToString("R", inv) },
                    new[] { "muzzle_height", off.y.ToString("R", inv) },
                };
                bool dryRun = writeScope != null && writeScope.DryRun;
                if (!CombatGeomTableWriter.TryWrite(sets, !dryRun, writeScope, out string tableReport, out string error))
                    return "ABORT 写 #combatgeom 表失败：" + error;

                sb.AppendLine($"已烘焙：F={off.z:R} R={off.x:R} H={off.y:R}");
                sb.AppendLine($"来源：{BakeTargets.PrefabPath} 的 Weapon_Rifle/Muzzle @ {BakeTargets.ReferenceClip} t=0");
                sb.Append(tableReport);
            }
            return sb.ToString();
        }
    }
}
