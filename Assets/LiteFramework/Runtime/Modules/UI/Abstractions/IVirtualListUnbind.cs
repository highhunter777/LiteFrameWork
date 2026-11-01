using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>
    /// 可选：节点重绑/回收前的解绑口（§8.2"节点重绑先解除旧回调/租约，绑定代次失效，再写新数据"）。
    /// 数据源不需要在节点上挂监听时可实现空实现，或干脆不实现本接口（内核不强制）。
    /// </summary>
    public interface IVirtualListUnbind
    {
        void Unbind(Component item);
    }
}
