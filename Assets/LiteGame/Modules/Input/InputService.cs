using System;
using LiteSim;

namespace LiteGame.Modules.Input
{
    /// <summary>
    /// IInputService 的实现（《角色状态与动作专项设计》§3"输入三件"的协调者）。
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
        /// 逐帧注入预测世界与本地槽位（IInputService.SetAimWorld）。
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
            // 同渲染帧内重入（追帧/多 procedure 步进）：**沿用本帧已裁决结论**——不重采样、也不清标记。
            // 清标记的唯一时机是 TryTakeForSend（上行消费本帧结论）；此处清标记会吞掉本帧上行输入。
            if (_sampledThisRenderFrame) return;

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
                // 未采到与被拦**同读法**：空意图 + 照常置位（沿用旧值会与权威空输入分叉，理由同上）。
                _pending = default;
                _sampledThisRenderFrame = true;
            }

            // 设备返回值的契约兜底（移动/瞄准长度 ≤ 1 归设备源自己保证，这里不重复做）： 只挡 NaN/Infinity —— 非法值会经 Step 污染整个 Sim。
            Sanitize(ref _pending);

            // **离散意图 action_seq 产生**（《游戏业务系统总设计》§3.2：离散位必须携带逐玩家单调递增的 seq——服务器按 (playerId, seq) 去重防重放）。
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
            _sampledThisRenderFrame = false;               // 上行消费本帧结论 = 唯一清标记点（下一帧采样才重新开闸）

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
