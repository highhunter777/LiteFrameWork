namespace LiteSim
{
    /// <summary>
    /// 动作四阶段（《游戏业务系统总设计》§6.1：None/Windup/Active/Recovery）。
    /// 主动作槽用 None..Recovery；技能槽常态停在 None（执行期占用主动作槽）。
    /// </summary>
    public enum ActionPhase : byte
    {
        None = 0,
        Windup = 1,
        Active = 2,
        Recovery = 3,
    }
}
