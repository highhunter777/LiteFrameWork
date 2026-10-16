using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 主流程：进入 Main 即加载**对局场景组**——Rig（vcam 装配）＋ 地图（内容＋判定代理），两者**加性**
    /// 叠加在启动场景（Boot.unity，只含引导件 + 主相机）之上；失败进确定错误态。**训练模式**复用同组（地图换 TrainingGround）。
    /// 场景经 **YooAsset 内容包**加载，而资源包在 `ProcedurePatch` 才初始化
    /// （§7.1 Patch 先于 Preload），故不可能更早；又必须在进对局前，所以是 Main 进入时。
    /// 后续流程树（主菜单 → 选模式 → 进场景 → 战斗 → 结算）在此扩展；
    /// Match/Battle 预留见 <see cref="ProcedureId"/>。
    /// </summary>
    public sealed class ProcedureMain : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>首图地图场景（内容包 location = 资源完整路径；纯地图：视觉＋灯＋`MapCollision` 代理＋出生标记，禁含 vcam）。
        /// 场景文件名不使用中文（跨平台打包风险——路径字面量纪律）。训练模式换本参数为 TrainingGround（同装载模型）。</summary>
        private const string MapScene = "Assets/Scenes/Arena01.unity";

        /// <summary>对局装配场景（Rig：`Player Camera`/`Aim Camera` 两台 vcam——相机服务跨场景按优先级解析）。</summary>
        private const string RigScene = "Assets/Scenes/BattleRig.unity";

        /// <summary>场景切换（可空 = 不切场景，测试装配）。</summary>
        private readonly ISceneService _scenes;

        /// <summary>五页页面流（批C-④；可空 = 测试装配不接 UI）。</summary>
        private readonly PageFlow _pages;

        /// <summary>联机入口编排件（判"是否配置了 Meta 基址"——入口页只在配置形态开；可空 = 离线装配）。</summary>
        private readonly MetaEntryService _meta;

        public ProcedureMain(ISceneService scenes = null, CancellationToken rootToken = default,
            PageFlow pages = null, MetaEntryService meta = null)
            : base(rootToken)
        {
            _scenes = scenes;
            _pages = pages;
            _meta = meta;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 测试入口：F9 = 进入测试对局（隔离测试发行者身份）。
            // 主菜单 → 进对局的正式入口（页面按钮/Matchmaking）由 UI 层提供——此处只留热键，不进正式 UI。
            // 门禁：开发/编辑器可用，正式包无该入口（不塞进 Release 玩家）。
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
                m.Request(ProcedureId.Match);
            // F10 = 测试模式快捷入口（GM 面板等效）：把场景 DebugTuner 的启动配置快照进运行时（未挂则套默认口径）；进入即本地服 Room-Test。
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                DebugTuner.ApplySnapshotOrDefaults();
                TestModeRuntime.RequestEnter();
            }
            if (TestModeRuntime.TakeEnterRequest())             // GM 面板/F10 同一条链（读即清）
            {
                TestModeRuntime.Active = true;
                m.Request(ProcedureId.Match, new ProcedureArgs(testRoom: true));
            }
            if (TestModeRuntime.TakeExitRequest())              // 主菜单里点退出：只关模式（对局内由 Battle 消费=离场）
            {
                TestModeRuntime.Active = false;
            }
#endif
            // 启动走廊窗口④：Main 只报场景进度不持所有权（走廊在 Preload 开——通用加载页 §1）
            if (LaunchLoading.Active)
                LaunchLoading.Report("正在加载场景…", _scenes != null ? _scenes.Progress : 0f);
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                await EnterBattleSceneAsync(ct);

                // 上云入口（批C-④）：配置了 Meta 基址 → 首进 Main 开登录页（五页入口，《上云测试页面层级设计》§1）；
                // 未配置（离线/本地开发形态）零变化。**只开一次**——对局结束/离场返回 Main 不重开入口
                // （页面流接管：结算页或大厅在场，见 PageFlow.OpenEntryOnce）。
                if (_pages != null && _meta != null && _meta.IsConfigured) _pages.OpenEntryOnce();

                // 启动走廊收口（窗口⑥ 首屏接线点——登录页/大厅或玩法场景就绪即收，通用加载页 §1）。
                LaunchLoading.End();
            }
            catch (OperationCanceledException) { LaunchLoading.End(); /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                LaunchLoading.End();                         // 失败收口（错误页不能被遮罩挡住）
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }

        /// <summary>
        /// 加载对局场景组（**加性**：Rig ＋ 地图——各自幂等；顺序不敏感，相机服务靠 sceneLoaded 重解析与 Follow 兜底）。
        /// 未注入场景服务（测试装配）= 跳过——本阶段无其他职责。
        /// 职责边界：地图＝纯内容（禁 vcam）；Rig＝装配件（禁地图内容）——换图不换装配。
        ///
        /// **不做"回填瞄准相机"这件事**：设备源在采样时发现缓存的相机已随旧场景销毁（Unity 伪 null）
        /// 会**自动回落到当前 `Camera.main`**——那是更强的保证（谁在什么时候切场景都对）。
        /// </summary>
        private async UniTask EnterBattleSceneAsync(CancellationToken ct)
        {
            if (_scenes == null) return;

            if (!_scenes.IsLoaded(RigScene))
                await _scenes.LoadAdditiveAsync(RigScene, ct);
            if (!_scenes.IsLoaded(MapScene))
                await _scenes.LoadAdditiveAsync(MapScene, ct);
            UnityEngine.Debug.Log($"[Main] 对局场景组已加载:Rig={RigScene} Map={MapScene}（叠加数 {_scenes.AdditiveCount}）");
        }
    }
}
