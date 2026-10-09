using System.Collections.Generic;

namespace LiteView
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

        /// <summary>写显式世界落点（<c>PlayAt</c> 的加载先于落位也成立：Materialize 续体读本字段）。</summary>
        public void SetPosition(int id, UnityEngine.Vector3 position)
        {
            if (_byId.TryGetValue(id, out var inst))
            {
                inst.Position = position;
                inst.HasPosition = true;
            }
        }

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

        /// <summary>收集到期实例的 id（**已实例化**且 `ExpireAt &lt;= now`；pending 的 ExpireAt = +∞ 不会命中）。</summary>
        public void CollectExpired(float now, List<int> into)
        {
            foreach (var kv in _byId)
            {
                var inst = kv.Value;
                if (inst.Go != null && inst.ExpireAt <= now) into.Add(inst.Id);
            }
        }

        /// <summary>收集全部活跃 id（Shutdown 用——快照遍历，避免边 Stop 边枚举）。</summary>
        public void CollectAll(List<int> into)
        {
            foreach (var kv in _byId) into.Add(kv.Key);
        }
    }
}
