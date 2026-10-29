using System.Collections.Generic;
using LiteSim;
using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 远端快照 jitter buffer 与渲染帧推进（《状态同步专项设计》§6.2）：
    /// 快照按帧号有序入缓冲，渲染帧按 tick 频率匀速前进并封顶"最新快照 − 渲染延迟"；
    /// 当前后两份快照生命更替或间距超阈值时硬切，避免把复活/传送播成穿墙滑行。
    /// </summary>
    internal sealed class SnapshotInterpolationBuffer
    {
        private const int Capacity = 8;

        private readonly List<SimWorldStateSnapshot> _snapshots = new List<SimWorldStateSnapshot>(Capacity);
        private float _renderFrame;
        private bool _hasRenderFrame;

        /// <summary>渲染相对最新快照的落后帧数（0 = 测试用即时模式）。</summary>
        public int RenderDelayFrames { get; set; } = SimConfig.InterpFrames;

        /// <summary>当前渲染帧（tick 帧轴；-1 = 尚无快照）。</summary>
        public int RenderFrame => _hasRenderFrame ? Mathf.FloorToInt(_renderFrame) : -1;

        public int Count => _snapshots.Count;

        public bool HasSource => _snapshots.Count > 0;

        public void OnSnapshot(SimWorldStateSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (_snapshots.Count > 0 && snapshot.Frame <= _snapshots[_snapshots.Count - 1].Frame) return;

            _snapshots.Add(snapshot);
            if (_snapshots.Count > Capacity) _snapshots.RemoveAt(0);
            if (!_hasRenderFrame)
            {
                _renderFrame = snapshot.Frame;   // 首个快照：先在当前帧落位，随后由缓冲延迟接管
                _hasRenderFrame = true;
            }
        }

        public void Reset()
        {
            _snapshots.Clear();
            _renderFrame = 0f;
            _hasRenderFrame = false;
        }

        /// <summary>渲染帧推进：按真实时间 × tick 频率匀速前进，封顶最新快照 − 渲染延迟；快照停摆时停住不外推。</summary>
        public void Advance(float dt)
        {
            if (!_hasRenderFrame || _snapshots.Count == 0) return;

            float newest = _snapshots[_snapshots.Count - 1].Frame;
            float oldest = _snapshots[0].Frame;
            float cap = newest - RenderDelayFrames;
            if (cap < oldest) cap = oldest;                 // 快照尚不足以撑满缓冲：先贴近现有最新

            float next = _renderFrame + dt * SimConfig.TickRate;
            if (next > cap) next = Mathf.Max(_renderFrame, cap);
            _renderFrame = next;

            // 只保留仍可能参与插值/最近历史的快照（至少保留一帧 <= 渲染帧 + 其后继）。
            while (_snapshots.Count > 2 && _snapshots[1].Frame <= _renderFrame)
                _snapshots.RemoveAt(0);
        }

        public bool TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)
        {
            snapshot = null;
            return TryGetRenderPair(out snapshot, out _, out _);
        }

        /// <summary>远端插值：渲染帧在两份快照之间按帧差插值；生命更替/传送级跳变直接对齐后帧。</summary>
        public bool TryInterpolate(long entityId, float snapDistance, out SimVector3 pos, out float yaw)
        {
            pos = default;
            yaw = 0f;
            if (!TryGetRenderPair(out SimWorldStateSnapshot from, out SimWorldStateSnapshot to, out float alpha))
                return false;

            bool hasFrom = TryFind(from, entityId, out var a);
            bool hasTo = TryFind(to, entityId, out var b);
            if (!hasFrom && !hasTo) return false;
            if (!hasFrom) { pos = b.Pos; yaw = b.Yaw; return true; }
            if (!hasTo) { pos = a.Pos; yaw = a.Yaw; return true; }

            if (a.LifeStartFrame != b.LifeStartFrame)
            {
                pos = b.Pos;
                yaw = b.Yaw;
                return true;
            }

            if (ViewTransformMath.ShouldSnap(a.Pos, b.Pos, snapDistance))
            {
                pos = b.Pos;
                yaw = b.Yaw;
                return true;
            }

            pos = ViewTransformMath.Lerp(a.Pos, b.Pos, alpha);
            yaw = ViewTransformMath.YawLerp(a.Yaw, b.Yaw, alpha);
            return true;
        }

        /// <summary>渲染帧时刻的权威位（按槽位，只读快照时间线）；换代/复活/跳变取后帧。</summary>
        public bool TryGetRenderPosition(SimWorldState sim, int slotIndex, float snapDistance, out SimVector3 position)
        {
            position = default;
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (!sim.IsAlive(slotIndex)) return false;
            if (!TryGetRenderPair(out SimWorldStateSnapshot from, out SimWorldStateSnapshot to, out float alpha))
                return false;

            long expectedId = sim.Entities[slotIndex].Id;
            bool inFrom = SlotAliveIn(from, slotIndex) && from.Entities[slotIndex].Id == expectedId;
            bool inTo = SlotAliveIn(to, slotIndex) && to.Entities[slotIndex].Id == expectedId;
            if (!inFrom && !inTo) return false;
            if (!inFrom) { position = to.Entities[slotIndex].Pos; return true; }
            if (!inTo) { position = from.Entities[slotIndex].Pos; return true; }

            EntitySnapshotEntry a = from.Entities[slotIndex];
            EntitySnapshotEntry b = to.Entities[slotIndex];
            if (a.Id != b.Id || a.LifeStartFrame != b.LifeStartFrame
                || ViewTransformMath.ShouldSnap(a.Pos, b.Pos, snapDistance))
            {
                position = b.Pos;
                return true;
            }

            position = ViewTransformMath.Lerp(a.Pos, b.Pos, alpha);
            return true;
        }

        private bool TryGetRenderPair(out SimWorldStateSnapshot from, out SimWorldStateSnapshot to, out float alpha)
        {
            from = null;
            to = null;
            alpha = 0f;
            if (_snapshots.Count == 0) return false;

            int fromIndex = -1;
            for (int i = 0; i < _snapshots.Count; i++)
            {
                if (_snapshots[i].Frame > _renderFrame) break;
                fromIndex = i;
            }
            if (fromIndex < 0)
            {
                from = _snapshots[0];
                to = from;
                return true;
            }

            from = _snapshots[fromIndex];
            to = fromIndex + 1 < _snapshots.Count ? _snapshots[fromIndex + 1] : from;
            if (to.Frame > from.Frame)
                alpha = Mathf.Clamp01((_renderFrame - from.Frame) / (to.Frame - from.Frame));
            return true;
        }

        private static bool SlotAliveIn(SimWorldStateSnapshot snapshot, int slotIndex)
            => (snapshot.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) != 0u;

        private static bool TryFind(SimWorldStateSnapshot snapshot, long entityId, out EntitySnapshotEntry entry)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((snapshot.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                if (snapshot.Entities[i].Id != entityId) continue;
                entry = snapshot.Entities[i];
                return true;
            }
            entry = default;
            return false;
        }
    }
}
