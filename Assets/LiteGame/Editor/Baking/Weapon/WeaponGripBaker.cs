using System;
using System.Globalization;
using LiteGame.Toolchain;
using UnityEditor;
using UnityEngine;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 持枪偏移烘焙工具（烘焙工具箱「持枪偏移」页，或 run_script 调用）： 在**参考持枪姿态**下采样。
    /// </summary>
    public static class WeaponGripBaker
    {
        /// <summary>烘焙幅度（1 = 完全跟随持枪手）；对局内实时调参走 GM 面板（GripTuning 覆写，不落盘）。</summary>
        private const float BakedTorsoWeight = 1f;

        public static string All()
        {
            return All(null);
        }

        public static string All(ToolWriteScope writeScope)
        {
            if (!string.Equals(BakeTargets.GripBinOutput, WeaponGripCatalog.DefaultAssetPath, StringComparison.Ordinal))
            {
                return "ABORT 产物路径与运行时装载路径不一致：" + BakeTargets.GripBinOutput
                    + " ≠ " + WeaponGripCatalog.DefaultAssetPath;
            }

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

                byte[] bytes = WeaponGripCatalog.Encode(followPath, weaponPath, localPos, localEuler, BakedTorsoWeight);
                WeaponGripCatalog catalog = WeaponGripCatalog.Load(bytes);   // 同一格式自校验（写入前即失败）
                string write = BakeTargets.WriteArtifactBytes(BakeTargets.GripBinOutput, bytes, writeScope);

                var inv = CultureInfo.InvariantCulture;
                return "已导出：" + BakeTargets.GripBinOutput
                    + "\n  follow=" + catalog.FollowPath
                    + "\n  weapon=" + catalog.WeaponPath
                    + "\n  localPos=" + catalog.LocalPosition.ToString("R", inv)
                    + "\n  localEuler=" + catalog.LocalEuler.ToString("R", inv)
                    + "\n  torsoWeight=" + catalog.TorsoWeight.ToString("R", inv)
                    + "（对局内调参：GM 面板「持枪幅度」滑杆，不落盘）"
                    + "\n  bytes=" + bytes.Length
                    + "\n  sha256=" + catalog.Digest + "\n"
                    + write
                    + "\n  数据产物：改后无需重生成 BuildHash，但需重发内容包（Config 收集组）。";
            }
        }

        private static string Rel(Transform root, Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Insert(0, c.name);
            return string.Join("/", parts.ToArray());
        }
    }
}
