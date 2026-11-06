using LiteSim;
using LiteFramework;
using LiteGame;
using LiteFramework;
using LiteGame.Modules.Input;

using LiteSync.Client;
namespace LiteGame
{
    /// <summary>
    /// 本地输入上行管线（《角色状态与动作专项设计》§3"输入三件"的落点，从对局上下文外提）： ① 本帧采样入史（RollbackSim.OnRealInput）——第 F 步用真实输入而非沿用。
    /// </summary>
    internal sealed class BattleInputPipeline
    {
        private readonly IBattleClient _battle;
        private readonly SimInputFrame[] _localInputs;   // 送进预测/上行的那一份（复用，零分配）

        /// <summary>
        /// 构造。<paramref name="expectedPlayers"/> = 房间规模（Sim 只接受"全体玩家一帧"的数组，
        /// SimStep 按 EntityId 就地排序；缺席槽位 = 空输入，EntityId=0 解析不到实体 → InputSystem 自然丢弃）。
        /// </summary>
        public BattleInputPipeline(IBattleClient battle, IInputService input, int expectedPlayers)
        {
            _battle = battle;
            Input = input;
            _localInputs = new SimInputFrame[expectedPlayers];
        }

        /// <summary>本地输入源（<see cref="IInputService"/>；null = 空输入——纯会话/测试形态）。</summary>
        public IInputService Input { get; set; }

        /// <summary>
        /// 输入三件（采样入史 → 同一份上行 → 沿用帧预发）。**必须在本地服步进之前**调用。
        /// </summary>
        public void FeedAndSendInputs(RollbackSim sim, long localEntityId, int inputFrame, float realDelta,
            int viewFrame)
        {
            IInputService input = Input;
            if (input == null) return;

            SimInputFrame local;
            if (input.TryTakeForPrediction(inputFrame, out SimInputFrame taken)) local = taken;   // 帧边界门在本帧的输入
            else local = input.Pending;                                                          // 本帧已消费过：沿用同一份（追帧不产生额外输入）
            local.EntityId = localEntityId;                                                      // 服务器 InputGate 覆写防伪；本地预测按 slot 实体对齐

            FillLocalInputs(local);
            sim.OnRealInput(inputFrame, _localInputs);   // 真实输入入史（≈ RTT/2 后到达服务器，其间本地按另一条路径预测）

            if (!_battle.Connected || !input.TryTakeForSend(out _)) return;

            // viewFrame = **视图实际渲染帧**（SimView.RenderFrame，jitter buffer 的帧轴）——
            // 不是"最新快照 + 插值帧"（后者在服务端再减 InterpFrames 会互相抵消，回溯到玩家没看到的帧）。
            _battle.Client.SendInput(inputFrame, local, viewFrame: viewFrame);   // 发**同一份**（含 EntityId 对齐）

            int steps = sim.PeekSteps(realDelta);
            if (steps < 2) return;                           // 单步/零步：没有额外帧

            SimInputFrame carried = local;
            carried.Buttons &= SimInputFrame.PredictedButtons;   // 沿用形（与 PrepareNext 同口径，掩码幂等）
            for (int f = inputFrame + 1; f < inputFrame + steps; f++)
                _battle.Client.SendInput(f, carried, viewFrame: viewFrame);
        }

        /// <summary>
        /// 追帧补发：本渲染帧跑了 ≥2 个逻辑帧时，多出的帧本地以"沿用上一帧"推进 （RollbackSim.PrepareNext）——这些帧**同样必须上行**。
        /// </summary>
        public void ResendCarriedFrames(RollbackSim sim, long localEntityId, int inputFrame, int viewFrame)
        {
            if (Input == null || !_battle.Connected || localEntityId == 0) return;

            for (int f = inputFrame + 1; f <= sim.State.Frame; f++)
            {
                // 按**实体 Id** 读回（历史数组按 EntityId 就地排序，远端槽位常带零身份——下标不可靠）。
                if (!sim.TryGetExecutedInput(f, localEntityId, out SimInputFrame carried))
                    break;                                    // 理论不达（本渲染帧刚执行刚入史）
                carried.EntityId = localEntityId;             // 服务器 InputGate 覆写防伪；与主路径同口径
                _battle.Client.SendInput(f, carried, viewFrame: viewFrame);
            }
        }

        /// <summary>把本地意图摊进"全体玩家一帧"的规范数组（其余槽位空输入——Sim 按 EntityId 解析，空槽自然跳过）。</summary>
        private void FillLocalInputs(in SimInputFrame local)
        {
            for (int i = 0; i < _localInputs.Length; i++) _localInputs[i] = default;
            int slot = _battle.Client.PlayerId;
            if (slot >= 0 && slot < _localInputs.Length) _localInputs[slot] = local;
        }
    }
}
