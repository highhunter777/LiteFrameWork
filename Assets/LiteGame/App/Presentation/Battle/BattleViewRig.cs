using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using LiteSim;
using LiteView;
using UnityEngine;
using LiteClient.UI;

namespace LiteGame
{
    /// <summary>
    /// 对局视图装配件（<see cref="ProcedureBattle"/> 的视图域，一处收口全部表现件接线）：
    /// 自有 `BattleView` 子作用域 → 资源取件（<see cref="BattleViewAssets"/>）→ 视图本体
    /// （<see cref="BattleViewSceneAssembler"/>）→ HUD 族（<see cref="BattleHudAssembler"/>）→
    /// 战斗反馈（<see cref="BattleFeedbackAssembler"/>）→ 视角/指针（<see cref="BattleLookController"/>）。
    ///
    /// **三面**：<see cref="LoadAsync"/>（取件与降级）、<see cref="Attach"/>（建视图与接线）、
    /// <see cref="Tick"/>（按相位驱动各域）、<see cref="Dispose"/>（按相位逆序拆：消费者先于生产者）。
    /// 流程层只保留"取会话 → 等信令 → 每帧驱动 → 收尾"的编排。
    ///
    /// **所有权**：视图根 GameObject 与 `BattleView` 作用域归本件；驱动实例与租约归各协作件
    /// （视图/ HUD / 反馈 / 视角）。视图是上下文的消费者，**先拆视图再拆上下文**（反向释放会读到已拆的 Sim）。
    /// 服务本体（内容/输入/相机/VFX/音频/命令）归装配根，本件只持接线所需的引用。
    ///
    /// **相位顺序**（tick/dispose 与拆分前逐项一致，见批次计划 A3）：
    /// tick = 移动动画 → 权威反馈 → HUD 族 → 世界表现件 → 反馈消费者 → 视角/指针；
    /// dispose = 移动动画 → HUD 族（逐件）→ 世界表现件 → HUD 根件 → 反馈消费者（逐件）→ 传送/诊断 →
    /// 分发器 → 视角/指针 → 上下文/视图根/租约作用域。
    ///
    /// **降级口径**：缺资源/缺服务一律"警告 + 显式降级"（缺角色 prefab → 无角色视图，不造假人；缺 HUD/激光/飘字/
    /// 音效 → 该项静默降级），不把表现增益当成对局失败。
    /// </summary>
    internal sealed class BattleViewRig
    {
        private readonly IInputService _input;       // 输入服务（设备源注入/清派发状态）
        private readonly ICameraService _camera;     // 相机服务（Cinemachine 适配器；装配根建、跨对局复用）
        private readonly CommandCenter _commands;    // 命令中心（传送命令化：撤销/重做/排队派发）
        private readonly ClientScope _viewScope;     // 视图资源（prefab 租约/视图根）自有作用域

        private readonly BattleViewAssets _assets;
        private readonly BattleViewSceneAssembler _viewAssembler = new BattleViewSceneAssembler();
        private readonly BattleHudAssembler _hudAssembler;
        private readonly BattleFeedbackAssembler _feedbackAssembler;
        private readonly BattleLookController _lookController;

        private BattleContext _context;
        private Transform _viewRoot;
        private GameObject _viewRootGo;
        private IDisposable _teleportReg;            // 传送命令处理器注册（进房登记 / 离场注销）
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        private BattleHeadshotDebugDrawer _headshotDebug;   // 身位圆柱/爆头带常驻可视化（release 剥离）
        private AutoHeadshotRig _autoHeadshot;              // 爆头自动验证件（GM 面板现场开关；release 剥离）
#endif

