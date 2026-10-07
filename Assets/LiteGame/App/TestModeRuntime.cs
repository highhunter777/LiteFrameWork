#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteSim;

namespace LiteGame
{
    /// <summary>测试模式的运行时快照与控制桥（开发/编辑器/开发包专用；release 整段剥离）。
    /// 配置面在 <c>TestModeSettings</c> 资产（测试面板编辑）；进入/退出由 GM 面板或 F10 发起。
    /// 对局行为（免死/人数/冻结/传送/时间缩放）全部由本快照驱动——测试模式恒为本地服形态，两端同进程同源。</summary>
    public static class TestModeRuntime
    {
        /// <summary>当前是否处于测试模式（进入置位；退出/离场由 GM 面板或对局内退出请求清除）。</summary>
        public static bool Active;

        /// <summary>是否已由面板应用过配置（F10 快捷入口仅在未配置时套默认）。</summary>
        public static bool Configured;

        // ---- 配置快照（面板/GM 进入时写入）----
        public static bool NoDeath = true;          // 全房免死（Hp 保底 1、目标不消失）
        public static float TimeScale = 1f;         // 世界时钟缩放（DebugTuner 落钟）
        public static bool Paused;                  // 世界时钟暂停
        public static bool TeleportEnabled = true;  // 定点传送（T 键 / GM 面板 → 准心点）
        public static bool BotFrozen;               // bot 冻结（权威侧位置每帧回写）
        public static int BotCount = 1;             // 补位bot 数量（总席位 = 1 + BotCount）

        /// <summary>
        /// **身位/爆头调试绘制**开关（默认关）：打开后在每个活体身上常驻画**服务端身位圆柱**
        /// （半径/高 = 烘焙判定常量，与命中判定同源）与爆头带段（黄圈=下沿、红圈=上沿）——
        /// 肉眼核对"判定柱在哪、多高算爆头、贴墙距离多远"（绘制件 <see cref="BattleHeadshotDebugDrawer"/>）。
        /// 纯诊断，不改任何判定；面板可现场开关，不必重进测试房。
        /// </summary>
        public static bool DrawHeadshotDebug;

        // ---- 请求桥（编辑器面板/热键 → 运行时；一次性标志）----
        public static bool EnterRequested;          // 进入测试模式（主菜单消费 → 进测试房）
        public static bool ExitRequested;           // 退出测试模式（对局内 = 请求离场并关模式）
        public static bool TeleportRequested;       //「传送到准心」请求（流程解析目标 → SimTestRules 分发）

        /// <summary>默认口径（F10 快捷入口在未配置时套用）：免死开、其余不干预。</summary>
        public static void ApplyDefaults()
        {
            NoDeath = true;
            TimeScale = 1f;
            Paused = false;
            TeleportEnabled = true;
            BotFrozen = false;
            BotCount = 1;
            DrawHeadshotDebug = false;
        }
    }
}
#endif