using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 上半身 LayerMask 的构造工厂（《动画模块专项设计》§6"通道之间的 Mask、覆盖和混合关系由 Profile 固定"）。
    ///
    /// **为什么单独成类**：Mask 构造只依赖 Avatar 的 humanoid 声明 + Profile 登记的附加骨路径，
    /// 是**纯函数**——与 <c>AnimatorAnimationBackend</c> 分离后不再需要 Animator 实例即可校验
    /// （也避免"图拓扑 + Mask 规则"混在同一个类里）；后端只负责把它挂到混合层上。
    ///
    /// 开人体位（Body/Head/双臂/手指），关双腿与各 IK；<b>Root 位关闭</b>：Body 已含髋部，
    /// 避免叠加层连根位移一起覆盖基础层。前提 <c>avatar != null &amp;&amp; avatar.isHuman</c>
    /// ——否则返回 null（调用方据此**不声明** <c>LayeredChannels</c>，UpperBody 请求被显性拒绝，§4）。
    ///
    /// **非人形附加骨**（<paramref name="extraTransformPaths"/>，由 Profile 登记）：
    /// <c>AvatarMaskBodyPart</c> 只覆盖 Avatar 映射的人形骨骼——骨架里独立存在的附加骨
    /// （武器骨、胸骨这类"add_"骨）不属任何人体位，必须经 **transform 路径段**进遮罩；
    /// 漏掉即上半身叠加层对这类骨的曲线整体不可见（实测形态：开火时手臂动、枪被基础层
    /// 钉在旧姿势）。路径书写与片段曲线路径同规（相对动画机根）。
    /// 注意 <c>new AvatarMask()</c> 默认**全 false**——漏设即"叠加层无输出"的静默失效，故逐位显式设置。
    /// </summary>
    public static class UpperBodyMaskFactory
    {
        /// <summary>按 Avatar 构造上半身 Mask；非 humanoid 返回 null（不静默降级）。
        /// <paramref name="extraTransformPaths"/> 为 Profile 登记的非人形附加骨路径
        /// （可 null = 无附加骨）；空串显性拒绝（登记面虽已校验，纯函数自身的前提条件同守一道）。</summary>
        public static AvatarMask TryBuild(Avatar avatar, IReadOnlyList<string> extraTransformPaths = null)
        {
            if (avatar == null || !avatar.isHuman) return null;

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

            // 附加骨走 transform 路径段：新增项逐位显式激活（新 AvatarMask 全 false 的同款静默失效面）。
            if (extraTransformPaths != null)
            {
                foreach (var path in extraTransformPaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        throw new ArgumentException("上半身附加 Mask 路径为空串", nameof(extraTransformPaths));
                }

                int baseCount = mask.transformCount;
                mask.transformCount = baseCount + extraTransformPaths.Count;
                for (int i = 0; i < extraTransformPaths.Count; i++)
                {
                    mask.SetTransformPath(baseCount + i, extraTransformPaths[i]);
                    mask.SetTransformActive(baseCount + i, true);
                }
            }
            return mask;
        }
    }
}