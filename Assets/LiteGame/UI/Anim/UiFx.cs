using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// 动效原语句柄（《动画模块专项设计》§14 原语复位清单 + 《UI框架总设计》§6.3 复位契约）：
    /// 原语被中断时**属性必须复位**——本仓 DOTween 的 <c>OnKill</c> 不触发，
    /// 故复位不挂 kill 钩子，而由持有者经本句柄显式收尾：四原语的**完成态即基线**
    /// （Pulse 呼吸回原 alpha、Flash 回原色、Slide 回原位、CountUp 到目标文本），
    /// 因此 <see cref="Stop(true)"/>（先 Complete 跳终值再 Kill）即复位；
    /// <see cref="Stop(false)"/> 停在半途不保证复位（调用方自担）。
    /// </summary>
    public sealed class UiFxHandle : IDisposable
    {
        private Tween _tween;

        internal UiFxHandle(Tween tween)
        {
            _tween = tween ?? throw new ArgumentNullException(nameof(tween));
        }

        /// <summary>是否仍在播放（完成/终止后为 false）。</summary>
        public bool IsPlaying => _tween != null && _tween.IsActive() && _tween.IsPlaying();

        /// <summary>
        /// 跳到终值并结束（终值 = 基线/目标态——即"复位"，无需回放）。幂等。
        /// </summary>
        public void Complete()
        {
            if (_tween != null && _tween.IsActive()) _tween.Complete();
        }

        /// <summary>
        /// 停止。<paramref name="complete"/>=true：先 Complete 跳终值（复位）再 Kill——展示作用域收尾/页面关闭口径；
        /// false：直接 Kill 停半途（复用前由 UIForm.PrepareForShow 的页面级复位兜底）。
        /// </summary>
        public void Stop(bool complete)
        {
            if (_tween == null) return;
            if (complete) Complete();                     // 先跳终值再杀：Kill(true) 不复位
            if (_tween.IsActive()) _tween.Kill(false);
            _tween = null;
        }

        /// <summary>展示作用域收尾口径（= Stop(true)：复位离场，§4.4 临时动效随展示收口）。</summary>
        public void Dispose() => Stop(complete: true);
    }

    /// <summary>
    /// 动效原语库（动效设计方案 附 A.1 的实现件）：**全项目唯一碰 DOTween 的地方**。
    /// **UIClock 轨**：SetUpdate(UpdateType.Manual, true)（经 DotweenUiClockDriver 按 UIClock
    /// 派发——时停不停、暂停即停）；
    /// SetLink(KillOnDisable) = 目标禁用即杀（防泄漏）；原语同步返回句柄、不携带任何判定。
    /// 句柄语义见 <see cref="UiFxHandle"/>（中断复位由持有者显式收尾——OnKill 不触发）。
    /// </summary>
    public static class UiFx
    {
        /// <summary>脉冲：透明度快速呼吸两次（图标/红点提醒）。完成态 = 原透明度。
        /// 缺省参单源＝<see cref="UiFxDefaults"/>（Lua shim 同表引用）。</summary>
        public static UiFxHandle Pulse(Graphic g, float strength = UiFxDefaults.PulseStrength, float duration = UiFxDefaults.PulseDuration)
        {
            var baseAlpha = g.color.a;
            return new UiFxHandle(g.DOFade(baseAlpha * strength, duration)
                .SetLoops(2, LoopType.Yoyo)
                .SetUpdate(UpdateType.Manual, true)
                .SetLink(g.gameObject, LinkBehaviour.KillOnDisable));
        }

        /// <summary>闪烁：一次性高亮回落（白 → 原色）。完成态 = 原色——
        /// 与 <see cref="UiFxHandle"/> 的"完成态即基线"复位契约一致。</summary>
        public static UiFxHandle Flash(Graphic g, float duration = UiFxDefaults.FlashDuration)
        {
            var c = g.color;
            return new UiFxHandle(g.DOColor(c, duration)
                .From(Color.white)
                .SetUpdate(UpdateType.Manual, true)
                .SetLink(g.gameObject, LinkBehaviour.KillOnDisable));
        }

        /// <summary>位移入场：从 offset 相对位滑回原位。完成态 = 原 anchoredPosition。
        /// 起点 = 原位 + offset、终点 = 原位（显式 <c>.From(原位 + offset)</c>，"完成态即基线"可证）。</summary>
        public static UiFxHandle Slide(RectTransform rt, Vector2 offset, float duration = UiFxDefaults.SlideDuration)
        {
            Vector2 origin = rt.anchoredPosition;
            return new UiFxHandle(rt.DOAnchorPos(origin, duration)
                .From(origin + offset)
                .SetEase(Ease.OutQuad)
                .SetUpdate(UpdateType.Manual, true)
                .SetLink(rt.gameObject, LinkBehaviour.KillOnDisable));
        }

        /// <summary>数值滚动（CountUp）：format 为格式化委托（如 v => ((int)v).ToString()）。
        /// 完成态 = 目标文本（from→to 滚完）。</summary>
        public static UiFxHandle CountUp(TMPro.TMP_Text label, float from, float to, float duration, System.Func<float, string> format)
        {
            return new UiFxHandle(DOTween.To(() => from, v =>
                {
                    from = v;
                    label.text = format(v);
                }, to, duration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(UpdateType.Manual, true)
                .SetLink(label.gameObject, LinkBehaviour.KillOnDisable));
        }
    }
}
