using System;
using LiteSim;

namespace LiteClient
{
    /// <summary>
    /// <see cref="IInputService"/> 的实现（《角色状态与动作专项设计》§3"输入三件"的协调者）。
    ///
    /// 三个门的落地（与 <see cref="IInputService"/> 的接口文档一一对应，本类只写实现细节）：
    ///
    /// **① 上下文门**——<see cref="IntentGate"/> 上登记的拦截源每渲染帧裁决一次。拦下时本帧采样作废、
    /// 待用意图写<b>全零</b>并照常置"已采样"：被拦下的帧等价于"这一帧没有战斗输入"，本地与服务器
    /// **同读**——本地零意图预测、上行零意图、服务器缺席兜底（<c>RoomRuntime</c> 缺席沿用 default）
    /// 也是零，三处逐位一致（《角色状态与动作专项设计》§3 第 2 件"UI/菜单打开时输出零战斗意图"、
    /// §6 验收"UI 打开时角色不动"）。解拦后的首个采样帧即恢复实时按键（采样先于预测，见
    /// <c>ProcedureBattle.OnUpdate</c> 第①步），不会凭空多出零帧。
    ///
    /// **② 上行**——每渲染帧一份**结论**：采到发采到的，没采到（被拦/设备未返回）发全零；
    /// <see cref="TryTakeForSend"/> 与 <see cref="TryTakeForPrediction"/> 共享同一份
    /// <see cref="_pending"/>：同一帧里预测消费与上行发出的**是同一份输入**（《状态同步专项设计》§5.1
    /// 的前提——两端同帧同值）。拦下期间照发还有第二层意义：ackSnapshot 随输入包上行，静默超
    /// <c>FullResendAckLagFrames</c> 会让服务器 NeedsFull 判据把**整帧**持续强制成全量（全房陪付
    /// 带宽）。冗余重发由 <c>RoomClient</c> 的最近帧窗口负责，本类不重复发。
    ///
    /// **③ 帧边界门**——<see cref="TryTakeForPrediction"/> 记 <see cref="_lastPredictedFrame"/>，
    /// 同一 frame 第二次调用返回 false；采样未发生过的帧（无待用）也返回 false，杜绝"拿上一次的
    /// 意图当这一帧的输入"这类追帧重复消费。
    ///
    /// **拦截源登记**：同名覆盖不抛（UI 重连/流程重进时同名更新是常态）；重名抛只发生在
    /// <see cref="IntentGate.Register"/> 直调路径——本类走覆盖语义，因为调用方是产品装配点，
    /// 抛错会把"同一理由重新登记"变成崩溃而不是幂等。
    /// </summary>
    public sealed class InputService : IInputService
    {
        private readonly IntentGate _gate = new IntentGate();

        private IIntentSource _source;
        private bool _hasPending;
        private bool _sampledThisRenderFrame;          // 本渲染帧已有确定的输入结论（采到/被拦零/未采到零——上行据此开闸）
        private SimInputFrame _pending;
        private int _lastPredictedFrame = -1;

        public IIntentSource Source => _source;

        public bool IsBlocked => _gate.IsBlocked;

        public string BlockedByName { get; private set; }

        public string BlockedReason { get; private set; }

        public int BlockerCount => _gate.Count;

        public long SampleDiposedByGate { get; private set; }

        public long SampleWithNoSource { get; private set; }

        public SimInputFrame Pending => _pending;

        public void SetSource(IIntentSource source) => _source = source;

        /// <summary>
        /// 逐帧注入预测世界与本地槽位（<see cref="IInputService.SetAimWorld"/>）。
        /// **只做能力转发**：设备源未实现 <see cref="IAimWorldSink"/>（触屏/手柄等不需要瞄准求交的源）
        /// 时是安全空操作——核心服务不因此认识世界类型，也不需要每个源都实现这个能力。
        /// </summary>
        public void SetAimWorld(SimWorldState world, int localSlot)
        {
            if (_source is IAimWorldSink sink) sink.SetAimWorld(world, localSlot);
        }

        public bool RegisterBlocker(IntentGate.BlockerKey key, Func<bool> isBlocking)
        {
            bool added = _gate.Remove(key.Name) == false;   // 同名 = 覆盖（产品装配点的幂等语义）
            _gate.Register(key, isBlocking);
            return added;
        }

        public bool UnregisterBlocker(string name) => _gate.Remove(name);

