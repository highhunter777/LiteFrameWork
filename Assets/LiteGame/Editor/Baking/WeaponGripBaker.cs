using System.Globalization;
using LiteView.Animation;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 持枪偏移烘焙工具（烘焙工具箱「持枪偏移」页，或 run_script 调用）：
    /// 在**参考持枪姿态**下采样，把「武器骨相对躯干骨」的局部变换写成
    /// <see cref="WeaponGripOffset"/> 资产（纯表现参数，勿手改——重跑本工具即重烘）。
    ///
    /// **为什么需要它**：换弹片段的枪骨曲线幅度远小于躯干（观感"身体在动、枪几乎不动"），
    /// 而枪骨是根骨（不随手），故换弹期需要按躯干做姿态覆写（见 WeaponGripConstraint）。
    /// 参考姿态取 <see cref="BakeTargets.ReferenceClip"/> t=0（该片段是持枪语境的单源——射弹时刻姿态族同源）。
    ///
    /// **偏移定义**：武器骨在跟随骨（躯干）局部空间下的位置与旋转。
    /// </summary>
    public static class WeaponGripBaker
    {
        /// <summary>跟随骨 = **持枪手**（<see cref="HumanBodyBones.RightHand"/>）。
        /// **为什么是右手而不是左手**：实测（Reload 片段逐帧手到枪距离）——枪骨 <c>add_weapon_r</c>
        /// 与右手骨世界位**完全相同**（静止差 0.6 mm），右手是枪的挂点父级；左手到枪恒 33.6 cm
        /// （片段里左手并未握枪）。跟随挂点手骨是唯一能让"枪永远在手"的结构解——跟随躯干会在
        /// 拉栓动作（右手位移 21 cm）时与挂点父级脱节，表现为"枪没对上手"。</summary>
        private const string FollowBone = "RightHand";

        public static string All()
        {
            using (var scope = PrefabEditScope.Open())
            {
                if (scope == null) return "ABORT prefab 载入失败：" + BakeTargets.PrefabPath;
                var contents = scope.Contents;

                var animator = contents.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                    return "ABORT 非 humanoid 角色（无躯干骨可跟随）";

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

                var asset = AssetDatabase.LoadAssetAtPath<WeaponGripOffset>(BakeTargets.GripAssetOutput);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<WeaponGripOffset>();
                    AssetDatabase.CreateAsset(asset, BakeTargets.GripAssetOutput);
                }
                asset.Configure(followPath, weaponPath, localPos, localEuler, 1f);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                var inv = CultureInfo.InvariantCulture;
                return "已烘焙：" + BakeTargets.GripAssetOutput
                    + "\n  follow=" + followPath + " weapon=" + weaponPath
                    + "\n  localPos=" + localPos.ToString("R", inv)
                    + "\n  localEuler=" + localEuler.ToString("R", inv)
                    + "\n  torsoWeight=1（真机可在资产面板调 0..1）";
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
