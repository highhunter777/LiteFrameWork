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
        [Tooltip("本地服务器：对局在**本进程内**跑真 RoomRuntime 内核，不连独立 RoomServer 进程（联调端点常量＝ProcedureMatch.TestHost/TestPort——此处不抄地址，改端口不撒谎），也不需要起 RoomServer 进程。\n" +
                 "真实边界：真 Sim/真 InputGate/真快照差分与协议；**无 Socket（无丢包/延迟/MTU）、无票据验签、单房间、剩余席位自动补位（站桩对手）**。\n" +
                 "弱网、断线重连真实性、多房间隔离与真实多人交互仍须在真服务器上验。")]
        public bool UseLocalServer;

        [Header("开发落点（直入 / 开发页）")]
        [Tooltip("直入训练场：启动即进测试模式对局（本地服 Room-Test），不加载登录页；退出按 UI（ESC→设置页→退出）回大厅页。**优先级高于「直入工作台」**。")]
        public bool DirectToTraining;

        [Tooltip("直入工作台：启动即进入工作台（加性加载工作台场景），不加载登录页；退出按 UI（ESC→设置页→退出）回大厅页。与本地服务器开关同族开发开关；工作台场景默认开、Boot 默认关。")]
        public bool WorkbenchMode;

        [Tooltip("开发页入口：编辑器在屏幕左上显示「开发」按钮（测试模式 / UI 工作台 / 关闭）——大厅页可随时跳转。")]
        public bool DevMenu = true;

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

        /// <summary>工作台模式开关的全局读点（流程层不认识 DebugTuner；见 <see cref="UseLocalServerEnabled"/> 同款理由）。</summary>
        public static bool WorkbenchModeEnabled;

        /// <summary>直入训练场开关的全局读点（同 <see cref="WorkbenchModeEnabled"/> 理由）。</summary>
        public static bool DirectToTrainingEnabled;

        /// <summary>工作台**面板内容** dev 预制体路径（单源；工作台场景内以预制体实例承载，构建器与守卫同引用）。</summary>
        public const string WorkbenchPrefabPath = "Assets/LiteGame/DevTool/WorkbenchUI.prefab";

        /// <summary>工作台**场景**路径（单源；工作台模式加性加载它——场景文件为 dev 件，不在收集组，
        /// 故加载仅编辑器形态）。</summary>
        public const string WorkbenchScenePath = "Assets/Scenes/UiWorkbench.unity";

        /// <summary>开发页（dev 覆盖件）预制体路径（单源；构建器与守卫同引用）。</summary>
        public const string DevMenuPrefabPath = "Assets/LiteGame/DevTool/DevMenuUI.prefab";

        private bool _workbenchLoaded;
        private bool _devMenuSpawned;

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
            WorkbenchModeEnabled = WorkbenchMode;
            DirectToTrainingEnabled = DirectToTraining;
            EnsureWorkbenchScene();                        // 工作台模式：加载工作台场景（一次性）
            EnsureDevMenu();                               // 开发页：开出「开发」入口（一次性）

            if (_world == null || !SyncEnabled) return;    // 关同步：滑杆停管，时钟归程序直控
            _world.TimeScale = WorldTimeScale;
            _world.Paused   = WorldPaused;
            _ui.Paused      = UiPaused;
            _events.StrictMode = StrictMode;
        }

        // ---- 工作台落点请求桥（读即清，TestModeRuntime 先例）：驱动件写、流程层读 ----

        private static bool s_workbenchMenuRequested;
        private static bool s_workbenchExitRequested;

        /// <summary>请求打开"对局菜单 UI"（设置页；工作台落点复用对局退场 UI）——驱动件在 ESC/开发页钮时置位。</summary>
        public static void RequestWorkbenchMenu() => s_workbenchMenuRequested = true;

        /// <summary>取工作台菜单请求（读即清）。</summary>
        public static bool TakeWorkbenchMenuRequest()
        {
            bool v = s_workbenchMenuRequested;
            s_workbenchMenuRequested = false;
            return v;
        }

        /// <summary>请求退出工作台落点（驱动件在"菜单退出后"置位）——流程层收到即关闭工作台场景。</summary>
        public static void RequestWorkbenchExit() => s_workbenchExitRequested = true;

        /// <summary>取工作台退出请求（读即清）。</summary>
        public static bool TakeWorkbenchExitRequest()
        {
            bool v = s_workbenchExitRequested;
            s_workbenchExitRequested = false;
            return v;
        }

        /// <summary>关闭工作台场景（落点收尾：回大厅页——UI 服务与页面都在 DontDestroyOnLoad 根上，不受影响）。
        /// 仅编辑器形态（dev 场景不进收集组）；未加载 = no-op。</summary>
        public static void CloseWorkbenchScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(WorkbenchScenePath);
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (Application.isPlaying)
            {
                // **Play 内必须走运行时 API**：EditorSceneManager 在 Play 中拒绝场景操作（实测静默无效）
                UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
                Log.Info("工作台模式：工作台场景卸载中（落点收尾 → 回大厅页）", LogTag.Workbench);
                return;
            }
