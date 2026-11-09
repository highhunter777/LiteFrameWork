using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework.UI
{
    /// <summary>
    /// 可选：Replace（同组全屏互斥切换）的"两组并发"定制位（《UI扩展能力设计》§1.5.5）。
    /// </summary>
    public interface IReplaceTransition
    {
        UniTask PlayReplace(UIForm outgoing, UIForm incoming, CancellationToken ct);

        /// <summary>带终态的 Replace（可选实现位；见 <see cref="ITransitionStrategy.PlayShow(UIForm, MotionPlayback)"/>）。</summary>
        UniTask PlayReplace(UIForm outgoing, UIForm incoming, MotionPlayback playback) => PlayReplace(outgoing, incoming, playback.Token);
    }
}
