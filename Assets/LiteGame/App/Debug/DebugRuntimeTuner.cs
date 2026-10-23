#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>运行态调试旋钮：世界时钟（时停/变速）、UI 时钟、事件中心 StrictMode。
    /// 连续旋钮不命令化；GM「重置时钟」经命令中心派发，处理器持有本件。
    /// release 构建零残留（条件编译）。</summary>
    public sealed class DebugRuntimeTuner : MonoBehaviour
    {
        private IWorldClock _world;
        private IUIClock _ui;
        private EventCenter _events;
        private CommandCenter _commands;                     // 命令中心（离散调试动作经 GM 命令——§6 闭环）
        private IDisposable _gmCommandRegistration;          // GM 示范命令注册（重注入先注销——幂等）

        [Header("世界时钟（时停/变速）")]
        [Tooltip("同步开关：false = 停止每帧覆写，允许运行期程序直接改时钟（时序验证/脚本演出用）")]
        public bool SyncEnabled = true;
        [Range(0f, 2f)] public float WorldTimeScale = 1f;
        public bool WorldPaused;

        [Header("UI 时钟")]
        public bool UiPaused;

        [Header("事件中心")]
        public bool StrictMode = true;

        public void Inject(IWorldClock world, IUIClock ui, EventCenter events, CommandCenter commands)
        {
            _world = world; _ui = ui; _events = events; _commands = commands;
            // GM 示范命令（§6/批2 消费端闭环）：发现面据此列命令，不硬编码清单；重注入先注销
            _gmCommandRegistration?.Dispose();
            if (_commands != null)
                _gmCommandRegistration = _commands.Register(new GmResetClockHandler(this),
                    new CommandOptions(gmOnly: true, description: "重置时钟（Scale=1、不暂停）"));
        }

        /// <summary>GM 面板执行入口（Inspector 右键菜单 = 离散动作的编辑器原生入口——经 Send 全链：权限门→处理器→审计）。</summary>
        [ContextMenu("GM 命令：重置时钟")]
        private void GmResetClock() => _commands?.Send(new GmResetClockCommand());

        /// <summary>把连续旋钮落回默认（Scale=1、不暂停）。</summary>
        public void ResetClock()
        {
            WorldTimeScale = 1f;
            WorldPaused = false;
            UiPaused = false;
        }

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
