using System;
using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 槽位 → 视图对象的映射与池。
    ///
    /// 槽位是 Sim 的稳定寻址（《状态同步专项设计》§3：实体 Id = 版本 + 槽位，禁止 swap-remove），
    /// 故视图直接按槽位下标索引——一个定长数组，零字典查找、零分配。
    ///
    /// **池按 prefab location 分桶**：每 location 一个 <see cref="ObjectPool{T}"/>（组合内核，
    /// 不自研第二套池——《对象池专项设计》§5 收编；不用 <see cref="GameObjectPool"/> 的原因见 §5.2：
    /// 全局 DontDestroyOnLoad 池根与本类的战斗域 root 生命周期不符）。validateOnAcquire 做 Unity
    /// fake-null 销毁防御；onGet = 激活，onRelease = 停用 + 挂回 _root；maxIdle 可配（默认 32）。
    /// 回收 = 停用 + 入池（不销毁）；视图对象本身由 <see cref="SimView.ViewRecycler"/> 决定最终去向
    /// （生产接 EntityService.Hide；测试接 Destroy）——本类只管"什么时候回收"。
    /// </summary>
    public sealed class EntityViewMap
    {
        private readonly SimView.ViewFactory _factory;
        private readonly SimView.ViewRecycler _recycler;
        private readonly Transform _root;
        private readonly Func<EntitySlot, string> _locationOf;
        private readonly int _poolMaxIdle;

        private readonly GameObject[] _views = new GameObject[SimConfig.MaxEntities];
        private readonly bool[] _has = new bool[SimConfig.MaxEntities];
        private readonly Dictionary<string, ObjectPool<GameObject>> _pools = new Dictionary<string, ObjectPool<GameObject>>(4);
        private int _count;

        public EntityViewMap(SimView.ViewFactory factory, SimView.ViewRecycler recycler, Transform root,
            Func<EntitySlot, string> locationOf = null, int poolMaxIdle = 32)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _recycler = recycler;      // null = 本类自有池（生产接 EntityService 时由它管池，两份不重复记账）
            _root = root;
            _locationOf = locationOf;
            _poolMaxIdle = poolMaxIdle;
        }

        /// <summary>已建立的实体视图数。</summary>
        public int Count => _count;

        /// <summary>池中驻留的视图数（诊断：应随回收回落，受 maxIdle 约束不无界增长）。</summary>
        public int PooledCount
        {
            get
            {
                int total = 0;
                foreach (var pool in _pools.Values) total += pool.UnusedCount;
                return total;
            }
        }

        public bool TryGet(int slotIndex, out GameObject view)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) { view = null; return false; }
            view = _views[slotIndex];
            return _has[slotIndex] && view != null;
        }

        /// <summary>建（或从池取）槽位视图。返回 null = 工厂拒绝（资源缺失等，表现为无实体可见）。</summary>
        public GameObject Create(int slotIndex, in EntitySlot slot)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return null;

            string location = ResolveLocation(slot);
            var view = GetPool(location).Acquire();      // 空池 create（工厂可拒绝返回 null）；池中件先过销毁防御
            if (view == null) return null;

            _views[slotIndex] = view;
            _has[slotIndex] = true;
            _count++;
            return view;
        }

        /// <summary>回收槽位视图（停用入池 + 交回收方）。幂等：无视图时 no-op。</summary>
        public void Release(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return;
            if (!_has[slotIndex]) return;

            GameObject view = _views[slotIndex];
            _views[slotIndex] = null;
            _has[slotIndex] = false;
            _count--;
            if (_count < 0) _count = 0;

            if (view == null) return;

            // [Diag] 临时诊断哨位：对局中视图回收 = 罕见事件，任何一次都留痕
            UnityEngine.Debug.LogWarning(
                $"[Diag] 视图回收 slot={slotIndex} name={view.name} active={view.activeSelf} pos={view.transform.position}");

            if (_recycler != null)
            {
                // 回收方决定去向（EntityService.Hide 会自己做停用/归池/销毁）——
                // 此处不再入本地池，避免"两份池"各自记账。
                _recycler(view);
                return;
            }

            // 自有池路径：onRelease（停用 + 挂回 _root）与超限销毁由内核驱动（PoolKey 归桶自描述）
            GetPool(PoolKey(view)).Release(view);
        }

        /// <summary>清空本地池（宿主关闭/对局结束；回收方为 null 时的自有池才需要）。</summary>
        public void ClearPool()
        {
            foreach (var pool in _pools.Values) pool.Clear();
            _pools.Clear();
        }

        private ObjectPool<GameObject> GetPool(string location)
        {
            if (!_pools.TryGetValue(location, out var pool))
            {
                string key = location;                        // 闭包捕获键（逐桶一份）
                pool = new ObjectPool<GameObject>(
                    create: () => _factory(key, _root),
                    onGet: go => go.SetActive(true),           // Release 侧停用入池——取件必须重新激活（对偶缺失 = 复用件永久隐形）
                    onRelease: go =>
                    {
                        go.SetActive(false);
                        if (_root != null) go.transform.SetParent(_root, false);
                    },
                    onDestroy: DestroyView,
                    maxIdle: _poolMaxIdle,
                    statsName: $"EntityViewMap.{location}",
                    validateOnAcquire: go => go != null);      // Unity fake-null 防御：池中件被外部销毁则失效转 create
                _pools[location] = pool;
            }
            return pool;
        }

        /// <summary>销毁口径：Play 延迟（不打断当帧）/ 编辑器 Immediate（与 GameObjectPool/UIService 同口径）。</summary>
        private static void DestroyView(GameObject go)
        {
            if (go == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(go); return; }
#endif
            UnityEngine.Object.Destroy(go);
        }

        private string ResolveLocation(in EntitySlot slot)
            => _locationOf != null ? _locationOf(slot) : SimView.DefaultEntityPrefab;

        /// <summary>池键：用视图名（工厂以 location 命名实例）——回收方为 null 的自有池路径使用。</summary>
        private static string PoolKey(GameObject view) => view.name;
    }
}