        public void SampleOnRenderFrame(SimVector3 localOrigin)
        {
            // 每渲染帧只裁决一次。**开闸点必须唯一**：只在这里清标记，而置位只发生在本帧有确定结论时
            // （下面三条路径：被拦零、无设备零、真采样——结论无论是谁，都恰好被消费一次）。
            bool alreadyHandled = _sampledThisRenderFrame;
            _sampledThisRenderFrame = false;

            if (alreadyHandled) return;

            _gate.Evaluate();
            if (_gate.IsBlocked)
            {
                var blocked = _gate.BlockedBy;
                BlockedByName = blocked.HasValue ? blocked.Value.Name : null;
                BlockedReason = blocked.HasValue ? blocked.Value.Reason : null;
                SampleDiposedByGate++;
                // 拦下 = 本帧没有战斗输入：写空意图并照常置位（理由见类注释①——
                // 与服务器空输入兜底逐位同值，预测/上行/权威三处同读"没有输入"）。
                _pending = default;
                _hasPending = true;
                _sampledThisRenderFrame = true;
                return;
            }
            BlockedByName = null;
            BlockedReason = null;

            if (_source == null)
            {
                SampleWithNoSource++;
                _pending = default;                      // 无设备 = 空意图（触屏源未接线时的合法形态）
                _hasPending = true;
                _sampledThisRenderFrame = true;
                return;
            }

            IntentSample sample = _source.Sample(localOrigin);
            if (sample.Handled)
            {
                _pending = sample.Frame;                 // 采到了（可能是空意图——用户真的什么都没按）
                _sampledThisRenderFrame = true;          // "本帧有确定结论"的置位点之一
            }
            else
            {
                SampleWithNoSource++;
                // 未采到与被拦**同读法**：空意图 + 照常置位（沿用旧值会与权威空输入分叉，理由同上）
                _pending = default;
                _sampledThisRenderFrame = true;
            }

            // 设备返回值的契约兜底（移动/瞄准长度 ≤ 1 归设备源自己保证，这里不重复做）：
            // 只挡 NaN/Infinity —— 非法值会经 Step 污染整个 Sim；服务器 InputGate 亦会拒，
            // 但本地预测不该先脏。清零 = 该分量无输入。
            Sanitize(ref _pending);

            // **离散意图 action_seq 产生**（《游戏业务系统总设计》§3.2：离散位必须携带逐玩家单调递增的
            // seq——服务器按 (playerId, seq) 去重防重放）。产生点选在帧边界门内（本帧采样的唯一收口）：
            // 只在含离散位的帧递增并写入；同一意图在追帧中被沿用帧复用不构成重放（沿用帧剥离离散位，
            // RollbackSim 侧）；**不随 Reset 归零**（连接内单调——归零会被服务器判为回退 seq 拒收）。
            if ((_pending.Buttons & SimInputFrame.DiscreteIntentButtons) != 0u)
                _pending.ActionSeq = ++_actionSeq;

            _hasPending = true;
        }

        public bool TryTakeForPrediction(int frame, out SimInputFrame input)
        {
            input = default;
            if (!_hasPending) return false;
            if (frame == _lastPredictedFrame) return false;   // 帧边界门：同一逻辑帧只消费一次

            _lastPredictedFrame = frame;
            input = _pending;
            return true;
        }

        public bool TryTakeForSend(out SimInputFrame input)
        {
            input = default;
            bool fresh = _sampledThisRenderFrame;
            _sampledThisRenderFrame = false;               // 无论是否连得上，渲染帧边界一律清

            if (!fresh) return false;
            input = _pending;
            return true;
        }

        public void Reset()
        {
            _pending = default;
            _hasPending = false;
            _sampledThisRenderFrame = false;
            _lastPredictedFrame = -1;
        }

        /// <summary>离散意图序号（会话内单调；见采样处的产生口径）。</summary>
        private uint _actionSeq;

        public void ResetAll()
        {
            Reset();
            _gate.Clear();
            _source = null;
            BlockedByName = null;
            BlockedReason = null;
            SampleDiposedByGate = 0;
            SampleWithNoSource = 0;
        }

        /// <summary>非有限分量清零（NaN/Inf 会经 Step 污染整个 Sim；清零 = 该分量无输入）。</summary>
        private static void Sanitize(ref SimInputFrame input)
        {
            if (!IsFinite(input.MoveX)) input.MoveX = 0f;
            if (!IsFinite(input.MoveZ)) input.MoveZ = 0f;
            // AimPoint（单口径——《固定斜视角射击方案专项设计》§3）：非有限一律清零——清零即"无点"：
            // 不派生朝向（保持上帧）、不产命中（Sim 侧只写 Fire 事件）。
            if (!IsFinite(input.AimPointX)) input.AimPointX = 0f;
            if (!IsFinite(input.AimPointY)) input.AimPointY = 0f;
            if (!IsFinite(input.AimPointZ)) input.AimPointZ = 0f;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
