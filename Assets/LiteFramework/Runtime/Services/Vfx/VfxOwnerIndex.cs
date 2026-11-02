using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 归属人 → 活跃特效 id 的索引（《VFX服务实施指导》§2.2 簿记件）：对局反馈件拆除时按归属人一次收回
    /// 自己播过的弹着/火光，不必等各自自然到期；空集即摘键（不给归属人留强引用），键表 O(键数) 有界。
    ///
    /// **为什么独立成件**：`VfxService` 已贴着类行数预算（≤400），归属簿记是纯数据结构、无服务状态，
    /// 单列后服务的行数预算只降不涨，簿记本身也可单独测。
    /// </summary>
    public sealed class VfxOwnerIndex
    {
        private readonly Dictionary<object, HashSet<int>> _byOwner = new Dictionary<object, HashSet<int>>(8);

        public void Track(object owner, int id)
        {
            if (owner == null) return;
            if (!_byOwner.TryGetValue(owner, out HashSet<int> ids))
            {
                ids = new HashSet<int>();
                _byOwner[owner] = ids;
            }
            ids.Add(id);
        }

        public void Untrack(object owner, int id)
        {
            if (owner == null) return;
            if (!_byOwner.TryGetValue(owner, out HashSet<int> ids)) return;
            ids.Remove(id);
            if (ids.Count == 0) _byOwner.Remove(owner);
        }

        /// <summary>取走该归属人的全部 id（调用方逐个 Stop）。返回 false = 该归属人没有登记。</summary>
        public bool TakeAll(object owner, List<int> into)
        {
            if (owner == null || !_byOwner.TryGetValue(owner, out HashSet<int> ids)) return false;
            into.AddRange(ids);
            _byOwner.Remove(owner);
            return true;
        }

        public void Clear() => _byOwner.Clear();
    }
}
