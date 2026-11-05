using System;

namespace LiteFramework.Animation
{
    /// <summary>一次推进判定的动作（<see cref="ClipCompletionTracker.Advance"/> 的输出）。</summary>
    public enum ClipTickAction
    {
        /// <summary>继续播放（未到边界）。</summary>
        None = 0,
        /// <summary>定义为循环、资产未自回绕：调用方须把片段时间重设为 <see cref="ClipCompletionTracker.WrapTarget"/>。</summary>
        Wrap = 1,
        /// <summary>到达结束边界（或检测到资产回卷）→ 该通道收 Completed，**恰好一次**。</summary>
        Completed = 2,
    }

    /// <summary>
    /// 片段结束边界的**纯判定逻辑**（Completed 的判定方式必须写入定义，
    /// 不读资产 loop 设置；时间归属分离）。
    ///
    /// **为什么单独成类**：判定只有三个输入——定义是否循环、片段时长、上一帧时间——不含任何引擎对象，
    /// 故与 `AnimatorAnimationBackend` 分离后：① 后端只管引擎调用（`GetTime`/`SetTime`），
    /// 判定不与图拓扑混在同一个类里；② 四个象限（定义为循环×资产循环/不循环）可用假时长直接单测，
    /// 不需要 Animator 与真资产。
    ///
    /// 时间用 <see cref="double"/> 接收（`AnimationClipPlayable.GetTime()` 的原生类型），
    /// 内部按 `float` 往返以与片段时长同精度比较。
    /// </summary>
    public struct ClipCompletionTracker
    {
        /// <summary>定义是否循环（**读定义，不读资产**）。</summary>
        public bool Loop;

        /// <summary>片段时长（秒；≤0 视为时长未知，不做任何判定）。</summary>
        public float Length;

        /// <summary>该通道是否已收过 Completed（保证恰好一次）。</summary>
        public bool Completed;

        /// <summary>最近一次 <see cref="Advance"/> 建议的回绕目标（仅返回 <see cref="ClipTickAction.Wrap"/> 时有意义）。</summary>
        public float WrapTarget;

        private float _previousTime;

        /// <summary>按提交参数重置（新片段落位时调用）。</summary>
        public void Reset(float length, bool loop, float startTime)
        {
            Length = length;
            Loop = loop;
            Completed = false;
            WrapTarget = 0f;
            _previousTime = startTime;
        }

        /// <summary>
        /// 推进一次判定。`time` 为当前片段时间、`epsilon` 为回卷判定容差（秒）。
        /// 循环定义**永不**返回 <see cref="ClipTickAction.Completed"/>；一次性定义在到达边界或检测到
        /// 资产自回绕（时间倒退）时返回一次 Completed，之后恒为 None。
        /// </summary>
        public ClipTickAction Advance(double time, float epsilon)
        {
            if (Completed) return ClipTickAction.None;      // 恰好一次
            if (Length <= 0f) return ClipTickAction.None;   // 时长未知：不判定

            float t = (float)time;

            if (Loop)
            {
                if (t >= Length)
                {
                    // 定义为循环但资产不循环：手工回绕（不依赖 FBX 导入设置）
                    float wrapped = t - Length * (float)Math.Floor((double)(t / Length));
                    WrapTarget = wrapped;
                    _previousTime = wrapped;
                    return ClipTickAction.Wrap;
                }
                _previousTime = t;
                return ClipTickAction.None;
            }

            // 一次性定义：到边界，或资产在循环（检测到回卷）→ 都算自然结束
            bool wrappedAround = t + epsilon < _previousTime;
            _previousTime = t;
            if (wrappedAround || t >= Length - epsilon)
            {
                Completed = true;
                return ClipTickAction.Completed;
            }
            return ClipTickAction.None;
        }
    }
}