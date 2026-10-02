using System;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// UI prefab 租约（《UI框架总设计》§5.2 资源租约与缓存——Unity 对象引用不能代替包引用计数）：
    /// UI loader 返回**可释放句柄**，与客户端 IContentService 对齐——实例持租约到真正销毁
    /// （缓存实例也算使用者），禁止"返回对象前 Release 句柄"（Player 加载即悬空引用）。
    /// </summary>
    public interface IUIPrefabLease
    {
        /// <summary>prefab 资产（租约存续期内有效）。</summary>
        GameObject Prefab { get; }

        /// <summary>释放底层引用（引用计数递减；最后一个依赖实例销毁后调用）。</summary>
        void Release();
    }

    /// <summary>
    /// 内容服务租约的 UI 适配（装配点绑定：<see cref="IContentService"/>.AcquireAsync&lt;GameObject&gt; →
    /// AssetLease 转 IUIPrefabLease——UIService 不感知内容服务类型，只认可释放句柄）。
    /// </summary>
    public sealed class ContentPrefabLease : IUIPrefabLease
    {
        private readonly AssetLease<GameObject> _lease;

        public GameObject Prefab => _lease.Asset;

        public ContentPrefabLease(AssetLease<GameObject> lease)
        {
            _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        }

        public void Release() => _lease.Dispose();
    }

    /// <summary>
    /// 租约工厂：无主租约（Release 为 no-op）——编辑器工具/测试替身用（对象不经内容服务加载，
    /// 无底层引用可归）。生产主链一律走 <see cref="ContentPrefabLease"/>。
    /// </summary>
    public static class UIPrefabLeases
    {
        public static IUIPrefabLease Unowned(GameObject prefab) => new UnownedLease(prefab);

        private sealed class UnownedLease : IUIPrefabLease
        {
            private GameObject _prefab;
            public GameObject Prefab => _prefab;
            public UnownedLease(GameObject prefab) => _prefab = prefab;
            public void Release() => _prefab = null;   // 无底层引用——只断引用（对象归创建方管理）
        }
    }
}
