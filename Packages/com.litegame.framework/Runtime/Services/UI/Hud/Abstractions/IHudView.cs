using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>
    /// HUD 视图最小契约：只约定装配器需要的根件；各 HUD 族的强类型视图接口继承本接口。
    /// </summary>
    public interface IHudView
    {
        /// <summary>HUD 根件（Canvas 实例根；准心/布局坐标系基准）。</summary>
        Transform RootTransform { get; }
    }
}
