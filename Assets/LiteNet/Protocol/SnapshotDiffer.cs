using System;
using System.Collections.Generic;
using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 增量快照差分器（《M10实施指导》决策 7 + §2.7）。
    ///
    /// **两段式 API（2026-09-18 批③ 修正）**：一个房间的多个客户端共享一份差分基线，
    /// 但每个客户端的 AOI 视点不同 → 必须"**每广播帧算一次差分，然后按客户端各取可见部分**"。
    /// 早期实现让每个客户端各调一次 <c>Build</c>，而 <c>Build</c> 内部会推进基线 →
    /// 第二个客户端看到的永远是"刚被第一个客户端刷新的基线" → 恒发空增量（实测：两个客户端时
    /// 116/144 次广播退化为全量，客户端 2 收不到任何变化）。故拆成：
    /// - <see cref="BeginFrame"/>：每广播帧一次——算变化集 / 判定是否全量 / **推进基线**；
    /// - <see cref="BuildFor"/>：每客户端一次——纯读，把本帧变化集按该客户端可见性过滤。
    /// （<see cref="Build"/> 是两者的便捷组合，供单客户端/测试路径使用。）
    ///
    /// 基线语义 = "**最近一次广播出去的状态**"。三类情况必须走全量，否则客户端会静默分叉：
    /// ① 首帧 / 基线未建立；② **活体集合发生变化**（生成/死亡/槽位复用——增量只发变化槽位，
    /// 客户端无法从"缺席"区分"没变"与"死了"）；③ 客户端 ack 落后（<see cref="NeedsFull"/>）。
    ///
    /// 金标是**轻量摘要**不是全量 SimWorldState（见 <see cref="SimWorldStateSnapshot"/>）：
    /// 服务器已为回溯环养 16 份全量状态，广播基线不该再养一份。
    ///
    /// 字段覆盖与 <see cref="SimChecksum"/>（公共口径）对齐：Id/Pos/Vel/Yaw/Hp/Flags/Shield/Kills/Deaths/
    /// SelectedWeapon + **开火驻留窗**（批次D）+ 主动作摘要（P0 公共战斗面）；Globals/CustomData 由
    /// <see cref="GlobalsDiffer"/> 兜底
    /// （有变化即转全量），新增公共逻辑字段必须同步扩展摘要。
    /// 私有面（武器弹药/技能 CD/状态明细/局内包/资源）不走差分——随每份快照全量发本人（SnapshotCodec.PackPrivate）。
    /// </summary>
    public sealed class SnapshotDiffer : ISnapshotSource
    {
        private readonly SimWorldStateSnapshot _baseline = new SimWorldStateSnapshot();
        private readonly AoiFilter _aoi = new AoiFilter();          // 实例持有网格（多房间/多实例互不串味）
        private Proto.MatchStateDelta _match;                       // 本帧比赛状态（BeginFrame 打包一次，BuildFor 复用）

        /// <summary>AOI 网格外活体数（诊断/Ops）：>0 = 地图超出 `SimConfig.AoiGridExtentMeters`——
        /// 已按视点距离兜底不漏发，但应把覆盖半径调大（否则每帧多一圈距离判定）。</summary>
        public int AoiOutsideCount => _aoi.OutsideCount;
        private readonly List<int> _visible = new List<int>();
        private readonly List<int> _changed = new List<int>();      // 本帧"与基线不同"的**可见全图**槽位
        private int _lastBroadcastFrame = -1;
        private int _lastFullFrame = int.MinValue;
        private bool _baselineValid;
        private bool _frameFull;                                     // 本帧是否必须全量（BeginFrame 判定）
        private ulong _lastRng;                                      // 摘要外的全局量（"全局状态变了吗"的探针基准）
        private readonly byte[] _lastGlobals = new byte[SimConfig.GlobalsBytes];
        private readonly byte[] _lastCustomData = new byte[SimConfig.MaxEntities * SimConfig.CustomBytesPerEntity];

        /// <summary>差分统计（Ops）：上一份快照的槽位数 / 全量、增量次数。</summary>
        public int LastDeltaCount;
        public long FullCount;
        public long DeltaCount;

        public int LastBroadcastFrame => _lastBroadcastFrame;
        public bool BaselineValid => _baselineValid;
        public int FramesSinceFull => _lastFullFrame == int.MinValue ? int.MaxValue : _lastBroadcastFrame - _lastFullFrame;

        /// <summary>本帧是否被判为全量帧（<see cref="BeginFrame"/> 之后有效；Ops/诊断用）。</summary>
        public bool FrameIsFull => _frameFull;

        /// <summary>
        /// **每广播帧调一次**：算本帧变化集、判定全量、推进基线。
        /// <paramref name="forceFull"/> = 整帧强制全量（重连/兜底）。
        /// </summary>
        public void BeginFrame(int frame, SimWorldState state, bool forceFull = false)
        {
            bool aliveChanged = !_baselineValid || state.AliveCount() != _baseline.AliveCount;
            bool dueFull = _lastFullFrame != int.MinValue && frame - _lastFullFrame >= ProtocolConstants.FullEveryFrames;
            bool full = forceFull || aliveChanged || dueFull;
            // 全局状态探针**每条路径都跑**（基准必须每帧推进，否则首帧全量后基准停在初始值，次帧必误报）
            bool globalsDirty = GlobalsDiffer(in state);
            full = full || (!forceFull && !aliveChanged && !dueFull && globalsDirty);
            _frameFull = full;

            // 变化集：全图口径（AOI 在 BuildFor 里按客户端过滤——同一份差分服务所有客户端）
            _changed.Clear();
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!state.IsAlive(i)) continue;
                if (!full && _baseline.Matches(i, in state)) continue;   // 未变化槽位跳过（全量帧=全活体）
                _changed.Add(i);
            }

            if (full)
            {
                _baseline.CaptureFull(in state);          // 金标 = 本帧广播出去的完整状态
                _lastFullFrame = frame;
                FullCount++;
            }
            else
            {
                _baseline.Capture(in state);              // 帧头/位图；槽位逐个刷新（见下）
                for (int c = 0; c < _changed.Count; c++)
                {
                    int slot = _changed[c];
                    _baseline.Entities[slot] = SimWorldStateSnapshot.Capture(in state, slot);
                }
                DeltaCount++;
            }

            _baselineValid = true;
            _lastBroadcastFrame = frame;
            _match = SnapshotCodec.PackMatch(state);              // 比赛状态层：每广播帧打包一次（全房同值）
        }

        /// <summary>
        /// **每客户端调一次**：取该客户端可见的槽位（全量帧 = 全部可见活体；增量帧 = 可见 ∩ 变化集）+
        /// 比赛状态层 + **本人私有面**（<paramref name="viewerEntityId"/> ≠ 0 时附 PrivateState——只发本人）。
        /// 纯读——不推进任何状态（同帧多次调用结果一致）。
        /// </summary>
        public Proto.StateSnapshot BuildFor(int frame, SimWorldState state, int ackInput, SimVector3 viewPos, float aoiRadius, long viewerEntityId)
        {
            _visible.Clear();
            _aoi.CollectVisible(in state, viewPos, aoiRadius, _visible);

            var msg = new Proto.StateSnapshot
            {
                Frame = frame,
                IsFull = _frameFull,
                Checksum = SimChecksum.ComputePublicChecksum(state),   // 和解判定锚点（增量也带公共口径校验值）
                AckInput = ackInput,
                Match = _match,                                        // 比赛状态层（全体同值）
            };
            if (viewerEntityId != 0L)
                msg.PrivateState = SnapshotCodec.PackPrivate(state, viewerEntityId);   // 私有层：只发本人

            if (_frameFull)
            {
                for (int v = 0; v < _visible.Count; v++)
                    msg.Slots.Add(SnapshotCodec.ToDelta(_visible[v], state.Entities[_visible[v]],
                        state.Actions[_visible[v] * SimConfig.ActionSlotsPerEntity]));
            }
            else
            {
                // 变化集 ⊂ 全图活体；这里按可见性过滤（两个升序表求交，槽位序保持升序）
                for (int c = 0; c < _changed.Count; c++)
                {
                    int slot = _changed[c];
                    if (!ContainsSorted(_visible, slot)) continue;
                    msg.Slots.Add(SnapshotCodec.ToDelta(slot, state.Entities[slot],
                        state.Actions[slot * SimConfig.ActionSlotsPerEntity]));
                }
            }

            LastDeltaCount = msg.Slots.Count;
            return msg;
        }

        /// <summary>便捷组合（单客户端/测试）：BeginFrame + BuildFor（无私有面）。</summary>
        public Proto.StateSnapshot Build(int frame, SimWorldState state, int ackInput, SimVector3 viewPos, float aoiRadius, bool forceFull = false)
        {
            BeginFrame(frame, state, forceFull);
            return BuildFor(frame, state, ackInput, viewPos, aoiRadius, viewerEntityId: 0L);
        }

        public Proto.StateSnapshot Build(int frame, SimWorldState state, int ackInput)
            => Build(frame, state, ackInput, SimVector3.Zero, SimConfig.AoiRadius, forceFull: false);

        /// <summary>ack 落后触发全量（决策 7：客户端对不上了，给它一份完整状态重新对齐）。</summary>
        public bool NeedsFull(int clientAckSnapshot)
            => FramesSinceFull >= ProtocolConstants.FullEveryFrames
            || clientAckSnapshot < _lastBroadcastFrame - ProtocolConstants.FullResendAckLagFrames;

        private static bool ContainsSorted(List<int> sorted, int value)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] == value) return true;
                if (sorted[i] > value) return false;   // 升序表：越过即不存在
            }
            return false;
        }

        /// <summary>
        /// "摘要未覆盖的全局状态"探针：Globals/CustomData（及未来新增的逻辑字段）不在 <see cref="EntitySnapshotEntry"/> 里，
        /// 差分器发不出去；变了而不转全量 → 客户端**静默分叉**（最难查的一类）。
        /// 基准每帧推进（含全量帧——否则首帧全量后基准停在初始值，次帧必误报）。
        /// M8 的 SimStep 里 Globals/CustomData 恒为零，实际只有 RngState（开火伤害浮动）会动。
        /// <b>纪律：任何进 checksum 的新状态字段都必须同时进 <see cref="EntitySnapshotEntry"/>。</b>
        /// </summary>
        private bool GlobalsDiffer(in SimWorldState state)
        {
            bool changed = _baselineValid
                && (state.RngState != _lastRng
                    || !SameBytes(state.Globals, _lastGlobals)
                    || !SameBytes(state.CustomData, _lastCustomData));

            _lastRng = state.RngState;
            state.Globals.CopyTo(_lastGlobals, 0);
            state.CustomData.CopyTo(_lastCustomData, 0);
            return changed;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;   // 逐字节比对（长度固定，见 SimConfig.GlobalsBytes/CustomBytesPerEntity）
            return true;
        }
    }
}
