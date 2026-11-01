using System.Globalization;
using System.Text;
using LiteGame.Toolchain;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 逻辑枪口偏移烘焙工具（烘焙工具箱「枪口偏移」页，或 run_script 调用；《固定斜视角射击方案专项设计》：
    /// "逻辑枪口 = 本体 + 朝向系常量偏移"，常量来源从手调改为**烘焙 prefab 锚点**）。
    ///
    /// **做什么**：把 <see cref="BakeTargets.PrefabPath"/> 的 `Weapon_Rifle/Muzzle` 锚点，在参考片段
    /// <see cref="BakeTargets.ReferenceClip"/> 的 t=0 姿态下采样，算出相对 prefab 根（SimView 旋转作用的视图根）的
    /// 局部偏移，写成判定几何表 `#combatgeom` 的 <c>muzzle_fwd</c>/<c>muzzle_right</c>/<c>muzzle_height</c>
    /// 三列（视觉 +Z→Sim 前向、+X→Sim 右向、+Y→高度）。
    ///
    /// **为什么用参考动画姿态而不是 prefab 静态姿态**：武器挂点 `add_weapon_r` 被全部战斗片段驱动，
    /// prefab 静态姿态是 bind pose（持枪位完全不同）；射弹时刻的视觉姿态在 AimIdle 族
    /// （瞄准 = AimIdle；腰射开火窗 FireIdle 也以 AimIdle 循环填窗，
    /// 见 `CombatGirlsAnimationProfile`/`CombatAnimMachine`），故单套常量取 AimIdle t=0。
    ///
    /// **换枪/换姿态时**：改 <see cref="BakeTargets"/> 目标常量后重跑；表值写完后需
    /// `Luban/gen.bat` → `python scripts/codegen/toolbox.py build-hash` 才进入判定与联机身份。
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
