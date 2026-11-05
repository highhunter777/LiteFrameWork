using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 句柄 → 实例表（《VFX服务实施指导》§2.2 第 3 件）：活跃登记 + 到期扫描。
    /// 加载在途的实例也**先登记**（`Go == null`）——竞态窗口从一开始就被表覆盖。
    /// </summary>
    public sealed class VfxHandleTable
    {
        private readonly Dictionary<int, VfxInstance> _byId = new Dictionary<int, VfxInstance>(64);

        /// <summary>活跃数（含加载在途）。</summary>
        public int ActiveCount => _byId.Count;

        public void Add(VfxInstance inst) => _byId[inst.Id] = inst;

        /// <summary>取走并移除（幂等：不存在返回 false）。</summary>
        public bool TryTake(int id, out VfxInstance inst)
        {
            if (!_byId.TryGetValue(id, out inst)) return false;
            _byId.Remove(id);
            return true;
        }

        /// <summary>最旧的一个活跃实例（句柄单调递增 → Id 最小 = 最旧）；无则 null。</summary>
        public VfxInstance Oldest()
        {
            VfxInstance oldest = null;
            foreach (var kv in _byId)
                if (oldest == null || kv.Value.Id < oldest.Id) oldest = kv.Value;
            return oldest;
        }

        /// <summary>收集到期实例的 id（`ExpireAt &lt;= now`）。
        ///
        /// **不以 `Go != null` 作前提**：加载在途的实例 `ExpireAt` 为 +∞（`Materialize` 才落寿命），
        /// 天然不会被收集；而**已实例化但挂点被外部销毁**的实例（视图根/实体视图整体拆除时 Unity 把
        /// 跟随实例一起销毁）`Go` 会变成 fake-null——把 `Go != null` 当判据就会把它误当成"还在加载"，
        /// 于是表项永久残留、`ActiveCount` 永不归零（离场冒烟会一直卡在非零活跃数）。</summary>
        public void CollectExpired(float now, List<int> into)
        {
            foreach (var kv in _byId)
            {
                var inst = kv.Value;
                if (inst.ExpireAt <= now) into.Add(inst.Id);
            }
        }

        /// <summary>收集全部活跃 id（Shutdown 用——快照遍历，避免边 Stop 边枚举）。</summary>
        public void CollectAll(List<int> into)
        {
            foreach (var kv in _byId) into.Add(kv.Key);
        }
    }
}
