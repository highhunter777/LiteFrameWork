using LiteFramework.Animation;
using LiteView.Animation;
using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 动画播放后端的构造口：视图模块只认契约，具体引擎后端由适配层提供并在装配根注入。
    /// </summary>
    public interface IAnimationBackendFactory
    {
        IAnimationPlaybackBackend Create(Animator animator, AnimationProfile profile);
    }
}
