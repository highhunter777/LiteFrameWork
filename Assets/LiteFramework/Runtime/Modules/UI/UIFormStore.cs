using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>
    /// 界面实例注册表（从 <see cref="UIService"/> 外提的"缓存实例 + 租约 + LRU 记账"域）：
    /// id → 实例（含池中）／实例租约（持到实例真正销毁）／最近使用序号（LRU 判据）。
    ///
    /// **所有权**：<see cref="Register"/> 即租约所有权移交——登记后由本表持有，直到
    /// <see cref="Detach"/>／<see cref="Unregister"/> 交回调用方释放（"谁登记、谁释放"单一出口）。
    /// **不承担策略**：预算与 Resident/销毁策略的取舍在 <see cref="UIService"/>（本表只给候选集）。
    /// </summary>
    internal sealed class UIFormStore
    {
        private readonly Dictionary<int, UIForm> _forms = new Dictionary<int, UIForm>(16);
        private readonly Dictionary<int, IUIPrefabLease> _leases = new Dictionary<int, IUIPrefabLease>(8);
        private readonly Dictionary<int, long> _lastUsed = new Dictionary<int, long>(8);
        private long _useCounter;

        /// <summary>在册实例（含池中）。**枚举期间不得改动本表**（开关界面须经 Tick 快照）。</summary>
        public Dictionary<int, UIForm>.ValueCollection All => _forms.Values;

        /// <summary>在册实例数（含池中）。</summary>
        public int Count => _forms.Count;

        public bool TryGet(int formId, out UIForm form) => _forms.TryGetValue(formId, out form);

        /// <summary>是否在册（含池中实例）。</summary>
        public bool Contains(int formId) => _forms.ContainsKey(formId);

        /// <summary>登记新实例并接管其租约（所有权移交——登记成功后调用方不得再释放该租约）。</summary>
        public void Register(int formId, UIForm form, IUIPrefabLease lease)
        {
            _forms[formId] = form;
            _leases[formId] = lease;
        }

        /// <summary>撤销登记（打开回滚/权威取消）：交回实例与租约，由调用方销毁半成品并释放租约。</summary>
        public void Unregister(int formId, out UIForm form, out IUIPrefabLease lease)
        {
            _forms.Remove(formId, out form);
            _leases.Remove(formId, out lease);
            _lastUsed.Remove(formId);
        }

        /// <summary>摘除并交回实例与租约（完整销毁路径）；不在册返回 false（幂等）。</summary>
        public bool Detach(int formId, out UIForm form, out IUIPrefabLease lease)
        {
            if (!_forms.Remove(formId, out form))
            {
                lease = null;
                return false;
            }
            _leases.Remove(formId, out lease);
            _lastUsed.Remove(formId);
            return true;
        }

        /// <summary>LRU 记账（实例落池后调用）——序号单调递增，越大越新。</summary>
        public void MarkUsed(int formId) => _lastUsed[formId] = ++_useCounter;

        /// <summary>
        /// 收集淘汰候选：**池中且非 Resident** 的实例（Resident 不参与淘汰——预算满时跳过常驻页），
        /// 附带最近使用序号供调用方排序（升序 = 最旧先淘汰）。
        /// </summary>
        public void CollectEvictionCandidates(List<(int id, long used)> into)
        {
            foreach (var kv in _forms)
            {
                if (kv.Value.State != UIFormState.Recycled) continue;
                if (kv.Value.Info.CacheStrategy == UICacheStrategy.Resident) continue;
                if (_lastUsed.TryGetValue(kv.Key, out long used)) into.Add((kv.Key, used));
            }
        }

        /// <summary>收集"仍打开"（Active/Covered/Paused）的实例 id——批量关闭/容错清理用。</summary>
        public void CollectOpenIds(List<int> into)
        {
            foreach (var kv in _forms)
                if (kv.Value != null && kv.Value.IsOpen) into.Add(kv.Key);
        }
    }
}
