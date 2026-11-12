using System.Collections.Generic;
using LiteSim;

namespace LiteGameNext.Features.Battle
{
    /// <summary>战斗事件统计：命中判定的唯一权威证据来源（不信 HUD 数字）。</summary>
    public sealed class CombatEventStats
    {
        private readonly Dictionary<long, int> _hitsDealt = new Dictionary<long, int>();
        private readonly Dictionary<long, int> _hitsTaken = new Dictionary<long, int>();

        /// <summary>权威战斗事件总数（含开火/着墙/复活等）。</summary>
        public long TotalEvents { get; private set; }

        /// <summary>命中事件数（Hit + Crit）。</summary>
        public long HitEvents { get; private set; }

        /// <summary>击杀事件数（Death）。</summary>
        public long DeathEvents { get; private set; }

        /// <summary>开火事件数（Fire）。</summary>
        public long FireEvents { get; private set; }

        /// <summary>着墙事件数（ObstacleHit）。</summary>
        public long ObstacleHits { get; private set; }

        /// <summary>最近一次命中的目标实体。</summary>
        public long LastHitTarget { get; private set; }

        /// <summary>最近一次命中的伤害值。</summary>
        public int LastHitDamage { get; private set; }

        /// <summary>本端（该实体）造成的命中次数。</summary>
        public int HitsDealtBy(long entityId) => _hitsDealt.TryGetValue(entityId, out int v) ? v : 0;

        /// <summary>本端承受的命中次数。</summary>
        public int HitsTakenBy(long entityId) => _hitsTaken.TryGetValue(entityId, out int v) ? v : 0;

        /// <summary>记一条权威战斗事件；按 kind 分类，<paramref name="localEntityId"/> 用于归属统计。</summary>
        public void Record(FrameEventKind kind, long a, long b, int value, long localEntityId)
        {
            TotalEvents++;
            switch (kind)
            {
                case FrameEventKind.Hit:
                case FrameEventKind.Crit:
                    HitEvents++;
                    LastHitTarget = a == localEntityId ? b : a;
                    LastHitDamage = value;
                    if (a == localEntityId) Add(_hitsTaken, a);
                    else if (b == localEntityId) Add(_hitsDealt, b);
                    break;
                case FrameEventKind.Death:
                    DeathEvents++;
                    break;
                case FrameEventKind.Fire:
                    FireEvents++;
                    break;
                case FrameEventKind.ObstacleHit:
                    ObstacleHits++;
                    break;
            }
        }

        /// <summary>换局清空。</summary>
        public void Reset()
        {
            _hitsDealt.Clear();
            _hitsTaken.Clear();
            TotalEvents = 0;
            HitEvents = 0;
            DeathEvents = 0;
            FireEvents = 0;
            ObstacleHits = 0;
            LastHitTarget = 0;
            LastHitDamage = 0;
        }

        private static void Add(Dictionary<long, int> map, long key)
            => map[key] = (map.TryGetValue(key, out int v) ? v : 0) + 1;
    }
}
