using LiteFramework.Animation;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 视图侧播放后端扩展口：在框架通用播放契约上补"片段清单登记"（清单是 Unity 资源载体，
    /// 不能进框架 Animation 模块的纯逻辑面）。
    /// </summary>
    public interface IAnimationPlaybackBackend : IAnimationBackend
    {
        void RegisterManifest(AnimationClipManifest manifest);
    }
}
