using UnityEngine;

namespace LiteGame.Modules.Camera
{
    /// <summary>
    /// **渲染相机来源**（可选能力，相机实现按需实现）：表现层要拿"真正在渲染的那台相机"。
    /// </summary>
    public interface IRenderCameraSource
    {
        /// <summary>解析虚拟相机（vcam 是场景对象——视图未建前可能尚未解析）。返回是否解析到。</summary>
        bool EnsureCamera();

        /// <summary>渲染相机（Cinemachine brain 的 OutputCamera）；解析不到返回 null。</summary>
        UnityEngine.Camera TryGetRenderCamera();

        /// <summary>是否已解析到虚拟相机（装配期诊断读口；未解析不是错误——切到配了 vcam 的场景会自动接上）。</summary>
        bool HasCamera { get; }

        /// <summary>最近一次解析结论（装配缺口日志用）。</summary>
        string LastResolveReason { get; }
    }
}
