using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 游戏输入服务（《角色状态与动作专项设计》§3"输入三件"的框架落点；《联机战斗演示专项设计》
    /// §5 联调顺序"输入和三个门"）：把"设备 → 意图 → 送到 Sim"这条链的**所有权与裁决**收进一个
    /// 实体服务，取代原先散在流程里的裸委托与采集类分居两处（`ProcedureBattle` 持
    /// <c>Func&lt;bool&gt;</c> 做上下文门、`LiteSim.View` 持采集类自己做门，拦截源无处登记、
    /// 谁拦的说不清、逻辑帧消费纪律靠调用点自觉）。
    ///
    /// <b>三个门与服务的关系</b>（设计逐条落地，职责不重叠；实现见 <see cref="InputService"/>）：
    /// <list type="number">
    /// <item><b>上下文门</b>——<see cref="RegisterBlocker"/> 登记的拦截源（UI 模态栈、暂停、失焦、
    ///   重连提示）：任一成立，本帧的采样结论**作废**（<see cref="SampleDiposedByGate"/> 计数），
    ///   待用意图写<b>全零</b>并照常置"已采样"（《角色状态与动作专项设计》§3 第 2 件
    ///   "UI/菜单打开时输出零战斗意图"的直接落地：UI 打开时角色不动）。
    ///   **为什么是"零"而不是"保持最后一次放行值"**（2026-10-02 修复，推翻 2026-09-26 口径）：
    ///   服务器对缺席帧的唯一读法是空输入兜底——本地沿用旧值推进、上行又静默，移动中弹模态即
    ///   "本地在动、权威已停"，下一份快照必和解回拉；零值让本地预测/上行/权威兜底三处逐位同读
    ///   "这一帧没有战斗输入"。照发还让 ackSnapshot 随包流动（静默超时会让服务器把整帧持续
    ///   强制成全量快照）。
    ///   UI 侧结论由装配根**经委托注入**，本服务不认识 UI 运行时（《客户端总设计》§5.1 逻辑边界）。</item>
    /// <item><b>采样与上行</b>——每渲染帧最多采 1 次（<see cref="SampleOnRenderFrame"/>），
    ///   <see cref="TryTakeForSend"/> 每渲染帧恰报一次：采到发采到的，没采到发全零；
    ///   同一份输入既进预测又上行（两端同帧同值）。</item>
    /// <item><b>帧边界门</b>——<see cref="TryTakeForPrediction"/> 每个逻辑帧消费一次，同一帧重复
    ///   取用返回 false（追帧不产生额外输入）。</item>
    /// </list>
    ///
    /// <b>数值面（本批落地范围）</b>：Move/Aim/Fire 三个连续意图。离散意图位
    /// （Reload/Switch/Skill/Pickup/UseItem）需要按键沿检测与逐玩家单调递增的 <c>action_seq</c>
    /// 发生器（陈旧/重放的 seq 会被服务器 InputGate 按前向单调拒收），未有消费者（武器/Action 系统
    /// 归 G2），**未实现**——设备源不产生这些位。
    ///
    /// 生命周期：登记在 Root Scope（跨对局复用），对局按 <see cref="Reset"/> 清派发状态（防上一局的
    /// 待用意图漏进下一局），关服按 <see cref="ResetAll"/> 全清。
    /// </summary>
    public interface IInputService
    {
        /// <summary>当前设备源；null = 无设备（移动端触屏源未接线前的合法形态——意图恒空但链路完整）。</summary>
        IIntentSource Source { get; }

        /// <summary>最近一次裁决是否被拦下（诊断/HUD/DebugTuner 用）。</summary>
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

        /// <summary>登记/替换一个拦截源，返回是否**新增**（false = 覆盖了既有同名源——
        /// UI 重连、流程重进时用同一名字更新是常态，重名不抛错；需要严格判重请直用
        /// <see cref="IntentGate.Register"/> 的抛错语义）。</summary>
        bool RegisterBlocker(IntentGate.BlockerKey key, System.Func<bool> isBlocking);

        /// <summary>注销拦截源（按名；返回是否找到）。</summary>
        bool UnregisterBlocker(string name);

        /// <summary>
        /// 渲染帧采样：先裁决拦截源（拦下即作废本帧结论并计数）→ 无设备源记空 → 采样并挡非有限值。
        /// 每渲染帧只裁决一次（重复调用直接返回，防调用方把它放进多趟驱动）。
        /// <b>必须带上下文</b>（本地玩家**预测**位置）——设备源据此做瞄准换算，不接受设备侧自己
        /// 缓存一份位置（否则会产生与裁决不同帧的陈旧原点）。
        /// </summary>
        void SampleOnRenderFrame(SimVector3 localOrigin);

        /// <summary>
        /// 逻辑帧消费门：取本逻辑帧的输入。同一 frame 重复调用返回 false（追帧不产生额外输入）。
        /// 返回 false 时 <paramref name="input"/> 为 default——调用方按"无输入"处理。
        /// </summary>
        bool TryTakeForPrediction(int frame, out SimInputFrame input);

        /// <summary>
        /// 上行取值：本渲染帧的输入结论，每渲染帧恰报一次（采到发采到的，没采到发全零——
        /// 服务器对缺席帧按空输入兜底执行，零值与其逐位同值，两端同帧同值的前提）。
        /// 返回的就是当帧送进预测的那一份——冗余重发由 <c>RoomClient</c> 的最近帧窗口承担，本服务不重复发。
        /// </summary>
        bool TryTakeForSend(out SimInputFrame input);

        /// <summary>清空待用意图与派发状态（流程离场/重连重进时调用；拦截源与设备源保持登记）。</summary>
        void Reset();

        /// <summary>全部清空（关服/测试复位：拦截源、设备源、意图、计数器）。</summary>
        void ResetAll();
    }
}
