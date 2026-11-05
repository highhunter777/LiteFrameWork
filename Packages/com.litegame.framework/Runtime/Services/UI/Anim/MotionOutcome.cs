using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework.UI
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
    /// 动效播放结果容器（可变引用型——策略内部写回，调用方事后读）。
    /// 由 <see cref="UITransitionRunner"/> 创建并交给策略，策略在收尾时 <see cref="Finish"/>。
    ///
    /// **取消面只是令牌**（M3-5c 转场解耦）：取消源归宿主（<c>OperationScope</c>），
    /// 本容器不再持有/释放 <c>CancellationTokenSource</c>——策略只读令牌、不拥有生命周期。
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
