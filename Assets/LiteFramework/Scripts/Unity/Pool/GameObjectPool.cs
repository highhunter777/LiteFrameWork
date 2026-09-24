using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteFramework
{
    /// <summary>GameObject 池化生命周期契约：取出后 / 归还前（实体、UI 件等场景物件实现）。
    /// 与 Core ObjectPool&lt;T&gt; 的 IPoolable 分层：那是纯 C# 实例的池回调，本接口是场景件回调。</summary>
    public interface IPoolLifecycle
    {
        void OnSpawn();      // 池取出/首次创建后
        void OnRecycle();    // 回收入池前（停粒子、退事件、断引用——谁挂谁清）
    }

    /// <summary>池化标记：记录产源桶键——Release 自描述归桶；非本池产物入池当场抛。</summary>
    public sealed class PooledInstance : MonoBehaviour
    {
        public string Key { get; internal set; }
    }

    /// <summary>
    /// GameObject 对象池（三池分工的第三件，设计方案 §1.1 既定：**包 Core ObjectPool&lt;T&gt; 做底层，
    /// 不自研第二套池逻辑**；纯数据对象 → ReferencePool，带回调的 C# 逻辑实例 → ObjectPool&lt;T&gt;，
    /// 会进场景渲染的 GameObject → 本池）。
    /// prefab 维度（key）分桶，每桶一个 ObjectPool&lt;GameObject&gt;（复用其所有权移交/重复归还检测/
    /// maxIdle 销毁/统计全套机制）；池根 DontDestroyOnLoad 防切场景误伤。
    /// 生命周期：Acquire 后 OnSpawn / Release 前 OnRecycle（实现 IPoolLifecycle 即被驱动）。
    /// 主线程 only（框架约定）。
    /// </summary>
    public sealed class GameObjectPool
    {
        private readonly Dictionary<string, ObjectPool<GameObject>> _buckets = new Dictionary<string, ObjectPool<GameObject>>(8);
        private Transform _root;                           // 池化停放容器（整体隐藏——池内物必非激活）；Dispose 后置空

        public GameObjectPool(Transform root = null, int maxIdlePerKey = 32)
        {
            MaxIdlePerKey = maxIdlePerKey < 1 ? 1 : maxIdlePerKey;
            if (root != null)
            {
                _root = root;
                _ownsRoot = false;
            }
            else
            {
                _ownsRoot = true;
                _root = new GameObject("[GameObjectPool]").transform;
                // DontDestroyOnLoad 仅 Play mode 合法（编辑器脚本/EditMode 用例直调抛 InvalidOperationException）——
                // 与 UIService/AudioService 同口径：编辑态跳过，编辑态池随测试作用域销毁即可。
                if (Application.isPlaying)
                    UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);
            }
            _root.gameObject.SetActive(false);
        }

        private bool _ownsRoot;                              // 自建根才由本池销毁（注入的 root 归注入方）

        public int MaxIdlePerKey { get; }

        /// <summary>取：桶命中复用（Acquire → 重挂父 → 激活 → OnSpawn），未命中 create 现建（打桶键标记）。</summary>
        public GameObject Get(string key, Func<GameObject> create, Transform parent = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (create == null) throw new ArgumentNullException(nameof(create));

            var go = GetBucket(key, create).Acquire();
            if (parent != null) go.transform.SetParent(parent, false);
            go.GetComponent<IPoolLifecycle>()?.OnSpawn();
            return go;
        }

        /// <summary>尝试复用（供异步加载管线先查池——UnusedCount&gt;0 时 Acquire 走复用不新建；未命中再走加载）。</summary>
        public bool TryGet(string key, out GameObject go, Transform parent = null)
        {
            go = null;
            if (!_buckets.TryGetValue(key, out var pool) || pool.UnusedCount == 0) return false;
            go = pool.Acquire();
            if (parent != null) go.transform.SetParent(parent, false);
            go.GetComponent<IPoolLifecycle>()?.OnSpawn();
            return true;
        }

        /// <summary>还：OnRecycle → 隐藏 → 归桶（按桶键标记自描述）。非本池产物抛（fail-fast）。</summary>
        public void Release(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            var marker = go.GetComponent<PooledInstance>();
            if (marker == null)
                throw new InvalidOperationException("该 GameObject 无桶键标记——不是本池产物，禁止入池");

            go.GetComponent<IPoolLifecycle>()?.OnRecycle();
            if (_root != null) go.transform.SetParent(_root, false);   // Dispose 后无停放容器——保持在位即可
            GetBucket(marker.Key, null).Release(go);       // 桶必然已存在（产物由此而出）
        }

        /// <summary>当前桶中（闲置）总数——统计/HUD 用。</summary>
        public int PooledTotal
        {
            get
            {
                int total = 0;
                foreach (var bucket in _buckets.Values) total += bucket.UnusedCount;
                return total;
            }
        }

        public int PooledCount(string key)
            => _buckets.TryGetValue(key, out var pool) ? pool.UnusedCount : 0;

        /// <summary>
        /// 排空全部桶并销毁闲置实例（池的生命周期释放面——宿主关闭/场景卸载时调用）。
        /// **只销毁闲置件**：仍在使用中的实例由调用方先归还/回收（谁持有谁负责，池不越权回收使用中的对象）。
        /// 幂等；桶结构保留（再次 Get 会重建桶）。销毁口径走各桶自己的 onDestroy（UnityEngine.Object.Destroy）。
        /// </summary>
        public void Clear()
        {
            foreach (var bucket in _buckets.Values)
                bucket.Clear();                            // ObjectPool.Clear = Trim(0)：销毁全部闲置件
        }

        /// <summary>
        /// 池的完整释放面：<see cref="Clear"/> + 销毁自建池根（未注入 root 时才有权销毁——注入的 root 归注入方）。
        /// 幂等；释放后再 Get/Release 会自建新桶（不抛——池是可再生的，与服务关闭语义不同）。
        /// </summary>
        public void Dispose()
        {
            Clear();
            if (_ownsRoot && _root != null)
            {
                DestroyAtRuntime(_root.gameObject);
                _root = null;
            }
        }

        /// <summary>销毁口径：Play mode 走延迟销毁（不打断当帧），编辑态走立即销毁
        /// （EditMode 无延迟销毁帧，<c>Destroy</c> 会报错——与 UIService/AudioService 同口径）。</summary>
        private static void DestroyAtRuntime(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(go); return; }
#endif
            UnityEngine.Object.Destroy(go);
        }

        private ObjectPool<GameObject> GetBucket(string key, Func<GameObject> create)
        {
            if (!_buckets.TryGetValue(key, out var pool))
            {
                pool = new ObjectPool<GameObject>(
                    create: () =>
                    {
                        var go = create != null ? create() : new GameObject(key);
                        go.AddComponent<PooledInstance>().Key = key;
                        return go;
                    },
                    onGet: go => go.SetActive(true),
                    onRelease: go => go.SetActive(false),
                    onDestroy: go => DestroyAtRuntime(go),
                    maxIdle: MaxIdlePerKey,
                    statsName: $"GameObjectPool.{key}");
                _buckets[key] = pool;
            }
            return pool;
        }
    }
}
