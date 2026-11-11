using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.Services.UnityObjects;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;
using LiteFramework.UI;
using LiteGame;
using LiteGame.Modules.Battle;
using LiteGame.Modules.Battle.SubModules.ArenaSubModule.RootMotion;
using LiteGame.Modules.Battle.Shared;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Input;
using LiteGame.Modules.Settings;
using LiteGame.Modules.Battle.Abstractions.View;
using LiteGame.Modules.Battle.SubModules.AimSubModule;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Abstractions;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Feedback;
using LiteGame.Modules.Battle.SubModules.CommandsSubModule;
using LiteGame.Modules.Battle.SubModules.HudSubModule.Abstractions;
using LiteGame.Modules.Battle.SubModules.HudSubModule.Crosshair;
using LiteGame.Modules.Battle.SubModules.HudSubModule.Model;

namespace LiteGame.Modules.Battle.SubModules.SetupSubModule
{
    /// <summary>
    /// 对局视图装配件（ProcedureBattle 的视图域，一处收口全部表现件接线）。
    /// </summary>
    internal sealed class BattleViewRig : IBattleViewGroup, IBattleViewAttachment
    {
        private readonly IContentService _content;   // 内容服务（grip.bin 数据产物取件）
        private readonly IInputService _input;       // 输入服务（设备源注入/清派发状态）
        private readonly ICameraService _camera;     // 相机服务（Cinemachine 适配器；装配根建、跨对局复用）
        private readonly CommandBus _commands;    // 命令总线（传送命令化：撤销/重做/排队派发）
        private readonly LiteGame.IDevSession _dev;  // 开发会话端口（激光/飘字开关；可空 = 测试装配）
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
        private readonly Action<SimVector3> _teleportDispatch;   // 传送分发（产品侧注入本地服宿主钩子）
        private IBattleDevOverlay _devOverlay;             // 开发叠加层（爆头绘制/自瞄验证；开发装配提供，发布 = null）
        private WeaponGripCatalog _gripCatalog;            // 持枪数据（grip.bin 装载；null = 降级）
        private readonly RootMotionDigestHolder _rootMotionDigest;   // 根位移摘要登记口（容器装配期注册；null = 测试装配）
        private readonly IAnimationBackendFactory _backendFactory;    // 动画后端工厂（装配根注入；null = 驱动降级）
        private readonly BattleStrategyProfile _strategy;             // 策略表投影（HUD 排行/伤害数字参数）
        private RootMotionCatalog _rootMotionCatalog;                // 根位移数据（rootmotion.bin 装载；对局必备）

        /// <param name="account">Account 作用域（本件在其下建 `BattleView` 子作用域——离场即整体回收）。</param>
        /// <param name="worldClock">世界钟（伤害数字寿命/合并窗；null = 飘字降级）。</param>
        internal BattleViewRig(IContentService content, IInputService input, ICameraService camera,
            ClientScope account, IVfxService vfx = null, LiteFramework.IWorldClock worldClock = null,
            AudioService audio = null, CommandBus commands = null, CrosshairFeelProfile crosshairFeel = null,
            Func<UIThemeAsset> themeProvider = null, LiteGame.IDevSession dev = null,
            IBattleDevOverlay devOverlay = null, Action<SimVector3> teleportDispatch = null,
            ILookPreferences lookPreferences = null, RootMotionDigestHolder rootMotionDigest = null,
            IAnimationBackendFactory animationBackendFactory = null, BattleStrategyProfile? strategy = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (account == null) throw new ArgumentNullException(nameof(account));
            _content = content;
            _input = input;
            _camera = camera;
            _commands = commands;
            _dev = dev;
            _devOverlay = devOverlay;
            _teleportDispatch = teleportDispatch;
            _rootMotionDigest = rootMotionDigest;
            _backendFactory = animationBackendFactory;
            _strategy = strategy ?? BattleStrategyProfile.Default;

            _viewScope = account.CreateChild("BattleView");
            _viewRootGo = new GameObject("[BattleView]");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_viewRootGo);
            _viewRoot = _viewRootGo.transform;

