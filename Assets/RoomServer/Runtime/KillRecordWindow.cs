using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 单条击杀记录（房间级**纯数据**）：击杀者/死者实体 Id、发生帧、击杀帧的装备武器槽。
    /// 不带 proto/字符串（R11 纯度——wire 映射归 App 层 <c>KillFeedPacker</c>）。
    /// </summary>
    public readonly struct RoomKillRecord
    {
        /// <summary>击杀者实体 Id（0 = 无归属：自杀/环境伤害）。</summary>
        public readonly long Killer;

        /// <summary>死者实体 Id。</summary>
        public readonly long Victim;

        /// <summary>击杀发生的权威帧。</summary>
        public readonly int Frame;

        /// <summary>击杀帧击杀者的装备武器槽（-1 = 无归属/未装备）。</summary>
        public readonly int WeaponSlot;

        public RoomKillRecord(long killer, long victim, int frame, int weaponSlot)
        {
            Killer = killer;
            Victim = victim;
            Frame = frame;
            WeaponSlot = weaponSlot;
        }
    }

    /// <summary>
    /// 最近击杀窗口（有界环；全局击杀提示的**公共面源**，随每份快照全量携带——30Hz 自带丢包收敛）。
    /// 上游每次击杀 <see cref="Add"/> 一条（满则覆盖最旧）：窗口容量即"客户端漏收快照时最多可补回的击杀数"。
    /// 遍历序 <see cref="Get"/>：下标 0 = 最旧 → 客户端按序排队展示。
    /// </summary>
    /// <summary>
    /// 单条权威命中记录（房间级纯数据）：射手/目标/发生帧/本帧聚合伤害/命中点/是否爆头。
    /// 同一 (shooter,target,frame) 的多弹丸由 <see cref="HitRecordWindow.AddOrAccumulate"/> 合并。
    /// </summary>
    public readonly struct RoomHitRecord
    {
        public readonly long Shooter;
        public readonly long Target;
        public readonly int Frame;
        public readonly int Value;
        public readonly SimVector3 Pos;
        public readonly bool Crit;

        public RoomHitRecord(long shooter, long target, int frame, int value, in SimVector3 pos, bool crit)
        {
            Shooter = shooter;
            Target = target;
            Frame = frame;
            Value = value;
            Pos = pos;
            Crit = crit;
        }
    }

    /// <summary>
    /// 最近命中窗口（有界环；命中/伤害/受击反馈的**公共面源**，随每份快照全量携带——30Hz 自带丢包收敛）。
    /// 上游在命中帧 <see cref="AddOrAccumulate"/>；同一 (shooter,target,frame) 只保留一条（多弹丸伤害聚合）。
    /// </summary>
    public sealed class HitRecordWindow
    {
        private readonly RoomHitRecord[] _items;
        private int _head;
        private int _count;

        public int Capacity => _items.Length;
        public int Count => _count;

        public HitRecordWindow(int capacity = 32)
        {
            if (capacity < 1) capacity = 1;
            _items = new RoomHitRecord[capacity];
        }

        /// <summary>记入一条命中；同 (shooter,target,frame) 已有记录则累加伤害并合并 Crit。</summary>
        public void AddOrAccumulate(long shooter, long target, int frame, int value, in SimVector3 pos, bool crit)
        {
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head + i) % _items.Length;
                ref RoomHitRecord existing = ref _items[idx];
                if (existing.Shooter != shooter || existing.Target != target || existing.Frame != frame) continue;
                _items[idx] = new RoomHitRecord(existing.Shooter, existing.Target, existing.Frame,
                    existing.Value + value, in existing.Pos, existing.Crit || crit);
                return;
            }

            if (_count < _items.Length)
            {
                _items[(_head + _count) % _items.Length] = new RoomHitRecord(shooter, target, frame, value, in pos, crit);
                _count++;
                return;
            }
            _items[_head] = new RoomHitRecord(shooter, target, frame, value, in pos, crit);
            _head = (_head + 1) % _items.Length;
        }

        /// <summary>按下标取（0 = 最旧）；越界抛。</summary>
        public RoomHitRecord Get(int index)
        {
            if (index < 0 || index >= _count) throw new System.ArgumentOutOfRangeException(nameof(index));
            return _items[(_head + index) % _items.Length];
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }

    public sealed class KillRecordWindow
    {
        private readonly RoomKillRecord[] _items;
        private int _head;      // 最旧下标
        private int _count;

        public int Capacity => _items.Length;
        public int Count => _count;

        public KillRecordWindow(int capacity = 16)
        {
            if (capacity < 1) capacity = 1;
            _items = new RoomKillRecord[capacity];
        }

        /// <summary>记入一条击杀（满则覆盖最旧——窗口只保最近 N 条）。</summary>
        public void Add(long killer, long victim, int frame, int weaponSlot)
        {
            if (_count < _items.Length)
            {
                _items[(_head + _count) % _items.Length] = new RoomKillRecord(killer, victim, frame, weaponSlot);
                _count++;
                return;
            }
            _items[_head] = new RoomKillRecord(killer, victim, frame, weaponSlot);
            _head = (_head + 1) % _items.Length;
        }

        /// <summary>按下标取（0 = 最旧）；越界抛。</summary>
        public RoomKillRecord Get(int index)
        {
            if (index < 0 || index >= _count) throw new System.ArgumentOutOfRangeException(nameof(index));
            return _items[(_head + index) % _items.Length];
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }
}
