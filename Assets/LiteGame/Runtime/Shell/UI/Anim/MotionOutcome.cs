using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame
{
    /// <summary>
    /// 表现播放的结束原因（《动画模块专项设计》§10"UI 的播放终态与 UI 操作结果分层映射"；
    /// 《UI框架总设计》§6.3 复位契约）。
    ///
    /// **为什么需要独立类型**：策略以 <see cref="ITransitionStrategy"/> 的 <c>UniTask</c> 返回时，
    /// "正常播完"与"被中途杀掉"在 Task 层不可区分（两者都正常返回）。而二者对页面收尾的含义完全相反：
    /// 前者是播放完成，后者必须**先复位再降级**（§6.3"不得只吞取消留着半截动画"）。
    /// 用独立枚举表达，避免用异常表达控制流（无异常也要能分类）。
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
    /// 一次表现播放的句柄（可取消 + 带原因的终态）——动画专项 §10"原语专注视觉效果，
    /// 适配层管理 UIClock、顶层 Tween、**结束原因**和属性恢复"的"结束原因"出口。
    ///
    /// 与角色动画的 <c>AnimationHandle</c> 分层不同：UI 表现是**单次、短暂、无通道仲裁**的，
    /// 这里只需要"可取消 + 终态可选查询"。
    /// </summary>
    public readonly struct MotionHandle
    {
        private readonly CancellationTokenSource _cts;

        /// <summary>终态（完成即写；未完成时读取返回 <see cref="MotionOutcome.Completed"/> 的占位——
        /// 请先看 <see cref="IsFinished"/>）。</summary>
        public MotionOutcome Outcome { get; }

        /// <summary>是否已收终态。</summary>
        public bool IsFinished { get; }

        /// <summary>该播放的取消源（宿主关闭/超时经它停工作；无取消源时 IsCancellationRequested 恒 false）。</summary>
        public CancellationToken Token => _cts != null ? _cts.Token : CancellationToken.None;

        internal MotionHandle(CancellationTokenSource cts, MotionOutcome outcome, bool finished)
        {
            _cts = cts;
            Outcome = outcome;
            IsFinished = finished;
        }

        /// <summary>请求取消（幂等；只发信号，复位由策略自身完成——§6.3）。</summary>
        public void Cancel()
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        }

        internal MotionHandle With(MotionOutcome outcome, bool finished) => new MotionHandle(_cts, outcome, finished);
    }

    /// <summary>
    /// 动效播放结果容器（可变引用型——策略内部写回，调用方事后读）。
    /// 由 <see cref="UITransitionRunner"/> 创建并交给策略，策略在收尾时 <see cref="Finish"/>。
    /// </summary>
    public sealed class MotionPlayback
    {
        private readonly CancellationTokenSource _cts;

        public MotionPlayback(CancellationTokenSource cts) => _cts = cts;

        /// <summary>调用方（宿主/超时）请求取消的信号源。</summary>
        public CancellationToken Token => _cts != null ? _cts.Token : CancellationToken.None;

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