            _assets = new BattleViewAssets(content, _viewScope, _viewRoot, themeProvider);
            _hudAssembler = new BattleHudAssembler(crosshairFeel, _strategy);
            _feedbackAssembler = new BattleFeedbackAssembler(vfx, audio, worldClock, camera, _assets, _viewRoot, dev,
                _strategy);
            _lookController = new BattleLookController(input, camera, lookPreferences);   // 视角偏好端口（可空 = 纯机制常量）
        }

        // ---- 诊断观测面（**仅测试/诊断用**：冒烟用例核对"视图组已建/挂载点/准心"；产品路径不读）----。

        /// <summary>视图是否已建并接线（<see cref="Attach"/> 之后 = true，<see cref="Dispose"/> 之后 = false）。</summary>
        internal bool IsBuilt => _context != null && _viewAssembler.View != null;

        /// <summary>HUD 族装配器（观测面：根件/准心）。</summary>
        internal BattleHudAssembler Hud => _hudAssembler;

        /// <summary>视图装配件（观测面：本地实体/视图实例）。</summary>
        internal BattleViewSceneAssembler View => _viewAssembler;

        /// <summary>战斗反馈装配件（观测面：伤害数字在册数）。</summary>
        internal BattleFeedbackAssembler Feedback => _feedbackAssembler;

        /// <summary>根位移目录（观测面：装载结果/摘要；动作位移执行层接线前无生产消费者）。</summary>
        internal RootMotionCatalog RootMotion => _rootMotionCatalog;

        // ---- 取件（资源租约；逐项降级）----。

        /// <summary>取全部对局表现资源（角色 prefab → 激光束 → HUD → 保护壳 → 飘字底件 → 逐档材质 → 片段清单 → 音效 clip → grip.bin）。
        /// 逐项表现资源失败只降级该项（缺角色资源＝无角色视图），不把缺美术当对局失败；
        /// **最后取 rootmotion.bin（内容包必备件）——缺失/损坏即失败**。</summary>
        public async UniTask LoadAsync(CancellationToken ct)
        {
            await _assets.LoadAsync(ct);
            _gripCatalog = await AcquireGripCatalogAsync(ct);
            _rootMotionCatalog = await AcquireRootMotionAsync(ct);
        }

