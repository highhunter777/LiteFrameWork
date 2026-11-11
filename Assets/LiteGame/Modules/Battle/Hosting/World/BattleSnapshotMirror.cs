using LiteNet;
using LiteSync.Snapshot;
using LiteSim;
using LiteFramework;
using LiteGame;
using LiteFramework;
using LiteGame.Modules.Battle.Abstractions.View;

namespace LiteGame.Modules.Battle.Hosting.World
{
    /// <summary>
    /// 权威快照镜像（从对局上下文外提）：**持久镜像重建** + 和解判定 + 视图插值源推进。
    /// </summary>
    internal sealed class BattleSnapshotMirror
    {
        private SimWorldState _state;                                              // 持久权威镜像（懒建）
        // 视图插值源轮转：容量必须 > SimView 的 jitter buffer（8）——只在两份之间轮转会覆盖
        // 视图仍持有的旧快照（其对象被新帧覆写后，帧号序被打乱，插值直接跨帧）。
        private readonly SimWorldStateSnapshot[] _viewSnapshots = new SimWorldStateSnapshot[16];
        private int _viewSnapWrite;                                                // 下一次写入下标（环形）
        // 增量和解材料缓冲（复用，零分配）：携带槽位 + 逐槽 wire 校验值。
        private readonly int[] _slotBuf = new int[SimConfig.MaxEntities];
        private readonly uint[] _slotChecksumBuf = new uint[SimConfig.MaxEntities];

        /// <summary>当前镜像（未被任何包应用过时为 null）。</summary>
        public SimWorldState State => _state;

        /// <summary>
        /// 应用一份状态快照：镜像重建 → 和解（checksum 比对/覆盖/重放）。
        /// </summary>
        public bool ApplyAndReconcile(RollbackSim sim, LiteNet.Proto.StateSnapshot snapshot)
        {
            _state = _state ?? new SimWorldState();
            SnapshotReassembler.Apply(snapshot, _state, out uint checksum);
            if (Reconcile(sim, snapshot, checksum)) return true;
            SnapshotReassembler.OverlayPrivateAndMatch(snapshot, sim.State);
            return false;
        }

        /// <summary>和解分发（P2-A）：全量帧走全图口径（legacy 重载）；增量帧走**携带槽位集**部分和解
        /// （<see cref="AuthoritativeFrameInfo"/>——视野外槽位不触发/不误回退）。</summary>
        private bool Reconcile(RollbackSim sim, LiteNet.Proto.StateSnapshot snapshot, uint checksum)
        {
            if (snapshot.IsFull)
                return sim.OnAuthoritativeSnapshot(snapshot.Frame, _state, checksum);

            int count = SnapshotReassembler.Collect(snapshot, _slotBuf, _slotChecksumBuf);
            var info = new AuthoritativeFrameInfo(isFull: false, fullChecksum: 0u,
                visibleChecksum: snapshot.VisibleChecksum, matchChecksum: snapshot.MatchChecksum,
                slotCount: count, slots: _slotBuf, slotChecksums: _slotChecksumBuf);
            return sim.OnAuthoritativeSnapshot(snapshot.Frame, _state, in info);
        }

        /// <summary>重连恢复（§9.3 步骤 3~5）：权威全量重建镜像 → 和解到该帧。</summary>
        public void ApplyReconnect(RollbackSim sim, LiteNet.Proto.StateSnapshot snapshot)
        {
            _state = _state ?? new SimWorldState();
            SnapshotReassembler.Apply(snapshot, _state, out uint checksum);
            sim?.OnAuthoritativeSnapshot(snapshot.Frame, _state, checksum);
        }

        /// <summary>
        /// 把当前镜像拷进视图插值源（两块轮转）并推给视图。
        /// </summary>
        public void PushToView(IBattleView view)
        {
            if (view == null || _state == null) return;

            var snap = _viewSnapshots[_viewSnapWrite];
            if (snap == null)
            {
                snap = new SimWorldStateSnapshot();
                _viewSnapshots[_viewSnapWrite] = snap;
            }
            snap.CaptureFull(_state);
            _viewSnapWrite = (_viewSnapWrite + 1) % _viewSnapshots.Length;   // 环形：保留视图仍在用的旧份

            view.OnAuthoritativeSnapshot(snap);
        }

        /// <summary>丢弃镜像（离场——不留下局可读的上一局状态）。</summary>
        public void Reset() => _state = null;
    }
}
