using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 叠加层 LayerMask 的构造工厂（《动画模块专项设计》§6"通道之间的 Mask、覆盖和混合关系由 Profile 固定"）。
    ///
    /// **自动派生**：
    /// - humanoid 部位位：开 Body/Head/双臂/手指，关双腿与各 IK；<b>Root 位关闭</b>（Body 已含髋部，
    ///   避免叠加层连根位移一起覆盖基础层）；
    /// - transform 段：遍历动画机下整棵层级，**默认全部纳入**，仅三类不进——①动画机根自身（Root 位关闭）
    ///   与人形根骨（Hips 的动画机直系祖先——根位移只归基础层，transform 项渗入会污染根运动）；
    ///   ②Avatar 人形映射的**双腿子树**（<c>GetBoneTransform(LeftUpperLeg/RightUpperLeg)</c> 定根，
    ///   部位位已把腿关在叠加层外，腿上补插骨一并排除）；③Profile 登记的**排除子树**（布料域骨——
    ///   动画曲线与布料解算器争抢会让表现打架）。
    ///
    /// **为什么敢默认全收**：非人形映射骨（武器骨/补插骨等）对没有其曲线的片段是**惰性的**——
    /// 多纳入不产生任何输出，少纳入才出"开火枪不动、前臂拧歪"这类静默失效；人形映射骨走部位位，
    /// 其 transform 项对 humanoid 片段同样惰性（人形骨只存肌肉曲线，不存原始曲线）。rig 加骨/改名
    /// 由此零维护。前提 <c>avatar != null &amp;&amp; avatar.isHuman</c>——否则返回 null（调用方据此
    /// **不声明** <c>OverlayChannel</c>（叠加层能力位），Overlay 请求被显性拒绝，§4）。
    /// 注意 <c>new AvatarMask()</c> 默认**全 false**——漏设即"叠加层无输出"的静默失效，故逐位显式设置。
    /// </summary>
    public static class OverlayMaskFactory
    {
        /// <summary>按动画机构造叠加层 Mask；非 humanoid 返回 null（不静默降级）。
        /// <paramref name="excludedPaths"/> 为 Profile 登记的排除子树（可 null = 无排除；
        /// 子树语义：命中路径自身与全部后代一并排除，路径书写与片段曲线路径同规——相对动画机根）。</summary>
        public static AvatarMask TryBuild(Animator animator, IReadOnlyList<string> excludedPaths = null)
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;

            var mask = new AvatarMask();
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);

            if (excludedPaths != null)
            {
                foreach (var path in excludedPaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        throw new ArgumentException("叠加层 Mask 排除路径为空串", nameof(excludedPaths));
                }
            }

            // 腿子树根：Avatar 的人形映射给腿定位（rig 无关）；映射缺失时无腿可排，全收其余。
            var legRoots = new List<Transform>();
            foreach (var part in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg })
            {
                var leg = animator.GetBoneTransform(part);
                if (leg != null) legRoots.Add(leg);
            }

            // 排除路径存在性诊断：无效排除 = 布料域可能重新暴露给动画层——显性警告，不静默吞掉。
            if (excludedPaths != null)
            {
                foreach (var ex in excludedPaths)
                {
                    if (animator.transform.Find(ex) == null)
                        Debug.LogWarning($"[Anim][diag] 叠加层 Mask 排除路径在骨架上不存在：{ex}（Profile 与 rig 不匹配）");
                }
            }

            var rootBone = FindHumanRootBone(animator);
            var paths = new List<string>();
            Walk(animator.transform, string.Empty, inLegSubtree: false, rootBone, legRoots, excludedPaths, paths);

            // transform 段逐位显式激活（新 AvatarMask 全 false——与 humanoid 部位位同一"漏设即静默失效"约束）
            mask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, true);
            }
            return mask;
        }

        /// <summary>递归收集纳入路径：默认全收；腿子树与排除子树连后代一并跳过；人形根骨只下行不登记
        /// （根位移归基础层）。路径与片段曲线路径同规（相对动画机根，斜杠分层）。</summary>
        private static void Walk(Transform node, string path, bool inLegSubtree, Transform humanRootBone,
            List<Transform> legRoots, IReadOnlyList<string> excludedPaths, List<string> into)
        {
            foreach (Transform child in node)
            {
                string childPath = path.Length == 0 ? child.name : path + "/" + child.name;
                bool childInLeg = inLegSubtree || legRoots.Contains(child);
                if (childInLeg) continue;                              // 腿子树：整棵不进（含腿上补插骨）
                if (IsExcluded(childPath, excludedPaths)) continue;    // 排除子树：自身+后代全跳

                if (child != humanRootBone) into.Add(childPath);       // 人形根骨：只下行不登记
                Walk(child, childPath, childInLeg, humanRootBone, legRoots, excludedPaths, into);
            }
        }

        /// <summary>排除子树命中：路径相等，或以「排除路径 + /」为前缀（序数比较——骨名大小写敏感）。</summary>
        private static bool IsExcluded(string path, IReadOnlyList<string> excludedPaths)
        {
            if (excludedPaths == null) return false;
            for (int i = 0; i < excludedPaths.Count; i++)
            {
                var ex = excludedPaths[i];
                if (path.Length == ex.Length) return string.CompareOrdinal(path, ex) == 0;
                if (path.Length > ex.Length && path[ex.Length] == '/'
                    && string.CompareOrdinal(path, 0, ex, 0, ex.Length) == 0) return true;
            }
            return false;
        }

        /// <summary>人形根骨 = Hips 沿父链上溯到动画机直系的那一节（根位移语义归基础层——叠加层的
        /// transform 项不得触达它，防 humanoid 根运动曲线经 transform 段渗入叠加层）。
        /// Hips 未映射或直挂动画机（罕见）时返回 null（无从排，全收其余）。</summary>
        private static Transform FindHumanRootBone(Animator animator)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return null;
            var root = hips;
            while (root.parent != null && root.parent != animator.transform) root = root.parent;
            return root != animator.transform ? root : null;
        }
    }
}
