using System;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 后端能力标记（缺失能力要"拒绝、回退或保持"三选一，**不能静默降级**）。
    /// 单独成件：它是**跨层判据面**——播放器在提交前据此拒绝（<c>CapabilitiesAllow</c>），
    /// 后端实现按真实构造结果声明（如上半身 Mask 建不出来就不声明 <see cref="LayeredChannels"/>），
    /// 测试据它断言"不静默降级"；接口定义（<see cref="IAnimationBackend"/>）不该被这份清单拖着走。
    /// </summary>
    [Flags]
    public enum AnimationBackendCapabilities
    {
        None = 0,
        /// <summary>支持循环播放。</summary>
        Looping = 1 << 0,
        /// <summary>支持带起点的提交（BeginPlay 的 normalizedTime）。</summary>
        StartAtNormalized = 1 << 1,
        /// <summary>支持速度倍率。</summary>
        SpeedOverride = 1 << 2,
        /// <summary>支持通道叠加（上半身层）。</summary>
        LayeredChannels = 1 << 3,
        /// <summary>支持**同通道多片段按权重混合**（普通混合器 `AnimationMixerPlayable`：无 Mask、纯权重；
        /// 权重由调用方（Driver/消费者）裁决，后端只执行——业务优先级由 Sim/Driver 解释）。</summary>
        ClipBlending = 1 << 4,
    }
}