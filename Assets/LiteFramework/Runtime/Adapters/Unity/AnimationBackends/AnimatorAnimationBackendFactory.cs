using LiteFramework.Animation;
using UnityEngine;

namespace LiteFramework.Animation
{
    /// <summary>Animator/PlayableGraph 后端的构造实现：默认混合时长与 Profile 的 Mask 排除面在此收口。</summary>
    public sealed class AnimatorAnimationBackendFactory : IAnimationBackendFactory
    {
        public IAnimationPlaybackBackend Create(Animator animator, AnimationProfile profile)
            => new AnimatorAnimationBackend(animator,
                AnimatorAnimationBackend.DefaultBlendSeconds, profile?.OverlayMaskExclusions);
    }
}
