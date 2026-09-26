#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>调试调参组件：Inspector 拖滑条实时调节运行时旋钮（§12.5）。
    /// GameEntry 装配期注入目标（step 7）；Update 同步字段 → 目标（每帧几次赋值，零成本）。
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

        public void Inject(IWorldClock world, IUIClock ui, EventCenter events)
            { _world = world; _ui = ui; _events = events; }

        private void Update()
        {
            if (_world == null || !SyncEnabled) return;    // 关同步：滑杆停管，时钟归程序直控
            _world.TimeScale = WorldTimeScale;
            _world.Paused   = WorldPaused;
            _ui.Paused      = UiPaused;
            _events.StrictMode = StrictMode;
        }
    }
}
#endif
