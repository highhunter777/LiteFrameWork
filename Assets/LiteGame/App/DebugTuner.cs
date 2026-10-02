#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>调试调参组件：Inspector 拖滑条实时调节运行时旋钮（§12.5）。
    /// GameEntry 装配期注入目标；Update 同步字段 → 目标（每帧几次赋值，零成本）。
    /// 挂 GameEntry 同 GameObject，随 DontDestroyOnLoad 常驻。release 构建零残留（条件编译）。</summary>
    public sealed class DebugTuner : MonoBehaviour
    {
        private IWorldClock _world;
        private IUIClock _ui;
        private EventCenter _events;

        [Header("世界时钟（时停/变速）")]
        [Tooltip("同步开关：false = 停止每帧覆写，允许运行期程序直接改时钟（时序验证/脚本演出用）")]
        public bool SyncEnabled = true;
        [Range(0f, 2f)] public float WorldTimeScale = 1f;
        public bool WorldPaused;

        [Header("UI 时钟")]
        public bool UiPaused;

        [Header("事件中心")]
        public bool StrictMode = true;

        [Header("联机（离线隔离开发）")]
        [Tooltip("本地服务器：对局在**本进程内**跑真 RoomRuntime 内核，不连 127.0.0.1:17777，也不需要起 RoomServer 进程。\n" +
                 "真实边界：真 Sim/真 InputGate/真快照差分与协议；**无 Socket（无丢包/延迟/MTU）、无票据验签、单房间、剩余席位自动补位（站桩对手）**。\n" +
                 "弱网、断线重连真实性、多房间隔离与真实多人交互仍须在真服务器上验。")]
        public bool UseLocalServer;

        /// <summary>本地服务器开关的全局读点（流程层不认识 DebugTuner）。</summary>
        /// <remarks>
        /// 静态是因为消费者 `ProcedureMatch` 由装配根构造，而 DebugTuner 是**场景里可选挂**的调试组件
        /// （GameEntry 只做 `GetComponent<DebugTuner>()?.Inject(...)`）—— 为它加一条装配链不值当。
        /// **release 下本文件整体被条件编译剥离**，故该静态不存在于正式包（§22 禁"不可重置的静态服务"，
        /// 此处的静态是调试开关、随 #if 整段消失）。
        /// </remarks>
        public static bool UseLocalServerEnabled;

        public void Inject(IWorldClock world, IUIClock ui, EventCenter events)
            { _world = world; _ui = ui; _events = events; }

        private void Update()
        {
            UseLocalServerEnabled = UseLocalServer;      // 开关全局同步（见上面静态字段的说明）

            if (_world == null || !SyncEnabled) return;    // 关同步：滑杆停管，时钟归程序直控
            _world.TimeScale = WorldTimeScale;
            _world.Paused   = WorldPaused;
            _ui.Paused      = UiPaused;
            _events.StrictMode = StrictMode;
        }
    }
}
#endif