        /// <param name="account">Account 作用域（本件在其下建 `BattleView` 子作用域——离场即整体回收）。</param>
        /// <param name="worldClock">世界钟（伤害数字寿命/合并窗；null = 飘字降级）。</param>
        internal BattleViewRig(IContentService content, IInputService input, ICameraService camera,
            ClientScope account, IVFXService vfx = null, LiteFramework.IWorldClock worldClock = null,
            AudioService audio = null, CommandCenter commands = null, CrosshairFeelProfile crosshairFeel = null,
            Func<UiThemeAsset> themeProvider = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (account == null) throw new ArgumentNullException(nameof(account));
            _input = input;
            _camera = camera;
            _commands = commands;

            _viewScope = account.CreateChild("BattleView");
            _viewRootGo = new GameObject("[BattleView]");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_viewRootGo);
            _viewRoot = _viewRootGo.transform;

            _assets = new BattleViewAssets(content, _viewScope, _viewRoot, themeProvider);
            _hudAssembler = new BattleHudAssembler(crosshairFeel);
            _feedbackAssembler = new BattleFeedbackAssembler(vfx, audio, worldClock, camera, _assets, _viewRoot);
            _lookController = new BattleLookController(input, camera);
        }

        // ---- 取件（资源租约；逐项降级）----

        /// <summary>取全部对局表现资源（角色 prefab → 激光束 → HUD → 保护壳 → 飘字底件 → 逐档材质 → 片段清单 → 音效 clip）。
        /// 逐项失败只降级该项（缺角色资源＝无角色视图），不把缺美术当对局失败。</summary>
        internal UniTask LoadAsync(CancellationToken ct) => _assets.LoadAsync(ct);

        // ---- 装配（建视图 + 接线；须在 StartGame 之后调用）----

        /// <summary>
        /// 建视图并接线：<see cref="SimView"/>（镜像/插值/静默门/相机服务）→ 移动动画驱动 → HUD 族 →
        /// 瞄准激光/保护壳 → 命中分发与反馈消费者 →〔编辑器〕爆头诊断件 → 传送命令登记。
        ///
        /// **相机不由本件创建**：装配根建好 <see cref="ICameraService"/>（内含场景配置好的虚拟相机）并
        /// 跨对局复用，本件只把端口交给 SimView，并在开局 <see cref="ICameraService.Reset"/> 一次
        /// （镜头重新落位，不从上局位置飞过来）。
        ///
        /// <paramref name="openBattleMenu"/>＝对局菜单钮回调（HUD 菜单钮点击 → 流程层开设置页——退场链；
        /// null / 旧 prefab 无此钮 = 静默降级，HUD 其余面照常）。
        /// </summary>
        internal void Attach(BattleContext context, Action openBattleMenu = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            UnityEngine.Events.UnityAction menuClick =
                openBattleMenu != null ? new UnityEngine.Events.UnityAction(openBattleMenu) : null;

            _viewAssembler.AttachCore(_context, _viewRoot, _assets.Prefab, _camera,
                _assets.ClipManifestLease?.Asset);

            HitFeedbackDispatcher dispatcher = _feedbackAssembler.CreateDispatcher(_viewAssembler.View);
            _hudAssembler.Attach(_context, _viewAssembler.View, _input,
                _assets.HudView, _assets.HudInstance, menuClick, dispatcher, PublicFactState);

            _viewAssembler.AttachLaser(_context, _input, _assets.LaserBeam);
            _viewAssembler.AttachProtectionFx(_assets.ProtectionShellLease);
            _hudAssembler.AttachRespawnHud(PublicFactState,
                () => _viewAssembler.View != null ? _viewAssembler.View.LocalEntityId : 0L,
                _viewAssembler.IsLocalDead, dispatcher);
            _lookController.Attach(_viewAssembler.View);
            _feedbackAssembler.AttachConsumers(_context, _viewAssembler.View);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 爆头区域常驻可视化（测试模式诊断件）：只读预测态，不改判定；release 剥离
            _headshotDebug = new BattleHeadshotDebugDrawer(_context.Sim, () => _context.MatchRules);
            // 爆头自动验证件（GM 面板现场开关）：替换输入源 + 统计爆头/命中，用于真实对局端到端验链路
            _autoHeadshot = new AutoHeadshotRig(_context.Sim.State, _context.LocalEntityId);
            _feedbackAssembler.Register(_autoHeadshot);
#endif
            AttachInputWiring();
            _teleportReg = _commands?.RegisterUndoable(new TeleportToAimHandler(
                aimOf: () => _lookController.AimPoint,
                originOf: ReadLocalOrigin,
                dispatch: DispatchTeleport));                       // 传送命令化：撤销/重做随命令中心可用
            Log.Info($"view-attached prefab={BattleViewAssets.EntityPrefab}", LogTag.Battle);
        }

