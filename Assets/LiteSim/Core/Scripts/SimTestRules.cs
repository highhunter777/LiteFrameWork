namespace LiteSim
{
    /// <summary>
    /// 测试房规则（本地测试形态专用）：随房入口设置、对局中不变——客户端预测与进程内服务器同进程读同一静态，
    /// 两端确定性由"同一入口同一值"保证；常规入口进房时复位 false。**在线房间级下发未做**——
    /// 真 KCP 形态不提供测试房（见 ProcedureMatch 的入口口径）。
    /// </summary>
    public static class SimTestRules
    {
        /// <summary>全房免死：跨死亡线时 Hp 保底 1、不写 Kill/Death——命中/受击反馈保留，目标永不消失。</summary>
        public static bool NoDeath;
    }
}