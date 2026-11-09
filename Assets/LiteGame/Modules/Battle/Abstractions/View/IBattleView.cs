using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 表现视图端口（路线图 M0 接缝 1）：对局上下文只持本端口，不认识具体视图实现 （战斗表现 SimView 实现它）。
    /// </summary>
    public interface IBattleView
    {
        /// <summary>当前渲染帧（tick 帧轴；-1 = 尚无快照）。</summary>
        int RenderFrame { get; }

        /// <summary>视图逐帧推进（插值、实体视图、表现驱动）。</summary>
        void Tick(float realDelta);

        /// <summary>丢弃插值状态（重生/传送/重连后不跨段插值）。</summary>
        void ResetInterpolation();

        /// <summary>回滚重放（重放段事件一律静默）。</summary>
        void OnRollback(int replayedToFrame);

        /// <summary>和解发生（本地表现交给衰减收敛）。</summary>
        void OnReconcile(int frame);

        /// <summary>帧事件交付（逻辑帧边界；事件在 <paramref name="state"/> 的事件缓冲内）。</summary>
        void OnFrameEvents(SimWorldState state);

        /// <summary>本地实体对齐（首份快照确定本地槽位后调用）。</summary>
        void AlignLocal(long entityId);

        /// <summary>推入权威快照（视图插值源轮转；完整捕获而非增量）。</summary>
        void OnAuthoritativeSnapshot(SimWorldStateSnapshot snapshot);

        /// <summary>写入房级快照频率（Hz；JoinAck 权威值，插值窗口随之派生）。</summary>
        void SetSnapshotHz(int hz);
    }
}
