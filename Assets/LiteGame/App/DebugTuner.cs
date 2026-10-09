#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>调试调参组件：**静态启动配置与旋钮**（Inspector 编辑，场景序列化，进 Play 前定好；
/// Play 中改 Inspector 值同样即时生效）。GameEntry 装配期注入目标；Update 同步字段 → 目标。
/// 挂 GameEntry 同 GameObject，随 DontDestroyOnLoad 常驻。**局内可调项在 GM 面板**（测试模式现场开关）。
/// release 构建零残留（条件编译）。</summary>
    public sealed class DebugTuner : MonoBehaviour
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

        [Header("联机（离线隔离开发）")]
        [Tooltip("本地服务器：对局在**本进程内**跑真 RoomRuntime 内核，不连 127.0.0.1:17777，也不需要起 RoomServer 进程。\n" +
                 "真实边界：真 Sim/真 InputGate/真快照差分与协议；**无 Socket（无丢包/延迟/MTU）、无票据验签、单房间、剩余席位自动补位（站桩对手）**。\n" +
                 "弱网、断线重连真实性、多房间隔离与真实多人交互仍须在真服务器上验。")]
        public bool UseLocalServer;

        [Header("测试模式启动配置（进房时快照进运行时；局内可调项在 GM 面板）")]
        [Tooltip("全房免死：跨死线 Hp 保底 1、不写 Kill/Death——目标不消失，命中/受击反馈保留")]
        public bool NoDeath = true;
        [Tooltip("无限子弹：进房初值；对局中可在 GM 面板现场开合")]
        public bool InfiniteAmmo;
        [Tooltip("瞄准激光：进房初值；对局中可在 GM 面板现场开合")]
        public bool LaserSight = true;
        [Tooltip("伤害数字：进房初值；对局中可在 GM 面板现场开合")]
        public bool DamageNumbers = true;
        [Tooltip("爆头自动验证：替换输入源自动打爆头（进房初值；对局中可在 GM 面板现场开合）")]
        public bool AutoHeadshot;
        [Tooltip("爆头区域可视化：进房初值；对局中可在 GM 面板现场开合")]
        public bool DrawHeadshotDebug;
        [Tooltip("定点传送：T 键 / GM 面板按钮 → 传送到当前准心点")]
        public bool TeleportEnabled = true;
        [Tooltip("bot 冻结：权威侧每帧把补位 bot 位置回写到进房时快照（站桩加强，防推挤类漂移）")]
        public bool BotFrozen;
        [Tooltip("补位 bot 数量（总席位 = 1 + 本值）"), Min(0)]
        public int BotCount = 1;

        /// <summary>本地服务器开关的全局读点（流程层不认识 DebugTuner）。</summary>
        /// <remarks>
        /// 静态是因为消费者 `ProcedureMatch` 由装配根构造，而 DebugTuner 是**场景里可选挂**的调试组件
        /// （GameEntry 只做 `GetComponent<DebugTuner>()?.Inject(...)`）—— 为它加一条装配链不值当。
        /// **release 下本文件整体被条件编译剥离**，故该静态不存在于正式包（§22 禁"不可重置的静态服务"，
        /// 此处的静态是调试开关、随 #if 整段消失）。
        /// </remarks>
        public static bool UseLocalServerEnabled;

        /// <summary>把**本组件的启动配置**快照进运行时并置位测试模式（GM 面板"进入测试模式"/主菜单 F10 共用）；
        /// 场景未挂 DebugTuner 时套默认口径（<see cref="TestModeRuntime.ApplyDefaults"/>）。
        /// 逐项经 <see cref="TestModeOptions.Set"/> 落值（副作用收口在该表）。</summary>
        public static void ApplySnapshotOrDefaults()
        {
            var tuner = UnityEngine.Object.FindAnyObjectByType<DebugTuner>(FindObjectsInactive.Include);
            if (tuner == null)
            {
                TestModeRuntime.ApplyDefaults();
            }
            else
            {
                TestModeOptions.Set(TestModeOptions.Id.NoDeath, tuner.NoDeath);
                TestModeOptions.Set(TestModeOptions.Id.InfiniteAmmo, tuner.InfiniteAmmo);
                TestModeOptions.Set(TestModeOptions.Id.LaserSight, tuner.LaserSight);
                TestModeOptions.Set(TestModeOptions.Id.DamageNumbers, tuner.DamageNumbers);
                TestModeOptions.Set(TestModeOptions.Id.AutoHeadshot, tuner.AutoHeadshot);
                TestModeOptions.Set(TestModeOptions.Id.DrawHeadshotDebug, tuner.DrawHeadshotDebug);
                TestModeOptions.Set(TestModeOptions.Id.TeleportEnabled, tuner.TeleportEnabled);
                TestModeOptions.Set(TestModeOptions.Id.BotFrozen, tuner.BotFrozen);
                TestModeRuntime.BotCount = tuner.BotCount;               // 数量项不在开关表（int）
            }
            TestModeRuntime.Active = true;
        }

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
