using System.Collections.Generic;

namespace LiteClient
{
    /// <summary>
    /// 公共击杀面缓冲（《战斗HUD层级设计》§2 击杀提示面 / 《联机战斗演示专项设计》§3）：
    /// 快照携带的最近击杀窗口（<c>MatchStateDelta.recent_kills</c>）→ **去重后的待展示队列**，
    /// 供 HUD 驱动逐条取用（本件不持表现/不碰 UI——纯数据协作件，同 <c>BattleSnapshotMirror</c> 定位）。
    ///
    /// - **去重键** <c>(killer, victim, frame)</c>：窗口随每份快照全量重发，同一击杀会在多份快照里重复出现；
    ///   记忆环（容量 64 &gt; 服务端窗口 16）确保每条只入队一次；
    /// - **首份快照 / 重连**：窗口内既有记录一律标为"已见"而不入队——**不重播历史击杀**
    ///   （<see cref="Reset"/> 后下一份快照按"首份"处理）；
    /// - **全局**：记录来自权威房间（含他人击杀）——不止本地造成的击杀。
    /// 生命周期归 <c>BattleContext</c>（Match Scope 内），Dispose 即弃。
    /// </summary>
    public sealed class PublicKillFeed
    {
        /// <summary>单条待展示击杀（UI 驱动消费面）。</summary>
        public readonly struct Entry
        {
            /// <summary>击杀者实体 Id（0 = 无归属：自杀/环境伤害）。</summary>
            public readonly long Killer;
            /// <summary>死者实体 Id。</summary>
            public readonly long Victim;
            /// <summary>击杀发生帧。</summary>
            public readonly int Frame;
            /// <summary>击杀帧击杀者的装备武器槽（-1 = 无归属/未装备）。</summary>
            public readonly int WeaponSlot;

            public Entry(long killer, long victim, int frame, int weaponSlot)
            {
                Killer = killer;
                Victim = victim;
                Frame = frame;
                WeaponSlot = weaponSlot;
            }
        }

        private struct Key { public long Killer; public long Victim; public int Frame; }

        /// <summary>"已见"记忆环容量（&gt; 服务端窗口 16，容纳若干份快照的重复重发）。</summary>
        private const int SeenCapacity = 64;

        private readonly Key[] _seen = new Key[SeenCapacity];
        private int _seenCount;
        private readonly Queue<Entry> _pending = new Queue<Entry>(16);
        private bool _initialized;

        /// <summary>待展示条数（诊断/测试面）。</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// 喂入一份快照的最近击杀窗口：首份/重连后首份 → 仅标记已见（不重播）；否则新记录入队。
        /// </summary>
        public void Ingest(LiteNet.Proto.StateSnapshot snapshot)
        {
            LiteNet.Proto.MatchStateDelta match = snapshot?.Match;
            if (match == null) return;

            if (!_initialized)
            {
                _initialized = true;
                for (int i = 0; i < match.RecentKills.Count; i++) MarkSeen(match.RecentKills[i]);
                return;
            }

            for (int i = 0; i < match.RecentKills.Count; i++)
            {
                LiteNet.Proto.KillRecord r = match.RecentKills[i];
                if (IsSeen(r)) continue;
                MarkSeen(r);
                _pending.Enqueue(new Entry(r.Killer, r.Victim, r.Frame, r.WeaponSlot));
            }
        }

        /// <summary>取出一条待展示击杀（FIFO：先发生先展示）；队列空 = false。</summary>
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

        /// <summary>重连：清待展示队列并复位"已见"记忆——下一份快照的窗口按"首份"重新标记（不重播断线前击杀）。</summary>
        public void Reset()
        {
            _pending.Clear();
            _seenCount = 0;
            _initialized = false;
        }

        private bool IsSeen(in LiteNet.Proto.KillRecord r)
        {
            int n = _seenCount < SeenCapacity ? _seenCount : SeenCapacity;
            for (int i = 0; i < n; i++)
            {
                int idx = ((_seenCount - 1 - i) % SeenCapacity + SeenCapacity) % SeenCapacity;
                ref Key k = ref _seen[idx];
                if (k.Killer == r.Killer && k.Victim == r.Victim && k.Frame == r.Frame) return true;
            }
            return false;
        }

        private void MarkSeen(in LiteNet.Proto.KillRecord r)
        {
            _seen[_seenCount % SeenCapacity] = new Key { Killer = r.Killer, Victim = r.Victim, Frame = r.Frame };
            _seenCount++;
        }
    }
}
