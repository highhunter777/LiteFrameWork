using LiteSim;

namespace LiteGame.Modules.Input
{
    /// <summary>
    /// 输入服务：设备 → 意图 → Sim 的唯一裁决点（上下文门 / 采样上行 / 帧边界门，实现见 <see cref="InputService"/>）。
    /// 离散意图位（换弹/切枪/技能等）未实现；设计依据见《角色状态与动作专项设计》§3。
    /// </summary>
    public interface IInputService
    {
        /// <summary>当前设备源；null = 无设备（移动端触屏源未接线前的合法形态——意图恒空但链路完整）。</summary>
        IIntentSource Source { get; }

        /// <summary>最近一次裁决是否被拦下（诊断/HUD/调试面用）。</summary>
        bool IsBlocked { get; }

        /// <summary>最近一次拦下本意图的源名；未拦下 = null（"谁关的输入门"的唯一答案）。</summary>
        string BlockedByName { get; }

        /// <summary>拦下理由（产品策略说明；诊断用）。</summary>
        string BlockedReason { get; }

        /// <summary>当前登记源数量（同名覆盖不累加）。</summary>
        int BlockerCount { get; }

        /// <summary>被上下文门拦下的采样次数（诊断：UI 拦截时长占比）。</summary>
        long SampleDiposedByGate { get; }

        /// <summary>无设备源、或设备未返回采样（Handled=false）导致空意图的采样次数（诊断：触屏源未接线/设备未就绪时可见）。</summary>
        long SampleWithNoSource { get; }

        /// <summary>当前待用意图（本帧的输入结论：采到 = 设备读数；被拦/未采到 = 全零）。**不含 EntityId**——由会话补齐。</summary>
        SimInputFrame Pending { get; }

        /// <summary>设置设备源（null = 清空设备）。切换设备不重置帧边界状态。</summary>
        void SetSource(IIntentSource source);

        /// <summary>
        /// 逐渲染帧注入预测世界与本地槽位（瞄准求交用，流程层在采样前调用）；只读。
        /// 设备源未实现 <see cref="IAimWorldSink"/> 时为空操作（瞄准维持二维口径）。
        /// </summary>
        void SetAimWorld(SimWorldState world, int localSlot);

        /// <summary>登记/替换一个拦截源，返回是否**新增**（false = 覆盖了既有同名源——
        /// UI 重连、流程重进时用同一名字更新是常态，重名不抛错；需要严格判重请直用
        /// <see cref="IntentGate.Register"/> 的抛错语义）。</summary>
        bool RegisterBlocker(IntentGate.BlockerKey key, System.Func<bool> isBlocking);

        /// <summary>注销拦截源（按名；返回是否找到）。</summary>
        bool UnregisterBlocker(string name);

        /// <summary>
        /// 渲染帧采样：先裁决拦截源（拦下即作废并计数）→ 无源记空 → 采样并挡非有限值；每帧只裁决一次。
        /// localOrigin 必须是本帧预测位置——设备源不得自缓存（会与裁决不同帧）。
        /// </summary>
        void SampleOnRenderFrame(SimVector3 localOrigin);

        /// <summary>
        /// 逻辑帧消费门：取本逻辑帧的输入。同一 frame 重复调用返回 false（追帧不产生额外输入）。
        /// 返回 false 时 <paramref name="input"/> 为 default——调用方按"无输入"处理。
        /// </summary>
        bool TryTakeForPrediction(int frame, out SimInputFrame input);

        /// <summary>
        /// 上行取值：本渲染帧的输入结论，每帧恰报一次；未采到报全零（与服务器缺席帧兜底同值）。
        /// 返回的就是当帧进预测的那一份；冗余重发归 RoomClient 的最近帧窗口。
        /// </summary>
        bool TryTakeForSend(out SimInputFrame input);

        /// <summary>清空待用意图与派发状态（流程离场/重连重进时调用；拦截源与设备源保持登记）。</summary>
        void Reset();

        /// <summary>全部清空（关服/测试复位：拦截源、设备源、意图、计数器）。</summary>
        void ResetAll();
    }
}