        /// <summary>取 rootmotion.bin（内容通道）并装载为根位移目录，同时登记内容摘要（fail-fast）：
        /// 根位移是权威位移的输入，缺文件/损坏＝对局不受支持（抛 <see cref="InvalidOperationException"/>，
        /// 由流程层按对局失败收尾）——**不像 grip 那样降级**。
        /// `bin` 资产在导入层以 Base64 文本承载（见 BinTextAssetImporter）。</summary>
        private async UniTask<RootMotionCatalog> AcquireRootMotionAsync(CancellationToken ct)
        {
            try
            {
                AssetLease<TextAsset> lease = await _content.AcquireAsync<TextAsset>(
                    RootMotionCatalog.DefaultAssetPath, ct: ct);
                try
                {
                    byte[] bytes = Convert.FromBase64String(lease.Asset.text);
                    RootMotionCatalog catalog = RootMotionCatalog.Load(bytes);
                    _rootMotionDigest?.Set(catalog.Digest);   // 首个对局装载成功前摘要为空串（身份面可读）
                    Log.Info("root-motion loaded entries=" + catalog.Entries.Count
                        + " tickRate=" + catalog.TickRate + " digest=" + catalog.Digest, LogTag.Battle);
                    return catalog;
                }
                finally
                {
                    lease.Dispose();                          // 字节已复制，租约立即归还
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw new InvalidOperationException(
                    "根位移数据缺失/损坏（内容包必备件，对局不可继续）：" + RootMotionCatalog.DefaultAssetPath, ex);
            }
        }

        /// <summary>取 grip.bin（内容通道）并装载为持枪数据目录：缺文件/损坏 = 警告 + 持枪约束降级
        /// （纯表现数据，不阻断对局）。`bin` 资产在导入层以 Base64 文本承载（见 BinTextAssetImporter）。</summary>
        private async UniTask<WeaponGripCatalog> AcquireGripCatalogAsync(CancellationToken ct)
        {
            try
            {
                AssetLease<TextAsset> lease = await _content.AcquireAsync<TextAsset>(
                    WeaponGripCatalog.DefaultAssetPath, ct: ct);
                try
                {
                    byte[] bytes = Convert.FromBase64String(lease.Asset.text);
                    WeaponGripCatalog catalog = WeaponGripCatalog.Load(bytes);
                    Log.Info("grip-catalog loaded digest=" + catalog.Digest, LogTag.Battle);
                    return catalog;
                }
                finally
                {
                    lease.Dispose();                          // 字节已复制，租约立即归还
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warning("持枪偏移数据缺失/损坏，持枪约束降级（换弹期枪由片段曲线驱动）："
                    + WeaponGripCatalog.DefaultAssetPath + "（" + ex.Message + "）", LogTag.Battle);
                return null;
            }
        }

        // ---- 装配（建视图 + 接线；须在 StartGame 之后调用）----。

        /// <summary>
        /// 建视图并接线：SimView（镜像/插值/静默门/相机服务）→ 移动动画驱动 → HUD 族 → 瞄准激光/保护壳 → 命中分发与反馈消费者 →〔编辑器〕爆头诊断件 → 传送命令登记。
        /// </summary>
        public void Attach(BattleContext context, Action openBattleMenu = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            UnityEngine.Events.UnityAction menuClick =
                openBattleMenu != null ? new UnityEngine.Events.UnityAction(openBattleMenu) : null;

            _viewAssembler.AttachCore(_context, _viewRoot, _assets.Prefab, _camera,
                _assets.ClipManifestLease?.Asset, _backendFactory, aimPointOf: () => _lookController.AimPoint,
                gripCatalog: _gripCatalog);

            HitFeedbackDispatcher dispatcher = _feedbackAssembler.CreateDispatcher(_viewAssembler.View);
            // 读模型在装配点构造（唯一翻译点是 BattleReadModel）：HUD 族只接读模型实例，不认 Sim 布局。
            Func<long> localEntityIdOf = () => _viewAssembler.View != null ? _viewAssembler.View.LocalEntityId : 0L;
            var predictedFacts = new BattleReadModel(() => _context.Sim.State, localEntityIdOf);
            var publicFacts = new BattleReadModel(PublicFactState, localEntityIdOf);
            var respawnFacts = new BattleReadModel(PublicFactState, localEntityIdOf, _viewAssembler.IsLocalDead);
            var hudModel = new BattleHudModel(predictedFacts, publicFacts, respawnFacts, localEntityIdOf,
                () => _context.NetworkQuality, _context.CombatFeed);
            _hudAssembler.Attach(_context, _viewAssembler.View, _input,
                _assets.HudView, _assets.HudInstance, menuClick, dispatcher, hudModel);

            _viewAssembler.AttachLaser(_context, _input, _assets.LaserBeam, _dev);
            _viewAssembler.AttachProtectionFx(_assets.ProtectionShellLease);
            _hudAssembler.AttachRespawnHud(dispatcher);
            _lookController.Attach(_viewAssembler.View);
            _feedbackAssembler.AttachConsumers(_context, _viewAssembler.View);
            // 开发叠加层（爆头区域常驻可视化 + 爆头自动验证件）：由 Dev 装配实现，发布形态 null = 零开销。
            _devOverlay?.Attach(_context, _input, consumer => _feedbackAssembler.Register(consumer));
            AttachInputWiring();
            if (_commands != null && _teleportDispatch != null)
            {
                _teleportReg = _commands.RegisterUndoable(new TeleportToAimHandler(
                    aimOf: () => _lookController.AimPoint,
                    originOf: ReadLocalOrigin,
                    dispatch: _teleportDispatch));                     // 传送命令化：撤销/重做随命令总线可用
            }
            Log.Info($"view-attached prefab={BattleViewAssets.EntityPrefab}", LogTag.Battle);
        }

        /// <summary>每帧驱动视图消费者（<see cref="BattleContext.Tick"/> 之后调用——权威已应用）：
        /// 移动动画 → 权威反馈 → HUD 族 → 世界表现件 → 反馈消费者 → 视角/指针（相位顺序固定）。</summary>
        public void Tick(float elapseSeconds)
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
        public void TickPreSample()
        {
            _devOverlay?.TickPreSample();        // 爆头绘制 + 自瞄验证（发布无叠加层 = 空调用）
        }

        /// <summary>
        /// 输入接线（§3 输入三件）：**设备源已在装配根装好**（InputSegment）， 本件只处理"对局相机注入 + 把服务交给对局上下文 + 镜头落位"。
        /// </summary>
        private void AttachInputWiring()
        {
            if (_input == null) return;                           // 无服务（替身/测试装配）= 无输入形态

            // **对局相机注入**：设备源持有的是装配根 boot 相机——叠加开发形态下它 **不随场景销毁**，`Camera.main` 回落也拿错机（两台相机在场），瞄准/相机相对移动会 全部算在错机上。
            if (_camera is IRenderCameraSource cameraSource && _input.Source is IAimCameraSink aimCamera)
            {
                cameraSource.EnsureCamera();                       // 视图未建时 Follow 还没跑过——先解析 vcam
                UnityEngine.Camera renderCamera = cameraSource.TryGetRenderCamera();
                if (renderCamera != null) aimCamera.SetAimCamera(renderCamera);
                else Log.Warning("对局渲染相机未解析到——瞄准/移动继续用设备源现持相机", LogTag.Battle);
            }

            _context.AttachInput(_input);
            _camera?.Reset();                                     // 镜头重新落位（不从上一局位置飞过来）
        }

        /// <summary>拆视图（按相位逆序：消费者 → 分发器 → 视图 → 根件 → 作用域）。不触发对局结束——那是流程层的裁决。</summary>
        public void Dispose()
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
            _devOverlay?.Detach();                // 先关闭自瞄（还原设备源）再弃引用
            _devOverlay = null;
            _feedbackAssembler.DisposeDispatcher();      // 命中分发（视图消费者——先于视图本体拆）
            _lookController.Dispose();                   // 视角源/瞄准点摘除 → 指针解锁 → 相机瞄准还原
            _context?.AttachInput(null);
            _context = null;
            _viewAssembler.Detach();                     // 视图实例随视图根销毁
            if (_viewRootGo != null)
            {
                UnityObjects.Destroy(_viewRootGo);
                _viewRootGo = null;
            }
            _viewRoot = null;
            _assets.ClearReferences();                   // 租约本性随 _viewScope 归还
            _viewScope.Dispose();
            _gripCatalog = null;
            _rootMotionCatalog = null;                   // 目录是纯数据（无租约）；置空防跨局误读
        }

        // ---- 装配细节 ----。

        /// <summary>本地实体当前位置（传送出发位；未就绪 = null——处理器拒绝并留因）。</summary>
        private SimVector3? ReadLocalOrigin()
        {
            if (_context == null || _context.Sim == null || _context.LocalEntityId == 0) return null;
            if (!_context.Sim.State.TryResolve(_context.LocalEntityId, out int slot)) return null;
            return _context.Sim.State.Entities[slot].Pos;
        }

        /// <summary>
        /// 公共事实读源：**权威镜像优先**（`BattleContext.AuthorityState`，首份快照后可用）， 首快照前回落本地预测态。
        /// </summary>
        private SimWorldState PublicFactState()
            => _context != null
                ? (_context.AuthorityState ?? _context.Sim?.State)
                : null;
    }
}
