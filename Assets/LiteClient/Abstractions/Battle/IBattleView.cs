using LiteSim;

namespace LiteClient
{
    /// <summary>
    /// 表现视图端口（路线图 M0 接缝 1）：对局上下文只持本端口，不认识具体视图实现
    /// （通用表现层 <c>LiteView.SimView</c> 实现它；纯会话/测试形态可注入替身）。
    ///
    /// **为什么在 Abstractions**：<c>LiteClient.Runtime</c> 不得引用 <c>LiteView</c>（单向依赖不变量 I3），
    /// 端口必须住双方都允许引用的契约程序集；<c>SimWorldState</c>/<c>SimWorldStateSnapshot</c> 来自
    /// <c>LiteSim.Core</c>（两端共编的纯逻辑件）。
    ///
    /// 成员语义与 <c>SimView</c> 同名方法一致（本端口只是类型边界，不改行为）：
    /// 回滚/和解 → 视图静默；帧事件在逻辑帧边界交付；权威快照推入视图插值缓冲。
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
    }
}
