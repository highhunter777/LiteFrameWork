using System;
using System.Collections.Generic;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 动画播放器。
    /// </summary>
    public sealed class AnimationPlayer : IDisposable
    {
        /// <summary>终态记录上限（有界保留；超出按最旧淘汰，淘汰计数留痕）。</summary>
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
        private readonly Dictionary<AnimationChannel, ChannelSlot> _slots = new Dictionary<AnimationChannel, ChannelSlot>(4);
        private readonly Dictionary<AnimationHandle, TerminalRecord> _terminals = new Dictionary<AnimationHandle, TerminalRecord>();
        private readonly Queue<AnimationHandle> _terminalOrder = new Queue<AnimationHandle>();

        private readonly int _playerId;
        private int _ownerGeneration;
        private int _sequence;
        private bool _disposed;

        /// <summary>终态回调（Handle + 终态；**恰好一次**）。Owner 订阅；播放器不等待它推进任何事实。</summary>
        public event Action<AnimationHandle, AnimationTerminalState> OnTerminal;

        /// <summary>因容量淘汰而丢弃的终态记录数（诊断；容量不足必须计数诊断）。</summary>
        public int EvictedTerminalRecords { get; private set; }

        /// <summary>被拒绝的请求数（诊断）。</summary>
        public int RejectedRequests { get; private set; }

        /// <summary>Owner 代次（失效即旧句柄全体作废；使 Owner 代次失效并停止接受请求）。</summary>
        public int OwnerGeneration => _ownerGeneration;

        public bool IsDisposed => _disposed;

        public AnimationPlayer(IAnimationBackend backend, AnimationProfile profile, int ownerGeneration = 0)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ownerGeneration = ownerGeneration;
            _playerId = System.Threading.Interlocked.Increment(ref s_nextPlayerId);
        }

        /// <summary>提交播放。返回拒绝时**不改变现有播放**。</summary>
        public AnimationStartResult Play(in AnimationRequest request)
        {
            if (_disposed)
                return AnimationStartResult.Reject(AnimationStartResult.Reason.OwnerUnavailable);

            if (!_profile.TryResolve(request, out var resolved, out var reason))
            {
                RejectedRequests++;
                return AnimationStartResult.Reject(reason);
            }

            // 能力校验：后端不支持的能力**明确拒绝**，不静默降级。
            if (!CapabilitiesAllow(resolved.StartNormalized, resolved.Speed, resolved.Channel))
            {
                RejectedRequests++;
                return AnimationStartResult.Reject(AnimationStartResult.Reason.UnsupportedCapability);
            }

            var handle = TakeChannel(resolved.Channel, resolved.Id, resolved.RequiresLoad, out var slot);
            if (!resolved.RequiresLoad)
                Commit(resolved.Channel, slot, handle, in resolved);

            return AnimationStartResult.Accept(handle);
        }

        /// <summary>
        /// 提交混合播放：与 <see cref="Play"/> **共用同一套通道仲裁** （<see cref="TakeChannel"/>——同通道替换收 Interrupted、句柄三分量、终态恰好一次。
        /// </summary>
        public AnimationStartResult PlayBlend(in AnimationBlendRequest request)
        {
            if (_disposed)
                return AnimationStartResult.Reject(AnimationStartResult.Reason.OwnerUnavailable);

            if (!_profile.TryResolveBlend(request, out var resolved, out var reason))
            {
                RejectedRequests++;
                return AnimationStartResult.Reject(reason);
            }

            // 能力位：混合路径**必需** ClipBlending——降级成单片段播放等于静默丢掉权重语义。
            if ((_backend.Capabilities & AnimationBackendCapabilities.ClipBlending) == 0
                || !CapabilitiesAllow(resolved.StartNormalized, resolved.Speed, resolved.Channel))
            {
                RejectedRequests++;
                return AnimationStartResult.Reject(AnimationStartResult.Reason.UnsupportedCapability);
            }

            var handle = TakeChannel(resolved.Channel, resolved.Id, requiresLoad: false, out var slot);
            if (!_backend.TryPlayBlend(in resolved))
                Finish(resolved.Channel, handle, slot.CurrentId, AnimationTerminalState.Failed);   // 后端拒绝/执行失败：不假装在播

            return AnimationStartResult.Accept(handle);
        }

        /// <summary>
        /// 就地更新混合权重（**连续调参路径**）： 权重随速度/方向逐帧变化时用它，而不是每帧 <see cref="PlayBlend"/>——后者会换句柄、 给旧播放收 Interrupted。
        /// </summary>
        public bool UpdateBlendWeights(AnimationHandle handle, float[] weights)
        {
            if (_disposed) return false;
            if (!TryFindCurrent(handle, out var channel, out var slot)) return false;
            if (slot.Loading) return false;                            // 加载中无节点可调（混合不走装载，防御）

            return _backend.TrySetBlendWeights(channel, weights);
        }

        /// <summary>
        /// 就地更新混合节点播放倍率（步频同步——移动腰射：AimWalk 原生步频 × 倍率 = 实际脚程， 免"走姿步频配跑速"滑步）。
        /// </summary>
        public bool TrySetBlendSpeed(AnimationHandle handle, float speedScale)
        {
            if (_disposed) return false;
            if (!TryFindCurrent(handle, out var channel, out var slot)) return false;
            if (slot.Loading) return false;

            return _backend.TrySetBlendSpeed(channel, speedScale);
        }

        /// <summary>
        /// 就地更新混合节点的**逐输入净播放速率**（步幅对齐——各片段按相位比/脚程换算给出净倍率； 见 <see cref="IAnimationBackend.TrySetBlendInputSpeeds"/>）。
        /// </summary>
        public bool TrySetBlendInputSpeeds(AnimationHandle handle, float[] speeds)
        {
            if (_disposed) return false;
            if (!TryFindCurrent(handle, out var channel, out var slot)) return false;
            if (slot.Loading) return false;

            return _backend.TrySetBlendInputSpeeds(channel, speeds);
        }

        /// <summary>
        /// 装载完成回填（由资源侧在装载结束时调用）。**只有仍是该通道当前请求时才提交**——
        /// 迟到结果只释放自己的资源，绝不抢回通道。
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
        /// 停止。
        /// </summary>
        public bool Stop(AnimationHandle handle, AnimationStopReason reason)
        {
            if (_disposed) return false;
            if (IsTerminal(handle))
            {
                ReleaseHeldChannel(handle);
                return false;
            }
            if (!TryFindCurrent(handle, out var channel, out var slot)) return false;   // 旧 Handle 不能停新播放

            Finish(channel, handle, slot.CurrentId, ToTerminal(reason));
            return true;
        }

        /// <summary>释放"完成但持帧"的通道占位（句柄须仍是该通道当前；不产生终态、不清终态记录）。
        /// 非持帧的已终态句柄在收口时已清槽位——此处查不到即 no-op。</summary>
        private void ReleaseHeldChannel(AnimationHandle handle)
        {
            if (!TryFindCurrent(handle, out var channel, out var slot)) return;
            _backend.TryStop(channel);
            slot.Current = default;
            slot.CurrentId = default;
            slot.Loading = false;
            slot.Active = false;
        }

        /// <summary>查询播放状态。**终态记录有界保留**——过期/未知返回 false。</summary>
        public bool TryGetState(AnimationHandle handle, out AnimationPlaybackState state)
        {
            state = default;

            if (_terminals.TryGetValue(handle, out var record))
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
        /// 每帧推进（**唯一驱动入口**——Graph Evaluate 只由一个驱动器调用）： **先单次采样全部通道**（一次 <c>Tick</c>，绝不逐通道循环驱动——那会把时间重复推进）， 再按返回的通道掩码。
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (_disposed) return;
            if (float.IsNaN(deltaSeconds)) return;

            AnimationChannelMask done = _backend.Tick(deltaSeconds);

            // 非分配迭代：增量字典可能删空键，但 Tick 期间 Finish 只改值不改集合结构——
            // 用枚举器比"拷进静态缓冲"更安全（静态缓冲在嵌套播放器场景会被互相覆盖）。
            foreach (var pair in _slots)
            {
                var slot = pair.Value;
                if (slot == null || !slot.HasCurrent || slot.Loading) continue;

                // 掩码里属于非活跃通道的位一律忽略（防迟到收口写错通道）。
                if ((done & AnimationChannelMasks.Of(pair.Key)) == 0) continue;

                Finish(pair.Key, slot.Current, slot.CurrentId, AnimationTerminalState.Completed);   // 自然结束 → Completed
            }
        }

        /// <summary>
        /// Owner 换代（对象被池化复用，使 Owner 代次失效并停止接受请求）：
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
        /// 释放（销毁顺序）：代次失效 → 停止接受请求 → 在途/当前播放确定终态 → 撤销订阅 → 释放后端。幂等。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            DisposeCurrentPlays();

            _disposed = true;                       // 使代次失效并停止接受请求（后续 Play 一律 OwnerUnavailable）
            OnTerminal = null;                      // 撤销订阅（不向已释放的 Owner 回调）

            _backend.Dispose();                     // 最后才释放后端持有的引擎对象/绑定

            _slots.Clear();
            _terminals.Clear();
            _terminalOrder.Clear();
        }

        // ---- 内部 ----。

        /// <summary>
        /// 通道仲裁共用段（<see cref="Play"/>/<see cref="PlayBlend"/> 同规矩，逐字复用——不允许两条路径长出两套仲裁）：
        /// 分配句柄（三分量身份）→ 旧当前播放（含加载中）收 Interrupted → 槽位换上新句柄。
        /// </summary>
        private AnimationHandle TakeChannel(AnimationChannel channel, AnimationId id, bool requiresLoad, out ChannelSlot slot)
        {
            var handle = new AnimationHandle(_playerId, _ownerGeneration, ++_sequence);
            slot = Slot(channel);

            if (slot.HasCurrent)
                Finish(channel, slot.Current, slot.CurrentId, AnimationTerminalState.Interrupted);

            slot.Current = handle;
            slot.CurrentId = id;
            slot.Loading = requiresLoad;
            slot.Active = true;
            return handle;
        }

        /// <summary>能力校验（Play/PlayBlend 共用）：后端不支持的能力**明确拒绝**，不静默降级。</summary>
        private bool CapabilitiesAllow(float startNormalized, float speed, AnimationChannel channel)
        {
            AnimationBackendCapabilities caps = _backend.Capabilities;
            if (startNormalized > 0f && (caps & AnimationBackendCapabilities.StartAtNormalized) == 0) return false;
            if (speed != 1f && (caps & AnimationBackendCapabilities.SpeedOverride) == 0) return false;
            // 逐通道能力门（对称拆位）：覆盖层不需 Mask（任何 rig 可用）、叠加层需后端构造出 Mask——
            // 各查各的能力位，缺失即提交前拒绝（不静默降级；§4 能力位诚实声明）
            if (channel == AnimationChannel.Override && (caps & AnimationBackendCapabilities.OverrideChannel) == 0) return false;
            if (channel == AnimationChannel.Overlay && (caps & AnimationBackendCapabilities.OverlayChannel) == 0) return false;
            return true;
        }

        private void Commit(AnimationChannel channel, ChannelSlot slot, AnimationHandle handle, in AnimationResolvedPlayback resolved)
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
            if (IsTerminal(handle)) return;                          // 已终态：不重复通知

            Remember(handle, id, channel, terminal);

            if (_slots.TryGetValue(channel, out var slot) && handle.Equals(slot.Current))
            {
                // 定义声明 HoldOnFinish 且自然完成 → **不停机不停用**（帧锁定）：非循环资产
                // 采样停在末帧，通道保持活跃；后续同通道提交仍走通道仲裁替换。其余终态照常收口。
                bool hold = terminal == AnimationTerminalState.Completed
                    && _profile.TryGetDefinition(id, out var def) && def.HoldOnFinish;
                if (!hold)
                {
                    if (!slot.Loading) _backend.TryStop(channel);     // 加载中无需停（从未提交）
                    slot.Current = default;
                    slot.CurrentId = default;
                    slot.Loading = false;
                    slot.Active = false;
                }
            }

            OnTerminal?.Invoke(handle, terminal);
        }

        private void Remember(AnimationHandle handle, AnimationId id, AnimationChannel channel, AnimationTerminalState terminal)
        {
            // 键＝句柄本体（结构化 Equals——不同句柄绝不互认）；句柄的 GetHashCode 只作桶内分桶， 碰撞由字典经 Equals 正确消解。
            if (_terminals.ContainsKey(handle)) return;

            while (_terminalOrder.Count >= TerminalRetentionCapacity)
            {
                AnimationHandle evicted = _terminalOrder.Dequeue();
                _terminals.Remove(evicted);
                EvictedTerminalRecords++;
            }

            _terminals[handle] = new TerminalRecord(id, channel, terminal);
            _terminalOrder.Enqueue(handle);
        }

        private bool IsTerminal(AnimationHandle handle) => _terminals.ContainsKey(handle);

        private ChannelSlot Slot(AnimationChannel channel)
        {
            if (!_slots.TryGetValue(channel, out var slot))
            {
                slot = new ChannelSlot();
                _slots[channel] = slot;
            }
            return slot;
        }

        /// <summary>句柄是否仍是某通道的当前播放。</summary>
        private bool TryFindCurrent(AnimationHandle handle, out AnimationChannel channel, out ChannelSlot slot)
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

        /// <summary>
        /// 单通道的播放槽（每通道最多一个待提交请求和一个当前逻辑播放）。
        /// </summary>
        private sealed class ChannelSlot
        {
            public AnimationHandle Current;          // 当前逻辑播放（已提交或提交中）
            public AnimationId CurrentId;
            public bool Loading;                     // 已接受但尚未提交成功
            public bool Active;

            /// <summary>替换当前播放：旧 Handle 得 Interrupted 终态（旧待提交 Handle 一并终止）。</summary>
            public bool HasCurrent => Active && Current.IsValid;
        }
    }
}
