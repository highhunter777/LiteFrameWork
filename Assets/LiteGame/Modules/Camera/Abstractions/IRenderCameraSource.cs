using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// **渲染相机来源**（可选能力，相机实现按需实现）：表现层要拿"真正在渲染的那台相机"。
    ///
    /// 为什么是能力端口而不是具体类型：装配形态有叠加开发相机、替身相机、未来别的后处理方案；
    /// 消费方（视图装配/伤害飘字）只需要"给我渲染相机"，不需要知道自己用的是 Cinemachine。
    /// 与 <see cref="IAimWorldSink"/> 同一范式：可选接口 + 消费方 <c>is</c> 能力接口，未实现即合法退化。
    /// </summary>
    public interface IRenderCameraSource
    {
        /// <summary>解析虚拟相机（vcam 是场景对象——视图未建前可能尚未解析）。返回是否解析到。</summary>
        bool EnsureCamera();

        /// <summary>渲染相机（Cinemachine brain 的 OutputCamera）；解析不到返回 null。</summary>
        Camera TryGetRenderCamera();

        /// <summary>是否已解析到虚拟相机（装配期诊断读口；未解析不是错误——切到配了 vcam 的场景会自动接上）。</summary>
        bool HasCamera { get; }

        /// <summary>最近一次解析结论（装配缺口日志用）。</summary>
        string LastResolveReason { get; }
    }
}
