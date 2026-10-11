namespace LiteSim
{
    /// <summary>
    /// 测试模式宿主钩子（本地测试形态专用）＋**活动局规则登记口**。
    ///
    /// **宿主钩子六件**（<see cref="Active"/>/<see cref="BotFrozen"/>/<see cref="BotInfiniteHp"/>/
    /// <see cref="BotResetRequested"/>/<see cref="TeleportDispatch"/>/
    /// <see cref="TeleportTarget"/>）：由 LiteGame 入口设置、**本地服宿主循环**（非确定性判定路径）读取
    /// ——进程内服务器据此执行冻结/回满/重置/传送动作；常规入口进房时复位。**在线房间级下发未做**——
    /// 真 KCP 形态不提供测试房（见 ProcedureMatch 的入口口径）。
    ///
    /// **免死/无限子弹已迁出静态**：运行态单源是每局 <see cref="MatchTestRules"/> 持有件——
    /// 经 <see cref="SimStep"/> 脊柱注入确定性系统（DamageSystem/WeaponSystem 不再读静态）；
    /// 进房初值随 <see cref="CombatValues"/> 进联机身份摘要；<see cref="ActiveMatch"/> 只作
    /// "当前活动局持有件"的登记口（客户端预测绑定 + GM 面板中局开合的写通道）——
    /// 确定性判定路径不读本类的任何静态。
    /// 读取方横跨程序集（Sim 系统 + 本地服宿主 DevLocalServer），故住 LiteSim.Core 而非 LiteGame。
    /// </summary>
    public static class SimTestRules
    {
        // ---- 宿主钩子（写入方：LiteGame 入口/流程；读取方：本地服宿主循环）----
        /// <summary>测试模式进行中（本地服形态）——宿主循环据此执行冻结/传送钩子。</summary>
        public static bool Active;

        /// <summary>bot 冻结：权威侧每帧把补位席位实体位置回写进房时快照（站桩加强，防推挤类漂移）。</summary>
        public static bool BotFrozen;

        /// <summary>bot 无限生命（GM 现场开关，Live）：权威侧每帧把补位席位回满——单帧伤害恒小于满血
        /// ⇒ 假人永不死（命中/飘字反馈照常）；已死者跳过（走完复活链自然回满）。</summary>
        public static bool BotInfiniteHp;

        /// <summary>重置假人分发：权威侧执行一次（补位席位满血回出生点 + 生命更替），消费即清。</summary>
        public static bool BotResetRequested;

        /// <summary>传送分发：准心目标已解析，待权威侧（本地服）执行一次。</summary>
        public static bool TeleportDispatch;

        /// <summary>传送目标点（世界）。</summary>
        public static SimVector3 TeleportTarget;

        // ---- 活动局登记口（进房写入、StartGame 绑定、GM 中局开合写它；判定路径不读）----
        /// <summary>当前活动局的测试规则持有件（每局新建替换；null = 无活动局）。</summary>
        public static MatchTestRules ActiveMatch;
    }
}