        /// <summary>每帧驱动视图消费者（<see cref="BattleContext.Tick"/> 之后调用——权威已应用）：
        /// 移动动画 → 权威反馈 → HUD 族 → 世界表现件 → 反馈消费者 → 视角/指针（相位顺序固定）。</summary>
        internal void Tick(float elapseSeconds)
        {
            _viewAssembler.TickMovement(elapseSeconds);
            _feedbackAssembler.TickAuthority();
            _hudAssembler.Tick(elapseSeconds);
            _viewAssembler.TickWorldFx();
            _feedbackAssembler.TickVisual();
            _lookController.Tick(elapseSeconds);
        }

        /// <summary>渲染帧采样**之前**的诊断件驱动（编辑器专用；release 空实现）：自瞄验证件要替换输入源，
        /// 必须在本帧采样前落位，否则自瞄点落后一帧（GM 面板现场开关；关闭即还原设备源）。</summary>
        internal void TickPreSample()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _headshotDebug?.Tick();              // 爆头区域常驻可视化（开关关时零开销）
            if (TestModeRuntime.AutoHeadshot)
            {
                if (_autoHeadshot != null && !_autoHeadshot.Active) _autoHeadshot.Enable(_input);
            }
            else if (_autoHeadshot != null && _autoHeadshot.Active)
            {
                _autoHeadshot.Disable();         // 还原设备源（与离场清理同一路径）
            }
            _autoHeadshot?.Tick();
#endif
        }

        /// <summary>
        /// 输入接线（§3 输入三件）：**设备源已在装配根装好**（<see cref="InputModule"/>），
        /// 本件只处理"对局相机注入 + 把服务交给对局上下文 + 镜头落位"。
        /// **上下文门不在这里裁决**——拦截源在装配根按名登记（`ui.modal`），由服务采样时统一裁决。
        /// </summary>
        private void AttachInputWiring()
        {
            if (_input == null) return;                           // 无服务（替身/测试装配）= 无输入形态

            // **对局相机注入**：设备源持有的是装配根 boot 相机——叠加开发形态下它
            // **不随场景销毁**，`Camera.main` 回落也拿错机（两台相机在场），瞄准/相机相对移动会
            // 全部算在错机上。这里把真对局渲染相机（brain 所在 Camera）显式换进去。
            if (_camera is CinemachineCameraService ccs && _input.Source is NewInputIntentSource source)
            {
                ccs.EnsureCamera();                                // 视图未建时 Follow 还没跑过——先解析 vcam
                Camera renderCamera = ccs.TryGetRenderCamera();
                if (renderCamera != null) source.SetAimCamera(renderCamera);
                else Log.Warning("对局渲染相机未解析到——瞄准/移动继续用设备源现持相机", LogTag.Battle);
            }

            _context.AttachInput(_input);
            _camera?.Reset();                                     // 镜头重新落位（不从上一局位置飞过来）
        }

        /// <summary>拆视图（按相位逆序：消费者 → 分发器 → 视图 → 根件 → 作用域）。不触发对局结束——那是流程层的裁决。</summary>
        internal void Dispose()
        {
            _viewAssembler.DisposeMovement();     // 先停动画驱动（视图消费者），再拆视图本体
            _hudAssembler.DisposeRankboard();     // 排行榜（格归位——同属视图消费者）
            _hudAssembler.DisposeKillFeed();      // 击杀提示（行归位——同属视图消费者）
            _feedbackAssembler.DisposeRemoteFire();   // 远端火光（挂点缓存归零——同属视图消费者）
            _hudAssembler.DisposeNetworkHud();    // 网络质量件（归位——同属视图消费者）
            _hudAssembler.DisposePanel();         // HUD 面板（面板归零——视图消费者，先于准心拆）
            _hudAssembler.DisposeRespawnHud();    // 死亡/复活 HUD（帧事件消费者——先于分发器摘）
            _hudAssembler.DisposeCrosshair();     // 准心驱动（还系统光标——同属视图消费者）
            _viewAssembler.DisposeLaser();        // 激光驱动（束实例随视图根销毁；租约随作用域归还）
            _viewAssembler.DisposeProtectionFx(); // 保护壳驱动（实例同样随视图根销毁、租约随作用域归还）
            _hudAssembler.DisposeRoot();          // HUD 根件（ScreenSpace Canvas）+ 菜单监听
            _feedbackAssembler.DisposeDamageNumbers();   // 伤害数字（消费者先于分发器拆——实例全归还池）
            _feedbackAssembler.DisposeCombatFx();        // 枪口火光/命中特效（同上——消费者先于分发器拆）
            _feedbackAssembler.DisposeAuthority();       // 权威命中/死亡反馈（先摘订阅，再拆分发器/视图）
            _feedbackAssembler.DisposeAudio();           // 对局音效（消费者先于分发器/作用域拆；在播代理自然播完）
            _teleportReg?.Dispose();              // 传送命令处理器（随本局装配注销——下局重登记）
            _teleportReg = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _headshotDebug = null;                // 纯每帧重绘（无持有状态），随分发器一并走
            // 自动验证件：**先关闭**（还原设备源）再弃引用——否则输入源会停留在替身态直到下次进房
            _autoHeadshot?.Disable();
            _autoHeadshot = null;
#endif
            _feedbackAssembler.DisposeDispatcher();      // 命中分发（视图消费者——先于视图本体拆）
            _lookController.Dispose();                   // 视角源/瞄准点摘除 → 指针解锁 → 相机瞄准还原
            _context?.AttachInput(null);
            _context = null;
            _viewAssembler.Detach();                     // 视图实例随视图根销毁
            if (_viewRootGo != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_viewRootGo);
                else UnityEngine.Object.DestroyImmediate(_viewRootGo);
                _viewRootGo = null;
            }
            _viewRoot = null;
            _assets.ClearReferences();                   // 租约本性随 _viewScope 归还
            _viewScope.Dispose();
        }

        // ---- 装配细节 ----

        /// <summary>本地实体当前位置（传送出发位；未就绪 = null——处理器拒绝并留因）。</summary>
        private SimVector3? ReadLocalOrigin()
        {
            if (_context == null || _context.Sim == null || _context.LocalEntityId == 0) return null;
            if (!_context.Sim.State.TryResolve(_context.LocalEntityId, out int slot)) return null;
            return _context.Sim.State.Entities[slot].Pos;
        }

        /// <summary>传送分发单点：写宿主钩子（本地服宿主循环消费——T 键/GM 面板/撤销共用同一通道）。</summary>
        private static void DispatchTeleport(SimVector3 pos)
        {
            SimTestRules.TeleportTarget = pos;
            SimTestRules.TeleportDispatch = true;      // 交权威侧（本地服宿主）执行
        }

        /// <summary>
        /// 公共事实读源：**权威镜像优先**（`BattleContext.AuthorityState`，首份快照后可用），
        /// 首快照前回落本地预测态。排行榜/阵亡面板等公共展示只吃它——预测的击杀/死亡不得先上板
        /// （否则会出现"我这边死了/我这边加击杀，对方没死"的单边展示）。
        /// </summary>
        private SimWorldState PublicFactState()
            => _context != null
                ? (_context.AuthorityState ?? _context.Sim?.State)
                : null;
    }
}
