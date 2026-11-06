using System;

namespace LiteSim
{
    /// <summary>
    /// 60Hz 逻辑帧泵（与 <see cref="FrameDriver"/> 的分工）。
    /// </summary>
    public sealed class FramePump
    {
        /// <summary>推进 <paramref name="steps"/> 个逻辑帧（steps ≤ 0 为无操作）。
        /// <paramref name="rules"/> = 本局测试规则（null = 无规则）。</summary>
        public void Step(int steps, SimWorldState s, in SimMapData map, SimInputFrame[] inputs,
            in CombatValues values, WeaponTable weapons, Action<SimWorldState> onLogicalFrame = null,
            MatchTestRules rules = null)
        {
            for (int i = 0; i < steps; i++)
            {
                SimStep.Step(s, map, inputs, values, weapons, rules: rules);
                if (onLogicalFrame != null) onLogicalFrame(s);
                s.Events.Clear();   // 帧内瞬态：消费后清
            }
        }
    }
}
