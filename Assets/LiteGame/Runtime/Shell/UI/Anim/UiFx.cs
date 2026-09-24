using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// 动效原语库（动效设计方案 附 A.1 的实现件，M4c 前置）：**全项目唯一碰 DOTween 的地方**。
    /// G1 动画时钟：SetUpdate(UpdateType.Manual, true) = **UIClock 轨**（经 DotweenUiClockDriver 按 UIClock
    /// 派发——时停不停、暂停即停；旧 SetUpdate(true) 是实时轨不等价 UIClock，动画专项 §1 裁决）；
    /// SetLink(KillOnDisable) = 目标禁用即杀（防泄漏）；原语同步返回、不携带任何判定。
    /// </summary>
    public static class UiFx
    {
        /// <summary>脉冲：透明度快速呼吸两次（图标/红点提醒）。</summary>
        public static Tweener Pulse(Graphic g, float strength = 0.2f, float duration = 0.16f)
        {
            var baseAlpha = g.color.a;
            return g.DOFade(baseAlpha * strength, duration)
                .SetLoops(2, LoopType.Yoyo)
                .SetUpdate(UpdateType.Manual, true)
                .SetLink(g.gameObject, LinkBehaviour.KillOnDisable);
        }

        /// <summary>闪烁：一次性高亮回落。</summary>
        public static Tweener Flash(Graphic g, float duration = 0.3f)
        {
            var c = g.color;
            return g.DOColor(new Color(1f, 1f, 0.6f, c.a), duration)
                .From(Color.white)
                .SetUpdate(UpdateType.Manual, true)
                .SetLink(g.gameObject, LinkBehaviour.KillOnDisable);
        }

        /// <summary>位移入场：从 offset 相对位滑回原位。</summary>
        public static Tweener Slide(RectTransform rt, Vector2 offset, float duration = 0.25f)
        {
            return rt.DOAnchorPos(rt.anchoredPosition + offset, duration)
                .From(true)
                .SetEase(Ease.OutQuad)
                .SetUpdate(UpdateType.Manual, true)
                .SetLink(rt.gameObject, LinkBehaviour.KillOnDisable);
        }

        /// <summary>数值滚动（CountUp）：**TMP 版**（2026-09-14 TMP 迁移）——format 为格式化委托（如 v => ((int)v).ToString()）。</summary>
        public static Tweener CountUp(TMPro.TMP_Text label, float from, float to, float duration, System.Func<float, string> format)
        {
            return DOTween.To(() => from, v =>
                {
                    from = v;
                    label.text = format(v);
                }, to, duration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(UpdateType.Manual, true)
                .SetLink(label.gameObject, LinkBehaviour.KillOnDisable);
        }
    }
}
