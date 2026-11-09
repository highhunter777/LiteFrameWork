using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteFramework
{
    /// <summary>
    /// GameObject 对象池（三池分工的第三件：**包 Core ObjectPool&lt;T&gt; 做底层， 不自研第二套池逻辑**；纯数据对象 → ReferencePool。）。
    /// </summary>
    public sealed class GameObjectPool
    {
        /// <summary>自建池根的对象名（诊断面识别；勿在别处再写一份）。</summary>
        public const string RootName = "[GameObjectPool]";

        private readonly Dictionary<string, ObjectPool<GameObject>> _buckets = new Dictionary<string, ObjectPool<GameObject>>(8);
        private readonly Dictionary<GameObject, string> _keys = new Dictionary<GameObject, string>(32);   // 产物 → 桶键（Release 自描述归桶）
        private readonly List<GameObject> _deadKeys = new List<GameObject>(8);                            // 外部销毁留下的悬空键（Clear 时收）
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
                _root = new GameObject(RootName).transform;
                // DontDestroyOnLoad 仅 Play mode 合法（编辑器脚本/EditMode 用例直调抛 InvalidOperationException）——
                // 与 UIService/AudioService 同口径：编辑态跳过，编辑态池随测试作用域销毁即可。
                if (Application.isPlaying)
                    UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);
            }
            _root.gameObject.SetActive(false);
        }

        private bool _ownsRoot;                              // 自建根才由本池销毁（注入的 root 归注入方）

        public int MaxIdlePerKey { get; }

        /// <summary>取：桶命中复用（Acquire → 重挂父 → 激活 → OnSpawn），未命中 create 现建（记池内桶键）。
        /// 工厂拒绝（返回 null，如资源缺失）时按 null 返回——与 <see cref="ObjectPool{T}"/> 契约一致。</summary>
        public GameObject Get(string key, Func<GameObject> create, Transform parent = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (create == null) throw new ArgumentNullException(nameof(create));

            var go = GetBucket(key, create).Acquire();
            if (go == null) return null;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>尝试复用（供异步加载管线先查池——UnusedCount&gt;0 时 Acquire 走复用不新建；未命中再走加载）。</summary>
        public bool TryGet(string key, out GameObject go, Transform parent = null)
        {
            go = null;
            if (!_buckets.TryGetValue(key, out var pool) || pool.UnusedCount == 0) return false;
            go = pool.Acquire();
            if (parent != null) go.transform.SetParent(parent, false);
            return true;
        }

        /// <summary>还：OnRecycle → 隐藏 → 归桶（按池内桶键自描述）。非本池产物抛（fail-fast）。</summary>
        public void Release(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            if (!_keys.TryGetValue(go, out var key))
                throw new InvalidOperationException("该 GameObject 不是本池产物（无桶键记录），禁止入池");

            if (_root != null) go.transform.SetParent(_root, false);   // Dispose 后无停放容器——保持在位即可
            GetBucket(key, null).Release(go);       // 桶必然已存在（产物由此而出）
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
        /// </summary>
        public void Clear()
        {
            foreach (var bucket in _buckets.Values)
                bucket.Clear();                            // ObjectPool.Clear = Trim(0)：销毁全部闲置件
            PruneDeadKeys();                               // 外部 Destroy 的活体（场景卸载）留悬空键——顺手收掉
        }

        /// <summary>摘掉已销毁对象的桶键（外部 Destroy 属违约，只防字典累积）。</summary>
        private void PruneDeadKeys()
        {
            if (_keys.Count == 0) return;
            _deadKeys.Clear();
            foreach (var pair in _keys)
                if (pair.Key == null) _deadKeys.Add(pair.Key);
            foreach (var dead in _deadKeys) _keys.Remove(dead);
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
                        if (go != null) _keys[go] = key;        // 桶键记账在池内，不往实例补组件
                        return go;                              // null = 工厂拒绝（资源缺失），交回 Acquire 处理
                    },
                    onGet: go => go.SetActive(true),
                    onRelease: go => go.SetActive(false),
                    onDestroy: go =>
                    {
                        if (go != null) _keys.Remove(go);
                        DestroyAtRuntime(go);
                    },
                    maxIdle: MaxIdlePerKey,
                    statsName: $"GameObjectPool.{key}",
                    diagnoseDoubleRelease: DevBuildFacts.IsDevelopment);
                _buckets[key] = pool;
            }
            return pool;
        }
    }
}
