using System.Collections.Generic;

namespace LiteGame.Feedback.DamageNumbers
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
        /// <summary>累计值（窗口内同档连击累加）。</summary>
        public readonly int Value;
        /// <summary>爆头档（合并键之三——**爆头只并爆头、普通只并普通**；条目内恒定，无升级语义）。</summary>
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
    /// 伤害数字聚合器（纯逻辑件——《命中反馈与伤害数字专项设计》§4.2）：同 **(目标, 角色, 爆头档)** 在 DamageNumberMotion.MergeWindowSeconds 窗口内的命中**累加成一条**。
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

        private readonly Dictionary<(long TargetId, HitLocalRole Role, bool Crit), Entry> _entries =
            new Dictionary<(long, HitLocalRole, bool), Entry>(16);

        private readonly double _mergeWindow;
        private readonly double _fadeEnd;

        /// <summary>在册条目数（诊断/预算面）。</summary>
        public int ActiveCount => _entries.Count;

        /// <param name="mergeWindowSeconds">合并窗口（秒）——默认单源常量；表驱动由驱动注入。</param>
        /// <param name="fadeEndSeconds">淡出终点/条目寿命（秒）——默认单源常量；表驱动由驱动注入。</param>
        public DamageNumberAggregator(float mergeWindowSeconds = DamageNumberMotion.MergeWindowSeconds,
            float fadeEndSeconds = DamageNumberMotion.FadeEndSeconds)
        {
            _mergeWindow = mergeWindowSeconds;
            _fadeEnd = fadeEndSeconds;
        }

        /// <summary>
        /// 合并或新出：**同键（目标, 角色, 爆头档）**在册且距最后活动 ≤ 窗口 → 累加＋重置淡出钟
        /// （返回 Merged=true 快照）；否则新条目（Value 直落、新种子）。爆头与否不同的命中各走各键。
        /// </summary>
        public DamageNumberSnapshot MergeOrSpawn(long targetId, HitLocalRole role, int value, bool crit, double now, int seed)
        {
            var key = (targetId, role, crit);
            if (_entries.TryGetValue(key, out Entry e) && now - e.LastMergeAt <= _mergeWindow)
            {
                e.Value += value;
                e.LastMergeAt = now;
                _entries[key] = e;
                return new DamageNumberSnapshot(targetId, role, e.Value, crit, e.SpawnedAt, e.LastMergeAt, e.Seed, merged: true);
            }

            e = new Entry { Value = value, Crit = crit, SpawnedAt = now, LastMergeAt = now, Seed = seed };
            _entries[key] = e;
            return new DamageNumberSnapshot(targetId, role, e.Value, e.Crit, e.SpawnedAt, e.LastMergeAt, e.Seed, merged: false);
        }

        /// <summary>
        /// 纯判定：此刻该键的命中**会不会并入既有条目**（键在册且距最后活动 ≤ 窗口）。
        /// </summary>
        public bool WillMerge(long targetId, HitLocalRole role, bool crit, double now)
            => _entries.TryGetValue((targetId, role, crit), out Entry e) && now - e.LastMergeAt <= _mergeWindow;

        /// <summary>查在册条目（驱动 Tick 读取账面用）。</summary>
        public bool TryGet(long targetId, HitLocalRole role, bool crit, out DamageNumberSnapshot snapshot)
        {
            if (_entries.TryGetValue((targetId, role, crit), out Entry e))
            {
                snapshot = new DamageNumberSnapshot(targetId, role, e.Value, e.Crit, e.SpawnedAt, e.LastMergeAt, e.Seed, merged: true);
                return true;
            }
            snapshot = default;
            return false;
        }

        /// <summary>摘账（驱动释放实例时同步调用——账实一致）。</summary>
        public bool Remove(long targetId, HitLocalRole role, bool crit)
        {
            return _entries.Remove((targetId, role, crit));
        }

        /// <summary>
        /// 淡出到期键收集（驱动 Tick 调）：最后活动距今 ≥ 注入的淡出终点（缺省 DamageNumberMotion.FadeEndSeconds）的条目键入列。
        /// </summary>
        public void CollectExpired(double now, List<(long TargetId, HitLocalRole Role, bool Crit)> expired)
        {
            expired.Clear();
            foreach (var kv in _entries)
            {
                if (now - kv.Value.LastMergeAt >= _fadeEnd)
                    expired.Add(kv.Key);
            }
        }
    }
}