#if UNITY_EDITOR
            if (UnityEngine.SceneManagement.SceneManager.sceneCount <= 1)   // 安全网：唯一场景不可卸（编辑期）
            {
                RequestWorkbenchHide();
                return;
            }
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            Log.Info("工作台模式：工作台场景已关闭（落点收尾 → 回大厅页）", LogTag.Workbench);
#endif
        }

        private static bool s_workbenchHideRequested;

        /// <summary>请求隐藏工作台 UI（场景不可卸时的退化收尾）——工作台驱动件读即清并自我收尾。</summary>
        public static void RequestWorkbenchHide() => s_workbenchHideRequested = true;

        /// <summary>取工作台隐藏请求（读即清）。</summary>
        public static bool TakeWorkbenchHideRequest()
        {
            bool v = s_workbenchHideRequested;
            s_workbenchHideRequested = false;
            return v;
        }

        /// <summary>工作台模式：**加性加载工作台场景**（`WorkbenchScenePath`）——开关勾上即"进入工作台"，
        /// 与"直接 Play 工作台场景"同一个场景资产（场景内以预制体实例承载面板，内容单源）。
        /// 幂等：只加载一次；已加载（含"直接 Play 形态"下场景就是当前场景）则跳过。
        /// 形态限制：工作台场景是 dev 件、不在收集组，故加载**仅编辑器形态**（开发版 Player 只享受"跳过对局场景组"）。</summary>
        private void EnsureWorkbenchScene()
        {
            if (!WorkbenchMode || _workbenchLoaded) return;
            _workbenchLoaded = true;
            if (UnityEngine.SceneManagement.SceneManager.GetSceneByPath(WorkbenchScenePath).isLoaded) return;   // 直接 Play 形态
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(WorkbenchScenePath) == null)
            {
                Log.Warning($"工作台模式：工作台场景缺失——{WorkbenchScenePath}", LogTag.Workbench);
                return;
            }
            var scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                WorkbenchScenePath, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
            Log.Info(scene.IsValid()
                ? $"工作台模式：工作台场景已加性加载（{WorkbenchScenePath}）"
                : $"工作台模式：工作台场景加载失败（{WorkbenchScenePath}）", LogTag.Workbench);
#else
            Log.Warning("工作台模式：工作台场景仅编辑器形态可加载（dev 场景不进收集组）——本形态只跳过对局场景组", LogTag.Workbench);
#endif
        }

        /// <summary>开发页（dev 覆盖件）：开出「开发」入口——测试模式（训练场）/ UI 工作台 / 关闭，
        /// 大厅页可随时跳（《UI工作台设计》§5 入口矩阵）。幂等：只建一次；dev 预制体不在收集组 ⇒ 仅编辑器形态。</summary>
        private void EnsureDevMenu()
        {
            if (!DevMenu || _devMenuSpawned) return;
            _devMenuSpawned = true;
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(DevMenuPrefabPath);
            if (prefab == null)
            {
                Log.Warning($"开发页：预制体缺失——{DevMenuPrefabPath}（重跑 AgentScripts/BuildWorkbenchPanel.cs）", LogTag.Workbench);
                return;
            }
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "DevMenuUI";
            UnityEngine.Object.DontDestroyOnLoad(instance);   // 常驻：工作台场景关闭后入口仍在（否则退出工作台＝没了入口）
            Log.Info($"开发页：入口已开出（{DevMenuPrefabPath}）", LogTag.Workbench);
#else
            Log.Warning("开发页：仅编辑器形态可用（dev 预制体不进收集组）", LogTag.Workbench);
#endif
        }
    }
}
#endif
