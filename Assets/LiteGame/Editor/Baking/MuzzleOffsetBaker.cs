using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 逻辑枪口偏移烘焙工具（烘焙工具箱「枪口偏移」页，或 run_script 调用；《固定斜视角射击方案专项设计》：
    /// "逻辑枪口 = 本体 + 朝向系常量偏移"，常量来源从手调改为**烘焙 prefab 锚点**）。
    ///
    /// **做什么**：把 <see cref="BakeTargets.PrefabPath"/> 的 `Weapon_Rifle/Muzzle` 锚点，在参考片段
    /// <see cref="BakeTargets.ReferenceClip"/> 的 t=0 姿态下采样，算出相对 prefab 根（SimView 旋转作用的视图根）的
    /// 局部偏移，输出为 Sim 坐标系的常量（视觉 +Z→Sim 前向、+X→Sim 右向、+Y→高度），
    /// 写入生成文件 <see cref="BakeTargets.MuzzleBakeOutput"/>（`MuzzleBake.g.cs`，勿手改——重跑本工具即重烘）。
    ///
    /// **为什么用参考动画姿态而不是 prefab 静态姿态**：武器挂点 `add_weapon_r` 被全部战斗片段驱动，
    /// prefab 静态姿态是 bind pose（持枪位完全不同）；射弹时刻的视觉姿态在 AimIdle 族
    /// （瞄准 = AimIdle；腰射开火窗 FireIdle 也以 AimIdle 循环填窗，
    /// 见 `CombatGirlsAnimationProfile`/`CombatAnimMachine`），故单套常量取 AimIdle t=0。
    ///
    /// **换枪/换姿态时**：改 <see cref="BakeTargets"/> 目标常量后重跑；
    /// 表化（tb_weapon per-weapon 列）落地前，本生成文件是双端编译期同值单源。
    /// </summary>
    public static class MuzzleOffsetBaker
    {
        public static string All()
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

                string text = BuildText(off);
                File.WriteAllText(BakeTargets.MuzzleBakeOutput, text);
                AssetDatabase.ImportAsset(BakeTargets.MuzzleBakeOutput);

                sb.AppendLine($"已烘焙：F={off.z:R} R={off.x:R} H={off.y:R}");
                sb.AppendLine($"来源：{BakeTargets.PrefabPath} 的 Weapon_Rifle/Muzzle @ {BakeTargets.ReferenceClip} t=0");
                sb.AppendLine($"输出：{BakeTargets.MuzzleBakeOutput}");
            }
            return sb.ToString();
        }

        /// <summary>生成文本（R 格式逐位往返；注释不含日期/批次痕迹——遵守注释卫生）。</summary>
        private static string BuildText(Vector3 off)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("// 本文件由 MuzzleOffsetBaker 生成（烘焙工具箱：菜单 LiteGame/烘焙工具箱，或 run_script 调用）——勿手改。");
            sb.AppendLine($"// 来源：{BakeTargets.PrefabPath} 的 Weapon_Rifle/Muzzle 锚点，");
            sb.AppendLine("//       参考动画姿态 AimIdle t=0（射弹时刻视觉姿态单族：瞄准=AimIdle、腰射开火窗亦以 AimIdle 填窗）。");
            sb.AppendLine("// 坐标：相对 prefab 根的视觉局部系（+Z 前 → Sim 前向；+X 右 → Sim 右向；+Y → 高度），");
            sb.AppendLine("//       与 SimView 的 FacingRotation（yaw → 旋转 90°−yaw）配套。");
            sb.AppendLine("namespace LiteSim");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>逻辑枪口常量（烘焙值；消费见 CombatConfig.MuzzleOrigin——三常量进 CombatConfigDigest）。</summary>");
            sb.AppendLine("    public static class MuzzleBake");
            sb.AppendLine("    {");
            sb.AppendLine($"        public const float Forward = {off.z.ToString("R", inv)}f;");
            sb.AppendLine($"        public const float Right = {off.x.ToString("R", inv)}f;");
            sb.AppendLine($"        public const float Height = {off.y.ToString("R", inv)}f;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
