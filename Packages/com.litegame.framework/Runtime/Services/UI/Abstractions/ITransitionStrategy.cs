using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework.UI
{
    /// <summary>
    /// 转场策略（动效设计方案 附 A.2）：入场/离场动效的表现位。
    /// </summary>
    public interface ITransitionStrategy
    {
        UniTask PlayShow(UIForm form, CancellationToken ct);
        UniTask PlayClose(UIForm form, CancellationToken ct);

        /// <summary>
        /// 带终态的播放（可选实现位）。未实现的方法壳按 <see cref="MotionPlayback.Finished"/> 判定（未写终态 = 按完成处理）。
        /// </summary>
        UniTask PlayShow(UIForm form, MotionPlayback playback) => PlayShow(form, playback.Token);

        /// <summary>带终态的播放（离场；见 <see cref="PlayShow(UIForm, MotionPlayback)"/>）。</summary>
        UniTask PlayClose(UIForm form, MotionPlayback playback) => PlayClose(form, playback.Token);
    }
}
