using System;
using LiteFramework;
using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>
    /// UI prefab 租约（《UI框架总设计》§5.2 资源租约与缓存——Unity 对象引用不能代替包引用计数）： UI loader 返回**可释放句柄**。
    /// </summary>
    public interface IUIPrefabLease
    {
        /// <summary>prefab 资产（租约存续期内有效）。</summary>
        GameObject Prefab { get; }

        /// <summary>释放底层引用（引用计数递减；最后一个依赖实例销毁后调用）。</summary>
        void Release();
    }
}
