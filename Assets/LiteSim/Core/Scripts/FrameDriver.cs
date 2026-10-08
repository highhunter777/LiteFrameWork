using System;

namespace LiteSim
{
    /// <summary>
    /// 帧驱动器（《状态同步实施方案》§3.2）：累加器 + 追帧（MaxCatchUp = 5）+ 固定 dt。
    /// 不吃 IGameClock（GameClock 可变速/暂停，服务表现层；逻辑帧恒 60Hz）；
    /// 不实现 ITickable（Core 零引擎依赖）——测试直接驱动，SimView 包装。
    /// 每逻辑帧末回调消费方（帧事件交付），随后清空事件缓冲；
    /// 积压超过 MaxCatchUp 时丢弃余量（防死亡螺旋——追不上就跳过，不累积）。
    /// </summary>
    public sealed class FrameDriver
    {
        private float _accumulator;

        /// <summary>上一次 Tick 实际推进的逻辑帧数（诊断/HUD 用）。</summary>
        public int StepsLastTick { get; private set; }

        /// <summary>
        /// 推进逻辑帧。<paramref name="onLogicalFrame"/> 在每个逻辑帧 Step 结束后、事件清空前回调
        /// （消费帧事件的唯一时机——追帧时每帧的事件都能交付，§3.7）。
        /// </summary>
        public void Tick(float realDelta, SimWorldState s, in SimMapData map, SimInputFrame[] inputs,
            in CombatValues values, WeaponTable weapons, Action<SimWorldState> onLogicalFrame = null)
        {
            _accumulator += realDelta;

            int steps = 0;
            while (steps < SimConfig.MaxCatchUp && _accumulator >= SimConfig.Dt)
            {
                SimStep.Step(s, map, inputs, values, weapons);

                if (onLogicalFrame != null) onLogicalFrame(s);
                s.Events.Clear(); // 消费后清空（无消费方也清——帧事件是帧内瞬态）

                _accumulator -= SimConfig.Dt;
                steps++;
            }

            // 追帧上限打满仍有积压 → 丢弃余量（防死亡螺旋；确定性不受影响——同一输入序列同一行为）
            if (_accumulator >= SimConfig.Dt) _accumulator = 0f;

            StepsLastTick = steps;
        }
    }
}
