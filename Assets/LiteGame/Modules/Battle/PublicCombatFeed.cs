using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 公共战斗事件面（《状态同步专项设计》§5.2 CombatEvent 面）：独立包
    /// <see cref="PacketType.CombatEvents"/> 携带的窗口 → **去重后的分类队列**，供各表现驱动逐条取用。
    ///
    /// **它取代了什么**：原 `PublicKillFeed` / `PublicFireFeed` / `PublicHitFeed` 三件——三份几乎相同的
    /// "已见记忆环 + 去重 + 队列"实现，每加一类事件就要再抄一份。现在只有**一条**去重与分发路径：
    /// 事件按 <see cref="FrameEventKind"/> 分流到各自的消费队列（视图/HUD/音效按需取用，互不干扰）。
    ///
    /// **去重**：键 = <see cref="CombatEventRecord.SameKey"/>（kind + 双方 + 帧），与服务端窗口的聚合键
    /// **严格同源**——窗口随每份事件批全量重发，记忆环保证一条只触发一次。
    ///
    /// **首份 / 重连**：第一份批里的记录一律标为"已见"而不入队（**不重播历史**：重连后不该突然
    /// 刷出一串断线前的击杀）；<see cref="Reset"/> 后下一份按"首份"处理。
    ///
    /// **本地过滤不在本件**：本地开火/命中该不该播归各消费者自己判（如远端火光驱动跳过本地实体），
    /// 本件保持"纯事实分发"——它不持表现、不碰 UI/VFX（同原有三件的定位）。
    ///
    /// 生命周期归 <c>BattleContext</c>（Match Scope 内），Dispose 即弃。
    /// </summary>
    public sealed class PublicCombatFeed
    {
        /// <summary>单条命中（伤害数字/受击特效/命中音效的消费面）。</summary>
        public readonly struct HitEntry
        {
            public readonly long Shooter;
            public readonly long Target;
            public readonly int Frame;
            public readonly int Value;
            public readonly float PosX;
            public readonly float PosY;
            public readonly float PosZ;

            /// <summary>true = 至少一发爆头（服务端已按 (shooter,target,frame) 聚合升档）。</summary>
            public readonly bool Crit;

            public HitEntry(long shooter, long target, int frame, int value,
                float posX, float posY, float posZ, bool crit)
            {
                Shooter = shooter;
                Target = target;
                Frame = frame;
                Value = value;
                PosX = posX;
                PosY = posY;
                PosZ = posZ;
                Crit = crit;
            }
        }

        /// <summary>单条击杀/死亡（击杀提示行 与 阵亡归属/死亡音效 共用）。</summary>
        public readonly struct KillEntry
        {
            /// <summary>击杀者实体 Id（0 = 无归属：自杀/环境伤害）。</summary>
            public readonly long Killer;

            /// <summary>死者实体 Id。</summary>
            public readonly long Victim;

            /// <summary>击杀发生帧。</summary>
            public readonly int Frame;

            /// <summary>击杀帧击杀者的装备武器槽（-1 = 无归属/未装备）——击杀提示的武器图标下标。</summary>
            public readonly int WeaponSlot;

            public KillEntry(long killer, long victim, int frame, int weaponSlot)
            {
                Killer = killer;
                Victim = victim;
                Frame = frame;
                WeaponSlot = weaponSlot;
            }
        }

        /// <summary>单条开火（远端开火火光）。</summary>
        public readonly struct FireEntry
        {
            public readonly long Shooter;
            public readonly int Frame;

            public FireEntry(long shooter, int frame)
            {
                Shooter = shooter;
                Frame = frame;
            }
        }

        /// <summary>"已见"记忆环容量：&gt; 服务端窗口上界（<c>CombatEventWindow.Capacity = 24</c>），
        /// 足以容纳若干份批的重复重发。</summary>
        private const int SeenCapacity = 96;

        private readonly CombatEventRecord[] _seen = new CombatEventRecord[SeenCapacity];
        private int _seenCount;

        private readonly Queue<HitEntry> _hits = new Queue<HitEntry>(32);
        private readonly Queue<KillEntry> _kills = new Queue<KillEntry>(8);              // HUD 击杀提示
        private readonly Queue<KillEntry> _killFeedback = new Queue<KillEntry>(8);        // 阵亡归属/死亡音效
        private readonly Queue<FireEntry> _fires = new Queue<FireEntry>(16);

        private bool _initialized;

        /// <summary>待消费命中条数（诊断/测试面）。</summary>
        public int HitCount => _hits.Count;

        /// <summary>待展示击杀条数（HUD）。</summary>
        public int KillCount => _kills.Count;

        /// <summary>待消费阵亡反馈条数（与 HUD 队列独立——视图 Attach 前到达的击杀不丢）。</summary>
        public int KillFeedbackCount => _killFeedback.Count;

        /// <summary>待播远端开火条数。</summary>
        public int FireCount => _fires.Count;

        /// <summary>
        /// 喂入一份事件批：首份/重连后首份 → 仅标记已见（不重播）；否则按 kind 分流入队。
        /// 未知 kind（未来端）与坏记录由 <see cref="CombatEventCodec.TryFromProto"/> 判掉——不猜、不改写。
        /// </summary>
        public void Ingest(CombatEventBatch batch)
        {
            if (batch == null) return;

            if (!_initialized)
            {
                _initialized = true;
                // 首份只标已见、不入队：未知 kind 也要标（否则未来端的多余类型会在每次批里被反复尝试）。
                for (int i = 0; i < batch.Records.Count; i++)
                    if (CombatEventCodec.TryFromProto(batch.Records[i], out CombatEventRecord seen))
                        MarkSeen(in seen);
                return;
            }

            for (int i = 0; i < batch.Records.Count; i++)
            {
                if (!CombatEventCodec.TryFromProto(batch.Records[i], out CombatEventRecord e)) continue;
                if (IsSeen(in e)) continue;
                MarkSeen(in e);
                Dispatch(in e);
            }
        }

        /// <summary>按 kind 分流（**唯一分发点**：新增事件类型只在这里加一个 case，
        /// 不需要改去重/记忆环/生命周期）。</summary>
        private void Dispatch(in CombatEventRecord e)
        {
            switch (e.Kind)
            {
                case FrameEventKind.Hit:
                case FrameEventKind.Crit:
                    _hits.Enqueue(new HitEntry(e.B, e.A, e.Frame, e.Value, e.Pos.X, e.Pos.Y, e.Pos.Z,
                        e.Kind == FrameEventKind.Crit));
                    return;

                case FrameEventKind.Death:
                {
                    var kill = new KillEntry(e.B, e.A, e.Frame, e.Value);   // Value = 击杀武器槽（见 proto 注释）
                    _kills.Enqueue(kill);
                    _killFeedback.Enqueue(kill);                          // 两条独立队列：HUD 与表现各取各的
                    return;
                }

                case FrameEventKind.Fire:
                    _fires.Enqueue(new FireEntry(e.A, e.Frame));
                    return;

                default:
                    // Explosion / Respawn / ObstacleHit 等：窗口已收（客户端本地表现走帧事件），
                    // 网络面暂无独立消费者。将来接消费时在 switch 加一个 case 即可。
                    return;
            }
        }

        /// <summary>取出一条待消费命中（FIFO）；队列空 = false。</summary>
        public bool TryDequeueHit(out HitEntry entry)
        {
            if (_hits.Count == 0) { entry = default; return false; }
            entry = _hits.Dequeue();
            return true;
        }

        /// <summary>取出一条待展示击杀（HUD，FIFO）；队列空 = false。</summary>
        public bool TryDequeueKill(out KillEntry entry)
        {
            if (_kills.Count == 0) { entry = default; return false; }
            entry = _kills.Dequeue();
            return true;
        }

        /// <summary>取出一条待处理的阵亡反馈（阵亡归属/死亡音效）；队列空 = false。</summary>
        public bool TryDequeueKillFeedback(out KillEntry entry)
        {
            if (_killFeedback.Count == 0) { entry = default; return false; }
            entry = _killFeedback.Dequeue();
            return true;
        }

        /// <summary>取出一条待播远端开火（FIFO）；队列空 = false。</summary>
        public bool TryDequeueFire(out FireEntry entry)
        {
            if (_fires.Count == 0) { entry = default; return false; }
            entry = _fires.Dequeue();
            return true;
        }

        /// <summary>重连：清全部队列并复位"已见"记忆——下一份批按"首份"重新标记（不重播断线前事件）。</summary>
        public void Reset()
        {
            _hits.Clear();
            _kills.Clear();
            _killFeedback.Clear();
            _fires.Clear();
            _seenCount = 0;
            _initialized = false;
        }

        private bool IsSeen(in CombatEventRecord e)
        {
            int n = _seenCount < SeenCapacity ? _seenCount : SeenCapacity;
            for (int i = 0; i < n; i++)
            {
                int idx = ((_seenCount - 1 - i) % SeenCapacity + SeenCapacity) % SeenCapacity;
                ref CombatEventRecord k = ref _seen[idx];
                if (k.SameKey(in e)) return true;
            }
            return false;
        }

        /// <summary>标记已见（<b>唯一写入口</b>）：Sim 记录与 wire 记录两条路都落到同一个环，
        /// 因此"首份标记"与"消费后标记"用的是同一套键，不会出现两套记忆。</summary>
        private void MarkSeen(in CombatEventRecord e)
        {
            _seen[_seenCount % SeenCapacity] = e;
            _seenCount++;
        }
    }
}
