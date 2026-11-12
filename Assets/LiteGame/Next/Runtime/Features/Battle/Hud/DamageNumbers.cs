using System.Collections.Generic;
using System.Globalization;
using LiteSim;

namespace LiteGameNext.Features.Battle.Hud
{
    /// <summary>伤害数字归属：本地造成 / 本地承受。</summary>
    public enum DamageRole
    {
        Dealt = 0,
        Taken = 1,
    }

    /// <summary>一条待显示的伤害数字（聚合后）。</summary>
    public readonly struct DamageNumber
    {
        public DamageNumber(long targetId, DamageRole role, int value, bool crit, float spawnedAt)
        {
            TargetId = targetId;
            Role = role;
            Value = value;
            Crit = crit;
            SpawnedAt = spawnedAt;
        }

        public long TargetId { get; }
        public DamageRole Role { get; }
        public int Value { get; }
        public bool Crit { get; }
        public float SpawnedAt { get; }

        /// <summary>显示文本（≥10000 走 K 简写）。</summary>
        public string Text => DamageNumberText.Format(Value);
    }

    /// <summary>伤害数字文本格式化：InvariantCulture 定点（跨区域一致），≥10⁴ 简写为 K。</summary>
    public static class DamageNumberText
    {
        /// <summary>简写阈值。</summary>
        public const int KThreshold = 10000;

        public static string Format(int value)
        {
            if (value < KThreshold) return value.ToString(CultureInfo.InvariantCulture);
            float k = value / 1000f;
            string head = k % 1f == 0f
                ? ((int)k).ToString(CultureInfo.InvariantCulture)
                : k.ToString("F1", CultureInfo.InvariantCulture);
            return head + "K";
        }
    }

    /// <summary>伤害数字聚合：同 (目标, 归属, 暴击档) 在合并窗口内累加成一条。</summary>
    public sealed class DamageNumberAggregator
    {
        /// <summary>合并窗口（秒）：窗口内同键命中累加，不新起条目。</summary>
        public const float MergeWindowSeconds = 0.35f;

        private struct Entry
        {
            public long TargetId;
            public DamageRole Role;
            public bool Crit;
            public int Value;
            public float LastMergeAt;
        }

        private readonly List<Entry> _entries = new List<Entry>(16);

        /// <summary>当前条目数（诊断/测试）。</summary>
        public int Count => _entries.Count;

        /// <summary>
        /// 记一次命中；返回本次应显示的条目快照（可能是合并后的累计值）。
        /// </summary>
        public DamageNumber Record(FrameEventKind kind, long target, long localEntityId, int value, float now)
        {
            bool crit = kind == FrameEventKind.Crit;
            DamageRole role = target == localEntityId ? DamageRole.Taken : DamageRole.Dealt;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (entry.TargetId != target || entry.Role != role || entry.Crit != crit) continue;
                if (now - entry.LastMergeAt > MergeWindowSeconds) continue;
                entry.Value += value;
                entry.LastMergeAt = now;
                _entries[i] = entry;
                return new DamageNumber(target, role, entry.Value, crit, entry.LastMergeAt);
            }

            var fresh = new Entry
            {
                TargetId = target,
                Role = role,
                Crit = crit,
                Value = value,
                LastMergeAt = now,
            };
            _entries.Add(fresh);
            return new DamageNumber(target, role, value, crit, now);
        }

        /// <summary>淘汰超窗条目（每帧调用；返回淘汰数）。</summary>
        public int Prune(float now)
        {
            int removed = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (now - _entries[i].LastMergeAt <= MergeWindowSeconds * 3f) continue;
                _entries.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        /// <summary>清空（换局）。</summary>
        public void Reset() => _entries.Clear();
    }
}
