using System;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// <see cref="IInputService"/> 的实现（《角色状态与动作专项设计》§3"输入三件"的协调者）。
    ///
    /// 三个门的落地（与 <see cref="IInputService"/> 的接口文档一一对应，本类只写实现细节）：
    ///
    /// **① 上下文门**——<see cref="IntentGate"/> 上登记的拦截源每渲染帧裁决一次。拦下时**直接把本帧
    /// 采样作废**（不写待用意图），而不是采一份全零覆盖：设备状态与意图之间因此保持"最后一次放行时
    /// 的样子"，UI 关掉后的第一帧沿用的是拦截前的意图，不会凭空出现一帧零输入。语义上，被拦下的帧
    /// 等价于这段真实时间里没有新输入——逻辑帧消费门照常取到同一份输入（追帧/多逻辑帧不产生额外输入）。
    ///
    /// **② 上行**——<see cref="TryTakeForSend"/> 只在"本渲染帧成功采样过"时返回 true（每渲染帧最多一次），
    /// 与 <see cref="TryTakeForPrediction"/> 共享同一份 <see cref="_pending"/>：同一帧里预测消费与
    /// 上行发出的**是同一份输入**（《状态同步专项设计》§5.1 的前提——两端同帧同值）。冗余重发由
    /// <c>RoomClient</c> 的最近帧窗口负责，本类不重复发。
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
        private bool _sampledThisRenderFrame;          // 本渲染帧采到了新输入（上行的唯一开闸条件）
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

        public bool RegisterBlocker(IntentGate.BlockerKey key, Func<bool> isBlocking)
        {
            bool added = _gate.Remove(key.Name) == false;   // 同名 = 覆盖（产品装配点的幂等语义）
            _gate.Register(key, isBlocking);
            return added;
        }

        public bool UnregisterBlocker(string name) => _gate.Remove(name);

        public void SampleOnRenderFrame(SimVector3 localOrigin)
        {
            // 每渲染帧只裁决一次。**开闸点必须唯一**：只在这里清标记，而置位只发生在真正采到新输入时
            // （下面）。于是"本帧有没有新输入"这件事恰好被消费一次——拦截帧、以及"被拦下期间本帧
            // 又被调用"的情况，都不会把上一次的意图当成新输入重发。
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
                return;                                  // 待用意图保持：本帧没有新输入（不是"零输入"）
            }
            BlockedByName = null;
            BlockedReason = null;

            // "本帧产生了一份新输入"的唯一置位点：只在真正采到新输入时置位，
            // 因此 TryTakeForSend 不会把上一次的意图当成新输入重发，也不会在断线恢复后爆发重传。
            _sampledThisRenderFrame = true;

            if (_source == null)
            {
                SampleWithNoSource++;
                _pending = default;                      // 无设备 = 空意图（触屏源未接线时的合法形态）
                _hasPending = true;
                return;
            }

            _pending = _source.Sample(localOrigin);

            // 设备返回值的契约兜底（移动/瞄准长度 ≤ 1 归设备源自己保证，这里不重复做）：
            // 只挡 NaN/Infinity —— 非法值会经 Step 污染整个 Sim；服务器 InputGate 亦会拒，
            // 但本地预测不该先脏。清零 = 该分量无输入。
            Sanitize(ref _pending);

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
            if (!IsFinite(input.AimX)) input.AimX = 0f;
            if (!IsFinite(input.AimZ)) input.AimZ = 0f;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
