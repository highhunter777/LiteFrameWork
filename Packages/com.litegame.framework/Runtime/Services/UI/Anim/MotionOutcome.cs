using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework.UI
{
    /// <summary>
    /// 表现播放的结束原因（《动画模块专项设计》§10"UI 的播放终态与 UI 操作结果分层映射"； 《UI框架总设计》§6.3 复位契约）。
    /// </summary>
    public enum MotionOutcome
    {
        /// <summary>正常播完（到达定义的结束边界）。</summary>
        Completed = 0,
        /// <summary>被调用方主动取消（超时/权威取消/关闭——策略须已复位到目标视觉）。</summary>
        Cancelled = 1,
        /// <summary>策略异常完成。</summary>
        Failed = 2,
    }

    /// <summary>
    /// 动效播放结果容器（可变引用型——策略内部写回，调用方事后读）。
    /// </summary>
    public sealed class MotionPlayback
    {
        private readonly CancellationToken _token;

        public MotionPlayback(CancellationToken token = default) => _token = token;

        /// <summary>调用方（宿主/超时）请求取消的令牌（无取消源时恒为 None）。</summary>
        public CancellationToken Token => _token;

        /// <summary>已收终态？</summary>
        public bool Finished { get; private set; }

        /// <summary>终态原因（<see cref="Finished"/> 为 false 时未定义）。</summary>
        public MotionOutcome Outcome { get; private set; } = MotionOutcome.Completed;

        /// <summary>收终态（**幂等**：只记第一次——重复 Finish 不覆盖，防迟到回调改写结果）。</summary>
        public void Finish(MotionOutcome outcome)
        {
            if (Finished) return;
            Finished = true;
            Outcome = outcome;
        }
    }
}
