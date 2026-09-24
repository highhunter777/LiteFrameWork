using System;
using System.Collections.Generic;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 角色动画播放器（《动画模块专项设计》§5/§6——批次"基础契约与 UI 接缝"的核心件）。
    ///
    /// 职责边界：**只解决视觉通道归属**（§6"业务优先级由 Sim/Driver 解释；播放器只解决视觉通道归属"）。
    /// 它不认识"角色是否允许换弹"、不扣弹、不写 Sim，也不碰 VFX/Audio。
    ///
    /// 落实的关键契约：
    /// - **一个已接受请求只产生一次终态**，且回调重入不能让旧请求再次结束或写回新实例（§5）；
    /// - **旧 Handle 不能停止复用对象的新播放**——句柄带 (播放器, Owner 代次, 请求序号) 三分量（§5）；
    /// - **替换加载中的请求必须终止旧待提交 Handle**，迟到加载只释放自己的资源、不抢回通道（§6）；
    /// - **终态记录有界保留**，过期查询返回未找到，不把已完成 Handle 永久留在全局表（§5）；
    /// - 各通道**至多一个待提交 + 一个当前播放**，无默认队列（§6/§12）；
    /// - 销毁顺序：代次失效 → 取消在途 → 撤销订阅 → 释放后端（§9）。
    ///
    /// 时钟：本类不持有分域时钟——由调用方（Driver/容器）按 §7 的更新次序把已缩放的
    /// delta 交给 <c>Tick</c>；播放器**不再次乘 TimeScale**（§7"避免重复缩放"）。
    /// </summary>
    public sealed class CharacterAnimationPlayer : IDisposable
    {
        /// <summary>终态记录上限（§5"有界保留"；超出按最旧淘汰，淘汰计数留痕）。</summary>
        public const int TerminalRetentionCapacity = 64;

        private static int s_nextPlayerId = 1;

        /// <summary>终态记录（含 ID/通道——<c>TryGetState</c> 对已终态句柄也要能回答"它是什么"）。</summary>
        private readonly struct TerminalRecord
        {
            public readonly AnimationId Id;
            public readonly AnimationChannel Channel;
            public readonly AnimationTerminalState State;

            public TerminalRecord(AnimationId id, AnimationChannel channel, AnimationTerminalState state)
            {
                Id = id;
                Channel = channel;
                State = state;
            }
        }

        private readonly IAnimationBackend _backend;
        private readonly AnimationProfile _profile;
        private readonly Dictionary<AnimationChannel, AnimationChannelSlot> _slots = new Dictionary<AnimationChannel, AnimationChannelSlot>(4);
        private readonly Dictionary<int, TerminalRecord> _terminals = new Dictionary<int, TerminalRecord>();
        private readonly Queue<int> _terminalOrder = new Queue<int>();

        private readonly int _playerId;
        private int _ownerGeneration;
        private int _sequence;
        private bool _disposed;

        /// <summary>终态回调（Handle + 终态；**恰好一次**）。Owner 订阅；播放器不等待它推进任何事实（§5）。</summary>
        public event Action<AnimationHandle, AnimationTerminalState> OnTerminal;

        /// <summary>因容量淘汰而丢弃的终态记录数（诊断；§12"容量不足必须计数诊断"）。</summary>
        public int EvictedTerminalRecords { get; private set; }

        /// <summary>被拒绝的请求数（诊断）。</summary>
        public int RejectedRequests { get; private set; }

        /// <summary>Owner 代次（失效即旧句柄全体作废；§9"使 Owner 代次失效并停止接受请求"）。</summary>
        public int OwnerGeneration => _ownerGeneration;

        public bool IsDisposed => _disposed;

        public CharacterAnimationPlayer(IAnimationBackend backend, AnimationProfile profile, int ownerGeneration = 0)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ownerGeneration = ownerGeneration;
            _playerId = System.Threading.Interlocked.Increment(ref s_nextPlayerId);
        }

        /// <summary>提交播放（§5 Play）。返回拒绝时**不改变现有播放**。</summary>
        public AnimationStartResult Play(in AnimationRequest request)
        {
            if (_disposed)
                return AnimationStartResult.Reject(AnimationStartResult.Reason.OwnerUnavailable);

            if (!_profile.TryResolve(request, out var resolved, out var reason))
            {
                RejectedRequests++;
                return AnimationStartResult.Reject(reason);
            }

            // 能力校验：后端不支持的能力**明确拒绝**，不静默降级（§4"不把不支持的能力静默降级"）
            AnimationBackendCapabilities caps = _backend.Capabilities;
            if (resolved.StartNormalized > 0f && (caps & AnimationBackendCapabilities.StartAtNormalized) == 0)
            { RejectedRequests++; return AnimationStartResult.Reject(AnimationStartResult.Reason.UnsupportedCapability); }
            if (resolved.Speed != 1f && (caps & AnimationBackendCapabilities.SpeedOverride) == 0)
            { RejectedRequests++; return AnimationStartResult.Reject(AnimationStartResult.Reason.UnsupportedCapability); }
            if (resolved.Channel != AnimationChannel.Locomotion && (caps & AnimationBackendCapabilities.LayeredChannels) == 0)
            { RejectedRequests++; return AnimationStartResult.Reject(AnimationStartResult.Reason.UnsupportedCapability); }

            var handle = new AnimationHandle(_playerId, _ownerGeneration, ++_sequence);
            var slot = Slot(resolved.Channel);

            if (slot.HasCurrent)
                Finish(resolved.Channel, slot.Current, slot.CurrentId, AnimationTerminalState.Interrupted);   // 替换：旧播放（含加载中）终止

            slot.Current = handle;
            slot.CurrentId = resolved.Id;
            slot.Loading = resolved.RequiresLoad;
            slot.Active = true;

            if (!resolved.RequiresLoad)
                Commit(resolved.Channel, slot, handle, in resolved);

            return AnimationStartResult.Accept(handle);
        }

        /// <summary>
        /// 装载完成回填（由资源侧在装载结束时调用）。**只有仍是该通道当前请求时才提交**——
        /// 迟到结果只释放自己的资源，绝不抢回通道（§6）。
        /// </summary>
        public void CompleteLoad(AnimationHandle handle, bool loadSucceeded, in AnimationResolvedPlayback resolved)
        {
            if (_disposed) return;
            if (!TryFindCurrent(handle, out var channel, out var slot)) return;   // 已被替换/已终态：迟到结果丢弃

            slot.Loading = false;
            if (!loadSucceeded)
            {
                Finish(channel, handle, slot.CurrentId, AnimationTerminalState.Failed);   // 装载失败 → Failed 终态
                return;
            }
            Commit(channel, slot, handle, in resolved);
        }

        /// <summary>
        /// 停止（§5 Stop）。返回 false = 句柄不指向当前播放（重复 Stop / 旧句柄 / 未知）。
        /// **不重复通知**：已终态的 Handle 再次 Stop 是 no-op。
        /// </summary>
        public bool Stop(AnimationHandle handle, AnimationStopReason reason)
        {
            if (_disposed) return false;
            if (IsTerminal(handle)) return false;                     // 重复 Stop 不重复通知（§13-3）
            if (!TryFindCurrent(handle, out var channel, out var slot)) return false;   // 旧 Handle 不能停新播放（§5）

            Finish(channel, handle, slot.CurrentId, ToTerminal(reason));
            return true;
        }

        /// <summary>查询播放状态。**终态记录有界保留**——过期/未知返回 false（§5）。</summary>
        public bool TryGetState(AnimationHandle handle, out AnimationPlaybackState state)
        {
            state = default;

            if (_terminals.TryGetValue(Key(handle), out var record))
            {
                state = new AnimationPlaybackState(handle, record.Id, record.Channel,
                    isLoading: false, isPlaying: false, record.State);
                return true;
            }

            if (handle.OwnerGeneration != _ownerGeneration) return false;    // 旧代次且无终态记录：已不可见
            if (!TryFindCurrent(handle, out var channel, out var slot)) return false;

            state = new AnimationPlaybackState(handle, slot.CurrentId, channel,
                slot.Loading, !slot.Loading, AnimationTerminalState.None);
            return true;
        }

        /// <summary>
        /// 每帧推进（**唯一驱动入口**，§7"Graph Evaluate 只由一个驱动器调用"）：
        /// 先采样各通道，再把自然结束的播放收成 Completed。delta 由调用方按分域时钟给出；播放器不再次缩放。
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (_disposed) return;
            if (float.IsNaN(deltaSeconds)) return;

            // 非分配迭代：增量字典可能删空键，但 Tick 期间 Finish 只改值不改集合结构——
            // 用枚举器比"拷进静态缓冲"更安全（静态缓冲在嵌套播放器场景会被互相覆盖）。
            foreach (var pair in _slots)
            {
                var slot = pair.Value;
                if (slot == null || !slot.HasCurrent || slot.Loading) continue;

                if (_backend.Tick(pair.Key, deltaSeconds))
                    Finish(pair.Key, slot.Current, slot.CurrentId, AnimationTerminalState.Completed);   // 自然结束 → Completed
            }
        }

        /// <summary>
        /// Owner 换代（对象被池化复用，§9"使 Owner 代次失效并停止接受请求"）：
        /// 旧代次的所有当前播放得 OwnerDisposed 终态，随后递增代次——旧句柄就此失效。
        /// </summary>
        public int BumpOwnerGeneration()
        {
            if (_disposed) return _ownerGeneration;
            DisposeCurrentPlays();
            _ownerGeneration++;
            return _ownerGeneration;
        }

        /// <summary>
        /// 释放（§9 销毁顺序）：代次失效 → 停止接受请求 → 在途/当前播放确定终态 → 撤销订阅 → 释放后端。幂等。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            DisposeCurrentPlays();

            _disposed = true;                       // 使代次失效并停止接受请求（后续 Play 一律 OwnerUnavailable）
            OnTerminal = null;                      // 撤销订阅（不再向已释放的 Owner 回调）

            _backend.Dispose();                     // 最后才释放后端持有的引擎对象/绑定

            _slots.Clear();
            _terminals.Clear();
            _terminalOrder.Clear();
        }

        // ---- 内部 ----

        private void Commit(AnimationChannel channel, AnimationChannelSlot slot, AnimationHandle handle, in AnimationResolvedPlayback resolved)
        {
            if (!_backend.TryPlay(in resolved))
            {
                Finish(channel, handle, slot.CurrentId, AnimationTerminalState.Failed);   // 后端拒绝/执行失败：不假装在播
                return;
            }
            slot.Loading = false;
        }

        /// <summary>把所有在途/在播收成 OwnerDisposed（BumpOwnerGeneration / Dispose 共用）。
        /// 先快照再收口：Finish 会改 _slots 的值，不能边遍历边改。</summary>
        private void DisposeCurrentPlays()
        {
            var pending = new List<(AnimationChannel channel, AnimationHandle handle, AnimationId id)>(_slots.Count);
            foreach (var pair in _slots)
            {
                var slot = pair.Value;
                if (slot == null || !slot.HasCurrent) continue;
                pending.Add((pair.Key, slot.Current, slot.CurrentId));
            }

            for (int i = 0; i < pending.Count; i++)
                Finish(pending[i].channel, pending[i].handle, pending[i].id, AnimationTerminalState.OwnerDisposed);
        }

        /// <summary>终态收口（**唯一入口**——保证恰好一次；重入安全）。</summary>
        private void Finish(AnimationChannel channel, AnimationHandle handle, AnimationId id, AnimationTerminalState terminal)
        {
            if (IsTerminal(handle)) return;                          // 已终态：不重复通知（§5/§13-3）

            Remember(handle, id, channel, terminal);

            if (_slots.TryGetValue(channel, out var slot) && handle.Equals(slot.Current))
            {
                if (!slot.Loading) _backend.TryStop(channel);         // 加载中无需停（从未提交）
                slot.Current = default;
                slot.CurrentId = default;
                slot.Loading = false;
                slot.Active = false;
            }

            OnTerminal?.Invoke(handle, terminal);
        }

        private void Remember(AnimationHandle handle, AnimationId id, AnimationChannel channel, AnimationTerminalState terminal)
        {
            int key = Key(handle);
            if (_terminals.ContainsKey(key)) return;

            while (_terminalOrder.Count >= TerminalRetentionCapacity)
            {
                int evicted = _terminalOrder.Dequeue();
                _terminals.Remove(evicted);
                EvictedTerminalRecords++;
            }

            _terminals[key] = new TerminalRecord(id, channel, terminal);
            _terminalOrder.Enqueue(key);
        }

        private bool IsTerminal(AnimationHandle handle) => _terminals.ContainsKey(Key(handle));

        private AnimationChannelSlot Slot(AnimationChannel channel)
        {
            if (!_slots.TryGetValue(channel, out var slot))
            {
                slot = new AnimationChannelSlot();
                _slots[channel] = slot;
            }
            return slot;
        }

        /// <summary>句柄是否仍是某通道的当前播放。</summary>
        private bool TryFindCurrent(AnimationHandle handle, out AnimationChannel channel, out AnimationChannelSlot slot)
        {
            foreach (var pair in _slots)
            {
                if (handle.Equals(pair.Value.Current)) { channel = pair.Key; slot = pair.Value; return true; }
            }
            channel = default;
            slot = null;
            return false;
        }

        private static AnimationTerminalState ToTerminal(AnimationStopReason reason)
            => reason switch
            {
                AnimationStopReason.Interrupted => AnimationTerminalState.Interrupted,
                AnimationStopReason.Cancelled => AnimationTerminalState.Cancelled,
                AnimationStopReason.OwnerDisposed => AnimationTerminalState.OwnerDisposed,
                AnimationStopReason.Failed => AnimationTerminalState.Failed,
                _ => AnimationTerminalState.Cancelled,
            };

        private static int Key(AnimationHandle h) => (h.PlayerId * 397) ^ (h.OwnerGeneration * 31) ^ h.RequestSequence;
    }
}
