using System;
using DG.Tweening;
using LiteFramework;
using UnityEngine;

namespace LiteGame.UI
{
    /// <summary>
    /// UI 动效时钟适配（G1 通用表现批；《动画模块专项设计》§1 裁决——"当前 SetUpdate(true) 不等价
    /// UIClock：必须由 UI 动效适配层明确接入项目 UIClock"）：
    ///
    /// - **DOTween**：<see cref="UpdateType.Manual"/> 轨经 <see cref="DotweenUiClockDriver"/> 按 UIClock
    ///   派发（UiFx 原语 / 转场策略 <c>SetUpdate(UpdateType.Manual, true)</c>——时停不停、暂停即停，
    ///   UIClock 语义）。**不使用 <c>UpdateType.Custom</c>**：本仓 DOTween 为闭源 DLL，其
    ///   <c>UpdateType</c> 仅含 <c>Normal/Late/Fixed/Manual</c>（2026-09-24 反射实证）；
    ///   <c>Manual</c> 轨不受引擎自动更新，只由 <c>DOTween.ManualUpdate</c> 推进——
    ///   实测 <c>ManualUpdate(dt, unscaled)</c> 下 <c>independent=true</c> 取 <c>unscaled</c> 参数，
    ///   不调用即完全冻结（暂停语义天然成立）。
    /// - **MonoBehaviour 件**（AnimatedImage 等序列帧）：经 <see cref="Delta"/> 取 UIClock 步进
    ///   （未绑定退化为 Time.unscaledDeltaTime——编辑器/测试兜底）。
    ///
    /// 进程内唯一 UIClock（GameModules 装配点 <see cref="Bind"/>；Domain-Reload-Off 静态残留由
    /// SubsystemRegistration 重置清空——§4 原则 8 同款纪律）。
    /// </summary>
    public static class UiAnimationClock
    {
        private static IUIClock _clock;

        /// <summary>装配点绑定（进程唯一 UI 时钟）。</summary>
        public static void Bind(IUIClock clock) => _clock = clock;

        /// <summary>清绑（Domain-Reload-Off 编辑场景）。</summary>
        public static void ResetForEditorReload() => _clock = null;

        /// <summary>动效步进：UIClock 已含时停/变速语义（未绑定时退化为真实帧步进）。</summary>
        public static float Delta => _clock == null ? Time.unscaledDeltaTime : _clock.ScaledDelta;
    }

    /// <summary>
    /// DOTween 的 UIClock 轨驱动（G1：Manual 轨的全局泵——随 UIClock 暂停即停、变速跟随）。
    /// 装配点注册进容器（注册序在 Clocks 之后）；只推进 <c>SetUpdate(UpdateType.Manual)</c> 轨，
    /// 不影响 Normal/Late/Fixed 轨的既有 Tween（实测：ManualUpdate 只推进 Manual 轨）。
    /// </summary>
    public sealed class DotweenUiClockDriver : ITickable
    {
        private readonly IUIClock _clock;

        public string StatsName => "UiClockDriver";

        public DotweenUiClockDriver(IUIClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Tick(float realDelta)
        {
            float step = _clock.ScaledDelta;
            if (step <= 0f) return;                           // UIClock 暂停：Manual 轨即停（不调用 = 不推进）

            // independent=true → 取 unscaled 参数；两条轨同源 UIClock 步进（时停不停 = 受 UIClock 支配）
            DOTween.ManualUpdate(step, step);
        }
    }
}
