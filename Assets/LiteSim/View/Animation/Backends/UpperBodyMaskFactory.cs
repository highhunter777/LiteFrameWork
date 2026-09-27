using UnityEngine;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 上半身 LayerMask 的构造工厂（《动画模块专项设计》§6"通道之间的 Mask、覆盖和混合关系由 Profile 固定"）。
    ///
    /// **为什么单独成类**：Mask 构造只依赖 Avatar 的 humanoid 声明，是**纯函数**——与
    /// `AnimatorAnimationBackend` 分离后不再需要 Animator 实例即可校验（也避免"图拓扑 + Mask 规则"
    /// 混在同一个类里）；后端只负责把它挂到混合层上。
    ///
    /// 开人体位（Body/Head/双臂/手指），关双腿与各 IK。前提 <c>avatar != null &amp;&amp; avatar.isHuman</c>
    /// ——否则返回 null（调用方据此**不声明** `LayeredChannels`，UpperBody 请求被显性拒绝，§4）。
    /// <b>Root 位关闭</b>：Body 已含髋部，避免叠加层连根位移一起覆盖基础层。
    /// 注意 <c>new AvatarMask()</c> 默认**全 false**——漏设即"叠加层无输出"的静默失效，故逐位显式设置。
    /// </summary>
    public static class UpperBodyMaskFactory
    {
        /// <summary>按 Avatar 构造上半身 Mask；非 humanoid 返回 null（不静默降级）。</summary>
        public static AvatarMask TryBuild(Avatar avatar)
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
            return mask;
        }
    }
}