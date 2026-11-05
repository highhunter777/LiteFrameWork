using System;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 后端能力标记（缺失能力要"拒绝、回退或保持"三选一，**不能静默降级**）。
    /// 单独成件：它是**跨层判据面**——播放器在提交前据此**逐通道**拒绝（<c>CapabilitiesAllow</c>），
    /// 后端实现按真实构造结果声明（如叠加层 Mask 建不出来就不声明 <see cref="OverlayChannel"/>），
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
        /// <summary>支持**覆盖层通道**（Override——无 Mask 整身接管，压过基础层后释放即回退；
        /// 与骨架形态无关，任何 rig 合法）。播放器据此在提交前放行/拒绝 Override 通道请求。</summary>
        OverrideChannel = 1 << 3,
        /// <summary>支持**同通道多片段按权重混合**（普通混合器 `AnimationMixerPlayable`：无 Mask、纯权重；
        /// 权重由调用方（Driver/消费者）裁决，后端只执行——业务优先级由 Sim/Driver 解释）。</summary>
        ClipBlending = 1 << 4,
        /// <summary>支持**叠加层通道**（Overlay——Mask 限定作用域的局部动作，叠在基础层之上）。
        /// 与 <see cref="OverrideChannel"/> 独立声明：叠加层需要后端构造出该层 Mask（当前骨架派生
        /// 仅人形成立），Mask 建不出来就不声明——Overlay 请求被播放器在提交前拒绝。</summary>
        OverlayChannel = 1 << 5,
    }
}