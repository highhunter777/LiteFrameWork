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
