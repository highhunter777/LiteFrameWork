using System.Collections.Generic;

namespace LiteClient
{
    /// <summary>
    /// 公共开火面缓冲（远端开火火光的公共面源）：快照携带的最近开火窗口
    /// （<c>MatchStateDelta.recent_fires</c>）→ **去重后的待播队列**，供表现驱动逐条取用
    /// （本件不持表现/不碰 VFX——纯数据协作件，同 <c>PublicKillFeed</c> 定位）。
    ///
    /// - **去重键** <c>(shooter, frame)</c>：窗口随每份快照全量重发，同一开火会在多份快照里重复出现；
    ///   记忆环（容量 &gt; 服务端窗口）确保每条只入队一次；
    /// - **首份快照 / 重连**：窗口内既有记录一律标为"已见"而不入队——**不重播历史开火**
    ///   （<see cref="Reset"/> 后下一份快照按"首份"处理）；
    /// - **本地开火不消费本队列**（消费者侧按本地实体 Id 过滤）：本地开火走本地帧事件即时播，
    ///   避免"预测即时闪 + 权威回闪"双闪。
    /// 生命周期归 <c>BattleContext</c>（Match Scope 内），Dispose 即弃。
    /// </summary>
    public sealed class PublicFireFeed
    {
        /// <summary>单条待播开火（表现驱动消费面）。</summary>
        public readonly struct Entry
        {
            /// <summary>开火者实体 Id。</summary>
            public readonly long Shooter;

            /// <summary>开火发生的权威帧。</summary>
            public readonly int Frame;

            public Entry(long shooter, int frame)
            {
                Shooter = shooter;
                Frame = frame;
            }
        }

        private struct Key { public long Shooter; public int Frame; }

        /// <summary>"已见"记忆环容量（&gt; 服务端窗口 16，容纳若干份快照的重复重发）。</summary>
        private const int SeenCapacity = 64;

        private readonly Key[] _seen = new Key[SeenCapacity];
        private int _seenCount;
        private readonly Queue<Entry> _pending = new Queue<Entry>(16);
        private bool _initialized;

        /// <summary>待播条数（诊断/测试面）。</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// 喂入一份快照的最近开火窗口：首份/重连后首份 → 仅标记已见（不重播）；否则新记录入队。
        /// </summary>
        public void Ingest(LiteNet.Proto.StateSnapshot snapshot)
        {
            LiteNet.Proto.MatchStateDelta match = snapshot?.Match;
            if (match == null) return;

            if (!_initialized)
            {
                _initialized = true;
                for (int i = 0; i < match.RecentFires.Count; i++) MarkSeen(match.RecentFires[i]);
                return;
            }

            for (int i = 0; i < match.RecentFires.Count; i++)
            {
                LiteNet.Proto.FireRecord r = match.RecentFires[i];
                if (IsSeen(r)) continue;
                MarkSeen(r);
                _pending.Enqueue(new Entry(r.Shooter, r.Frame));
            }
        }

        /// <summary>取出一条待播开火（FIFO：先发生先播）；队列空 = false。</summary>
        public bool TryDequeue(out Entry entry)
        {
            if (_pending.Count == 0)
            {
                entry = default;
                return false;
            }
            entry = _pending.Dequeue();
            return true;
        }

        /// <summary>重连：清待播队列并复位"已见"记忆——下一份快照的窗口按"首份"重新标记（不重播断线前开火）。</summary>
        public void Reset()
        {
            _pending.Clear();
            _seenCount = 0;
            _initialized = false;
        }

        private bool IsSeen(in LiteNet.Proto.FireRecord r)
        {
            int n = _seenCount < SeenCapacity ? _seenCount : SeenCapacity;
            for (int i = 0; i < n; i++)
            {
                int idx = ((_seenCount - 1 - i) % SeenCapacity + SeenCapacity) % SeenCapacity;
                ref Key k = ref _seen[idx];
                if (k.Shooter == r.Shooter && k.Frame == r.Frame) return true;
            }
            return false;
        }

        private void MarkSeen(in LiteNet.Proto.FireRecord r)
        {
            _seen[_seenCount % SeenCapacity] = new Key { Shooter = r.Shooter, Frame = r.Frame };
            _seenCount++;
        }
    }
}
