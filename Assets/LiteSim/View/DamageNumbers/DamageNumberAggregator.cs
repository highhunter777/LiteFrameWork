using System.Collections.Generic;

namespace LiteSim.View.DamageNumbers
{
    /// <summary>
    /// 聚合条目快照（驱动按快照驱动 TMP 实例——**聚合器不持实例**，只管合并窗口与账目；
    /// 实例的创建/更新/释放归驱动）。
    /// </summary>
    public readonly struct DamageNumberSnapshot
    {
        /// <summary>命中目标实体 Id（合并键之一）。</summary>
        public readonly long TargetId;
        /// <summary>本地角色（合并键之二——承受与造成各记各的）。</summary>
        public readonly HitLocalRole Role;
        /// <summary>累计值（窗口内连击累加）。</summary>
        public readonly int Value;
        /// <summary>暴击粘滞：任一组成命中为暴击即保持（升级不降级）。</summary>
        public readonly bool Crit;
        /// <summary>出场钟（升浮/抖动/出场缩放用——合并不重置）。</summary>
        public readonly double SpawnedAt;
        /// <summary>最后活动钟（淡出窗用——合并即重置，连击驻留）。</summary>
        public readonly double LastMergeAt;
        /// <summary>散布/抖动种子（新条目定、合并保持——运动不漂）。</summary>
        public readonly int Seed;
        /// <summary>本次调用是否并进既有条目（false = 新条目，驱动起新实例）。</summary>
        public readonly bool Merged;

        public DamageNumberSnapshot(long targetId, HitLocalRole role, int value, bool crit,
            double spawnedAt, double lastMergeAt, int seed, bool merged)
        {
            TargetId = targetId;
            Role = role;
            Value = value;
            Crit = crit;
            SpawnedAt = spawnedAt;
            LastMergeAt = lastMergeAt;
            Seed = seed;
            Merged = merged;
        }
    }

    /// <summary>
    /// 伤害数字聚合器（纯逻辑件——《命中反馈与伤害数字专项设计》§4.2）：同 (目标, 角色) 在
    /// <see cref="DamageNumberMotion.MergeWindowSeconds"/> 窗口内的命中**累加成一条**（连击合并——
    /// DNP Combination 的自研对应物）；窗口外命中起新条目。**零引擎、时钟注入**（驱动喂 now）——
    /// L1 源链接直测。
    /// </summary>
    public sealed class DamageNumberAggregator
    {
        private struct Entry
        {
            public int Value;
            public bool Crit;
            public double SpawnedAt;
            public double LastMergeAt;
            public int Seed;
        }

        private readonly Dictionary<(long TargetId, HitLocalRole Role), Entry> _entries =
            new Dictionary<(long, HitLocalRole), Entry>(16);

        private readonly double _mergeWindow;

        /// <summary>在册条目数（诊断/预算面）。</summary>
        public int ActiveCount => _entries.Count;

        /// <param name="mergeWindowSeconds">合并窗口（秒）——默认单源常量；测试注边界用。</param>
        public DamageNumberAggregator(float mergeWindowSeconds = DamageNumberMotion.MergeWindowSeconds)
        {
            _mergeWindow = mergeWindowSeconds;
        }

        /// <summary>
        /// 合并或新出：命中键在册且距最后活动 ≤ 窗口 → 累加＋暴击粘滞＋重置淡出钟（返回 Merged=true
        /// 快照）；否则新条目（Value 直落、新种子）。
        /// </summary>
        public DamageNumberSnapshot MergeOrSpawn(long targetId, HitLocalRole role, int value, bool crit, double now, int seed)
        {
            var key = (targetId, role);
            if (_entries.TryGetValue(key, out Entry e) && now - e.LastMergeAt <= _mergeWindow)
            {
                e.Value += value;
                e.Crit |= crit;
                e.LastMergeAt = now;
                _entries[key] = e;
                return new DamageNumberSnapshot(targetId, role, e.Value, e.Crit, e.SpawnedAt, e.LastMergeAt, e.Seed, merged: true);
            }

            e = new Entry { Value = value, Crit = crit, SpawnedAt = now, LastMergeAt = now, Seed = seed };
            _entries[key] = e;
            return new DamageNumberSnapshot(targetId, role, e.Value, e.Crit, e.SpawnedAt, e.LastMergeAt, e.Seed, merged: false);
        }

        /// <summary>
        /// 纯判定：此刻该键的命中**会不会并入既有条目**（键在册且距最后活动 ≤ 窗口）。
        /// 与 <see cref="MergeOrSpawn"/> 的分支判据同源（单一口径），供调用方在**真正合并之前**
        /// 预知"将起新条目"，以便先收口上一代条目——驱动用它避免同键覆盖导致旧实例失去跟踪
        /// （旧实例漏了淡出推进 ⇒ 永久残留 + 池泄漏）。**无副作用**：不改时钟、不改账面。
        /// </summary>
        public bool WillMerge(long targetId, HitLocalRole role, double now)
            => _entries.TryGetValue((targetId, role), out Entry e) && now - e.LastMergeAt <= _mergeWindow;

        /// <summary>查在册条目（驱动 Tick 读取账面用）。</summary>
        public bool TryGet(long targetId, HitLocalRole role, out DamageNumberSnapshot snapshot)
        {
            if (_entries.TryGetValue((targetId, role), out Entry e))
            {
                snapshot = new DamageNumberSnapshot(targetId, role, e.Value, e.Crit, e.SpawnedAt, e.LastMergeAt, e.Seed, merged: true);
                return true;
            }
            snapshot = default;
            return false;
        }

        /// <summary>摘账（驱动释放实例时同步调用——账实一致）。</summary>
        public bool Remove(long targetId, HitLocalRole role)
        {
            return _entries.Remove((targetId, role));
        }

        /// <summary>
        /// 淡出到期键收集（驱动 Tick 调）：最后活动距今 ≥ <see cref="DamageNumberMotion.FadeEndSeconds"/>
        /// 的条目键入列。聚合器只裁决，不做释放——实例释放与 <see cref="Remove"/> 归驱动。
        /// </summary>
        public void CollectExpired(double now, List<(long TargetId, HitLocalRole Role)> expired)
        {
            expired.Clear();
            foreach (var kv in _entries)
            {
                if (now - kv.Value.LastMergeAt >= DamageNumberMotion.FadeEndSeconds)
                    expired.Add(kv.Key);
            }
        }
    }
}
