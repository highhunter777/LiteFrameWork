namespace RoomServer.Runtime
{
    /// <summary>
    /// 单条开火记录（房间级**纯数据**）：开火者实体 Id 与发生帧。不带 proto（R11 纯度——
    /// wire 映射归 App 层 <c>FireSignalPacker</c>）。
    /// </summary>
    public readonly struct RoomFireRecord
    {
        /// <summary>开火者实体 Id。</summary>
        public readonly long Shooter;

        /// <summary>开火发生的权威帧。</summary>
        public readonly int Frame;

        public RoomFireRecord(long shooter, int frame)
        {
            Shooter = shooter;
            Frame = frame;
        }
    }

    /// <summary>
    /// 最近开火窗口（有界环；远端开火火光的**公共面源**，随每份快照全量携带——30Hz 自带丢包收敛）。
    /// 上游每次**实际开火**（武器门放行的扣扳机）<see cref="Add"/> 一条（满则覆盖最旧）：
    /// 窗口容量即"客户端漏收快照时最多可补回的开火数"。
    /// 遍历序 <see cref="Get"/>：下标 0 = 最旧 → 客户端按序消费。
    /// </summary>
    public sealed class FireRecordWindow
    {
        private readonly RoomFireRecord[] _items;
        private int _head;      // 最旧下标
        private int _count;

        public int Capacity => _items.Length;
        public int Count => _count;

        public FireRecordWindow(int capacity = 16)
        {
            if (capacity < 1) capacity = 1;
            _items = new RoomFireRecord[capacity];
        }

        /// <summary>记入一条开火（满则覆盖最旧——窗口只保最近 N 条）。</summary>
        public void Add(long shooter, int frame)
        {
            if (_count < _items.Length)
            {
                _items[(_head + _count) % _items.Length] = new RoomFireRecord(shooter, frame);
                _count++;
                return;
            }
            _items[_head] = new RoomFireRecord(shooter, frame);
            _head = (_head + 1) % _items.Length;
        }

        /// <summary>按下标取（0 = 最旧）；越界抛。</summary>
        public RoomFireRecord Get(int index)
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
