using System;
using DG.Tweening;
using LiteFramework;
using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>
    /// UI 动效时钟适配（《动画模块专项设计》§1——"SetUpdate(true) 不等价 UIClock：必须由 UI 动效适配层明确接入项目 UIClock"）。
    /// </summary>
    public static class UIAnimationClock
    {
        private static IUIClock _clock;

        /// <summary>装配点绑定（进程唯一 UI 时钟）。</summary>
        public static void Bind(IUIClock clock) => _clock = clock;

        /// <summary>是否已绑定 UI 时钟（未绑定时动效走引擎自动更新兜底，见 FadeSlideTransition）。</summary>
        public static bool IsBound => _clock != null;

        /// <summary>清绑（Domain-Reload-Off 编辑场景 / 宿主关停）。</summary>
        public static void ResetForEditorReload() => _clock = null;

        /// <summary>动效步进：UIClock 已含时停/变速语义（未绑定时退化为真实帧步进）。</summary>
        public static float Delta => _clock == null ? Time.unscaledDeltaTime : _clock.ScaledDelta;
    }

    /// <summary>
    /// DOTween 的 UIClock 轨驱动（Manual 轨的全局泵——随 UIClock 暂停即停、变速跟随）。
    /// </summary>
    public sealed class DotweenUIClockDriver : ITickable
    {
        private readonly IUIClock _clock;

        public string StatsName => "UIClockDriver";

        public DotweenUIClockDriver(IUIClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Tick(float realDelta)
        {
            float step = _clock.ScaledDelta;
            if (step <= 0f) return;                           // UIClock 暂停：Manual 轨即停（不调用 = 不推进）

            // independent=true → 取 unscaled 参数；两条轨同源 UIClock 步进（时停不停 = 受 UIClock 支配）。
            DOTween.ManualUpdate(step, step);
        }
    }
}
