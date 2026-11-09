using System;
using LiteFramework;
using UnityEngine;

namespace LiteFramework.UI
{

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
