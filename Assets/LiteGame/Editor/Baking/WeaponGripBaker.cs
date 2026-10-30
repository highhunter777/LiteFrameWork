using System.Globalization;
using System.Text;
using LiteGame.Toolchain;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 持枪偏移烘焙工具（烘焙工具箱「持枪偏移」页，或 run_script 调用）：
    /// 在**参考持枪姿态**下采样，把「武器骨相对持枪手」的局部变换烘成
    /// <see cref="BakeTargets.GripBakeOutput"/>（`GripBake.g.cs` 常量，勿手改——重跑本工具即重烘）。
    ///
    /// **为什么需要它**：枪骨是根骨（不随手），换弹期需要把武器骨钉回持枪语境
    /// （见 WeaponGripConstraint 的换弹上半身叠加覆写）。参考姿态取 <see cref="BakeTargets.ReferenceClip"/>
    /// t=0（该片段是持枪语境的单源——射弹时刻姿态族同源）。
    ///
    /// **偏移定义**：武器骨在跟随骨（持枪手 RightHand）局部空间下的位置与旋转。
    ///
    /// **为什么跟随右手**：实测（Reload 片段逐帧手到枪距离）——枪骨 <c>add_weapon_r</c>
    /// 与右手骨世界位**完全相同**（静止差 0.6 mm），右手是枪的挂点父级；左手到枪恒 33.6 cm
    /// （片段里左手并未握枪）。跟随挂点手骨是唯一能让"枪永远在手"的结构解——跟随躯干会在
    /// 拉栓动作（右手位移 21 cm）时与挂点父级脱节，表现为"枪没对上手"。
    ///
    /// **落点**：常量文件落 LiteView（纯表现参数，刻意不进 LiteSim/Core/Scripts 的 BuildHash 源集）。
    /// </summary>
    public static class WeaponGripBaker
    {
        public static string All()
        {
            return All(null);
        }

        public static string All(ToolWriteScope writeScope)
        {
            using (var scope = PrefabEditScope.Open())
            {
                if (scope == null) return "ABORT prefab 载入失败：" + BakeTargets.PrefabPath;
                var contents = scope.Contents;

                var animator = contents.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                    return "ABORT 非 humanoid 角色（无持枪手骨可跟随）";

                var follow = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var weapon = BakeTargets.FindDeep(animator.transform, "add_weapon_r");
                if (follow == null || weapon == null)
                    return "ABORT 缺骨（follow 或 add_weapon_r 未找到）";

                AnimationClip clip = BakeTargets.LoadReferenceClip();
                if (clip == null) return "ABORT 控制器载入失败或缺片段 '" + BakeTargets.ReferenceClip + "'（" + BakeTargets.ControllerPath + "）";

                clip.SampleAnimation(contents, 0f);

                // SampleAnimation 只写 Transform，场景世界矩阵是延迟刷新的（同帧直接读会拿到旧值——
                // 经典坑：曾导致偏移烘焙出 1 米量级的错值）。此处显式刷新，保证读到采样后的真实姿态。
                Physics.SyncTransforms();

                Vector3 localPos = follow.InverseTransformPoint(weapon.position);
                Vector3 localEuler = (Quaternion.Inverse(follow.rotation) * weapon.rotation).eulerAngles;

                string followPath = Rel(animator.transform, follow);
                string weaponPath = Rel(animator.transform, weapon);

                string write = BakeTargets.WriteGenerated(BakeTargets.GripBakeOutput, nameof(WeaponGripBaker),
                    new[]
                    {
                        $"// 来源：{BakeTargets.PrefabPath} 的 RightHand/add_weapon_r 局部变换 @ {BakeTargets.ReferenceClip} t=0（烘焙工具箱共享参考姿态）。",
                        "// 语义：武器骨相对跟随骨（持枪手 RightHand）的局部位姿——换弹上半身叠加期姿态覆写（WeaponGripConstraint）的唯一来源；",
                        "//       跟随右手而非躯干：add_weapon_r 与右手世界位全同（差 0.6mm）、右手是枪挂点父级——跟躯干会在拉栓（右手位移 21cm）时脱节。",
                        "// 落点：LiteView（纯表现参数，刻意不进 LiteSim/Core/Scripts 的 BuildHash 源集——表现值变化不踢两端握手）。",
                        "// 幅度：TorsoWeight 烘焙值 1（0=不约束 1=完全跟随）；对局内实时调参走 GM 面板（GripTuning 覆写，不落盘）。",
                    }, BuildBody(followPath, weaponPath, localPos, localEuler), writeScope);

                var inv = CultureInfo.InvariantCulture;
                return "已烘焙：follow=" + followPath + " weapon=" + weaponPath
                    + "\n  localPos=" + localPos.ToString("R", inv)
                    + "\n  localEuler=" + localEuler.ToString("R", inv)
                    + "\n  torsoWeight=1（对局内调参：GM 面板「持枪幅度」滑杆，不落盘）\n"
                    + write;
            }
        }

        /// <summary>常量体（R 格式逐位往返；注释不含日期/批次痕迹——遵守注释卫生）。</summary>
        private static string BuildBody(string followPath, string weaponPath, Vector3 localPos, Vector3 localEuler)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("namespace LiteGame.View.Animation");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>持枪偏移常量（烘焙值；换弹期武器骨跟随持枪手——消费见 WeaponGripConstraint/GripTuning）。</summary>");
            sb.AppendLine("    public static class GripBake");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>跟随骨路径（相对 Animator 根）：持枪手。</summary>");
            sb.AppendLine($"        public const string FollowPath = \"{followPath}\";");
            sb.AppendLine("        /// <summary>武器骨路径（相对 Animator 根；枪模型 Weapon_Rifle 是它的子节点，枪口火光/激光挂点随之跟随）。</summary>");
            sb.AppendLine($"        public const string WeaponPath = \"{weaponPath}\";");
            sb.AppendLine("        /// <summary>枪骨相对跟随骨的局部位置（持枪位偏移，参考姿态烘焙）。</summary>");
            sb.AppendLine($"        public const float LocalPosX = {localPos.x.ToString("R", inv)}f;");
            sb.AppendLine($"        public const float LocalPosY = {localPos.y.ToString("R", inv)}f;");
            sb.AppendLine($"        public const float LocalPosZ = {localPos.z.ToString("R", inv)}f;");
            sb.AppendLine("        /// <summary>枪骨相对跟随骨的局部旋转（欧拉角，度；消费侧 Quaternion.Euler）。</summary>");
            sb.AppendLine($"        public const float LocalEulerX = {localEuler.x.ToString("R", inv)}f;");
            sb.AppendLine($"        public const float LocalEulerY = {localEuler.y.ToString("R", inv)}f;");
            sb.AppendLine($"        public const float LocalEulerZ = {localEuler.z.ToString("R", inv)}f;");
            sb.AppendLine("        /// <summary>幅度缩放：0 = 不约束（用片段曲线）、1 = 完全跟随持枪手。</summary>");
            sb.AppendLine("        public const float TorsoWeight = 1f;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Rel(Transform root, Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Insert(0, c.name);
            return string.Join("/", parts.ToArray());
        }
    }
}
