namespace LiteSim
{
    /// <summary>
    /// 测试模式规则（本地测试形态专用）：随房入口设置、对局中不变——客户端预测与进程内服务器同进程读同一静态，
    /// 两端确定性由"同一入口同一值"保证；常规入口进房时复位 false。**在线房间级下发未做**——
    /// 真 KCP 形态不提供测试房（见 ProcedureMatch 的入口口径）。
    /// 读取方横跨程序集（Sim 系统 + 本地服宿主 DevLocalServer），故住 LiteSim.Core 而非 LiteGame。
    /// </summary>
    public static class SimTestRules
    {
        /// <summary>全房免死：跨死亡线时 Hp 保底 1、不写 Kill/Death——命中/受击反馈保留，目标永不消失。</summary>
        public static bool NoDeath;

        // ---- 运行时行为钩子（写入方：LiteGame 入口/流程；读取方：本地服宿主循环）----
        /// <summary>测试模式进行中（本地服形态）——宿主循环据此执行冻结/传送钩子。</summary>
        public static bool Active;

        /// <summary>bot 冻结：权威侧每帧把补位席位实体位置回写进房时快照（站桩加强，防推挤类漂移）。</summary>
        public static bool BotFrozen;

        /// <summary>传送分发：准心目标已解析，待权威侧（本地服）执行一次。</summary>
        public static bool TeleportDispatch;

        /// <summary>传送目标点（世界）。</summary>
        public static SimVector3 TeleportTarget;
    }
}