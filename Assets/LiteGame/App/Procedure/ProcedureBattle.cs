using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim;
using LiteSim.View;
using LiteSim.View.Animation;
using UnityEngine;
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 对局流程：
    /// OnEnter 建 <see cref="BattleContext"/>（Match Scope 全套）→ 会话建立后挂
    /// <see cref="SimView"/> 与输入服务（设备源）→ 每帧驱动；Ended（主动离场 / 会话失败）
    /// → 逆序收尾（视图 → BattleContext → Match Scope → Account Scope，§6.2 关闭序）→ 回 Main。
    ///
    /// 驱动形态：StageMachine 的 OnUpdate 即帧驱动入口（容器 Seal 后不可再注册 ITickable——
    /// 对局期驱动走阶段生命周期，天然随离场停止）。取消语义：阶段 CTS 链接宿主根（基类），
    /// 宿主关闭时等待 Ended 的异步被打断，finally 仍保证收尾（离场无 Match 残留的流程侧保证）。
    ///
    /// **视图所有权**：SimView 与玩家实体 prefab 的租约由本阶段持有（<see cref="IContentService"/> 注入）；
    /// 离场时先拆视图再拆上下文——视图是上下文的消费者，反向释放会读到已拆的 Sim。
    ///
    /// **输入所有权**：服务本身归装配根（跨对局复用、拦截源在装配根登记一次），
    /// 本阶段只持**本局的设备源**（相机是局内对象）并在离场时清派发状态；流程不持任何
    /// "UI 是否拦截"的判断——那是登记进服务的具名拦截源（`ui.modal`）。
    /// </summary>
    public sealed class ProcedureBattle : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>主动离场热键（测试入口）。</summary>
        private const UnityEngine.KeyCode LeaveKey = UnityEngine.KeyCode.F10;
        /// <summary>测试模式传送键（传送到当前准心点）。</summary>
        private const UnityEngine.KeyCode TeleportKey = UnityEngine.KeyCode.T;
#endif

        /// <summary>瞄准滞回的最小保持宽度——与 Brain DefaultBlend（EaseInOut 0.3s）对齐：
        /// 短按也走完一次完整混合，连点不重启混合（相机抖动的输入侧治理）。</summary>
        private const float AimMinHoldSeconds = 0.3f;

        /// <summary>
        /// 玩家实体 prefab（内容包路径；资源缺失时<see cref="AssetService"/> 侧**回退程序化灰盒**，
        /// 不把整局拖进错误态）。
        /// </summary>
        private const string EntityPrefab = SimView.DefaultEntityPrefab;

        /// <summary>对局准心的场景对象名与子路径（训练场 /Battle HUD/Crosshair/{Hip,Ads}——构建器单源）。</summary>
        private const string HudRootName = "Battle HUD";
        private const string CrosshairPath = "Crosshair";
        private const string HipName = "Hip";
        private const string AdsName = "Ads";

        /// <summary>瞄准激光束 prefab（内容包路径：Assets/FX 归置副本 fx_lazer_sight——KriptoFX Lazer 单源）。
        /// 加载失败/缺 LineRenderer → 静默降级（warn 一次，无激光不失败——同准心 HUD 缺失口径）。</summary>
        private const string LaserSightPrefab = "Assets/FX/fx_lazer_sight.prefab";

        private readonly IContentService _content;
        private readonly IVFXService _vfx;
        private readonly IInputService _input;       // 输入服务
        private readonly ICameraService _camera;     // 相机服务（Cinemachine 适配器；装配根建、跨对局复用）
        private readonly AimHoldGate _aimGate = new AimHoldGate(AimMinHoldSeconds);   // 瞄准态滞回：短按保完整摆动、连点不翻转
        private readonly bool _requireCamera;        // 缺相机 = 装配缺口（见 ctor 注释）

        private BattleContext _context;
        private ClientScope _account;
        private ClientScope _viewScope;          // 视图资源（prefab 租约/视图根）自有作用域
        private SimView _view;
        private CharacterLocomotionDriver _locomotion;   // 移动动画驱动（视图的消费者——先于视图拆除）
        private BattleCrosshairDriver _crosshair;       // 对局准心（战斗 HUD 第一件——场景 /Battle HUD，见 AttachCrosshair）
        private BattleLaserDriver _laser;               // 瞄准激光（准心的世界空间兄弟件——开火射线的可见投影）
        private GameObject _laserBeam;                 // 激光束实例（挂 _viewRoot，随离场拆；驱动只管显隐/端点）
        private AssetLease<GameObject> _laserLease;    // 激光束 prefab 租约（登记进 _viewScope，随作用域归还）
        private Func<Vector3?> _aimPointOf;             // 瞄准点来源（世界坐标；null = 无设备形态——相机保持场景构图）
        private Transform _viewRoot;
        private GameObject _viewRootGo;
        private GameObject _prefab;
        private AssetLease<GameObject> _prefabLease;

        /// <param name="input">输入服务（装配根注册的跨对局实例）。**流程不持"UI 是否拦截"这类判断**：
        /// 上下文门是登记进服务的具名拦截源（`ui.modal` 由装配根登记），流程只负责本局的设备源与
        /// 清派发状态（《角色状态与动作专项设计》§3"输入三件"）。null = 无输入服务（测试装配/无输入形态）。</param>
        /// <param name="camera">相机服务（Cinemachine 适配器，装配根建，**跨场景常驻**）。**相机构图归
        /// vcam 的场景配置**——流程只做一件事：每局开局 <see cref="ICameraService.Reset"/>（下一帧重新落位，
        /// 不从上局位置飞过来）。服务对象恒非 null；"这一刻有没有接上 vcam"由
        /// <paramref name="requireCamera"/> 在开打前裁决（见下）。</param>
        /// <param name="requireCamera">缺相机时是否拒绝对局。**装配根按场景是否配了虚拟相机决定**：
        /// 有 vcam 的包传 false（正常路径）；没配 vcam 的包传 true——让"对局跑得动但看不见"
        /// 这种最难的静默失效变成显性失败，而不是进了对局才发现画面纹丝不动。</param>
        public ProcedureBattle(IContentService content, IInputService input, ICameraService camera,
            IVFXService vfx = null, CancellationToken rootToken = default, bool requireCamera = false)
            : base(rootToken)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _input = input;
            _camera = camera;
            _vfx = vfx;
            _requireCamera = requireCamera;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, req.BattleClient, req.AccountScope, ct).Forget();   // 一行转发，仅此而已——禁止 async void

        /// <summary>async 方法不能带 <c>in</c> 参数（CS1988）——payload 在转发处取值，async 体只收所需字段。</summary>
        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, BattleClient battleClient,
            ClientScope accountScope, CancellationToken ct)
        {
            ProcedureId? next = null;
            ProcedureArgs nextArgs = default;
            try
            {
                _account = accountScope ?? throw new InvalidOperationException("Battle 阶段缺少 Account Scope（必须由 Match 移交）");
                // 相机缺失 = 装配缺口（场景没配 vcam / 进对局前没切到玩法场景）：
                // 不静默降级成"跑得动但看不见"的对局，在开打前显性失败。
                if (_requireCamera && !CameraReady())
                    throw new InvalidOperationException(
                        "对局相机未就绪：场景里没有接上的 CinemachineVirtualCamera"
                        + "（相机配置归场景——确认已切到配了 vcam 的玩法场景）");

                // 视图根 + 实体 prefab（缺失回退程序化灰盒——不把缺美术资源当启动失败）
                _viewScope = _account.CreateChild("BattleView");
                _viewRootGo = new GameObject("[BattleView]");
                if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_viewRootGo);
                _viewRoot = _viewRootGo.transform;
                await AcquireEntityPrefabAsync(ct);
                await AcquireLaserSightAsync(ct);

                _context = new BattleContext(battleClient, _account);
                UnityEngine.Debug.Log("[Battle] context-created (Match Scope built)");

                await WaitStartGame(battleClient, ct);        // Sim 就绪 = 视图可建（SimView 要读预测态）
                AttachView();

                BattleContext.EndReason reason = await WaitEnded(_context, ct);
                UnityEngine.Debug.Log($"[Battle] ended reason={reason} reconciles={_context.ReconcileCount}");
                next = ProcedureId.Main;              // 会话失败也回 Main（日志已留痕）
            }
            catch (OperationCanceledException)
            {
                // 宿主关闭/流程被打断：finally 收尾，不流转
            }
            catch (Exception ex)
            {
                Fail(m, ex, nameof(RunAsyncCore));
                next = ProcedureId.Error;
                nextArgs = new ProcedureArgs(ex);
            }
            finally
            {
                // 关闭序（§6.2）：输入接线 → 视图（含租约/实例）→ BattleContext（Match Scope 一并 Dispose）→ Account Scope
                DetachView();
                _context?.Dispose();
                _context = null;
                _account?.Dispose();
                _account = null;
            }

            if (next.HasValue) m.Request(next.Value, nextArgs);
        }

        /// <summary>等 StartGame（Reliable 信令；Sim 未建则视图无源）。已建（重连/重进）直接放行。</summary>
        private static async UniTask WaitStartGame(BattleClient battle, CancellationToken ct)
        {
            // **判据是"StartGame 是否已处理"，不是"快照是否已到"**：快照与 StartGame 是两条独立路径
            // （Unreliable vs Reliable），快照先到完全正常；拿 LastSnapshotFrame 当"已在局中"的代理，
            // 会让本方法提前返回、后面读空的 Sim。
            if (battle.Client.HasStartGame && battle.Client.StartGame != null) return;

            var ready = new UniTaskCompletionSource();
            void Handler(LiteNet.Proto.StartGame _) => ready.TrySetResult();
            battle.Client.OnStartGame += Handler;
            try
            {
                await ready.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                battle.Client.OnStartGame -= Handler;         // 临时订阅必须退——持久订阅归 BattleContext
            }
        }

        /// <summary>
        /// 取玩家实体 prefab（经内容服务租约，登记进视图作用域）。资源包未入库的克隆（第三方素材按
        /// 仓库政策不入 VCS）或加载失败时**回退程序化灰盒**——缺角色资源是可复现降级而非错误态。
        /// </summary>
        private async UniTask AcquireEntityPrefabAsync(CancellationToken ct)
        {
            try
            {
                _prefabLease = await _content.AcquireAsync<GameObject>(EntityPrefab, ct: ct);
                _viewScope.Register(_prefabLease);
                _prefab = _prefabLease.Asset;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                UnityEngine.Debug.Log($"[Battle] entity prefab 缺失，回退程序化灰盒:{EntityPrefab}（{ex.Message}）");
                _prefabLease = null;
                _prefab = GreyboxEntity.Make();
                _viewScope.Register(new DelegatedDisposable(() =>
                {
                    if (_prefab != null)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(_prefab);
                        else UnityEngine.Object.DestroyImmediate(_prefab);
                    }
                }));
            }
        }

        /// <summary>
        /// 取瞄准激光束 prefab 并实例化（经内容服务租约，登记进视图作用域；实例挂 _viewRoot 随离场拆）。
        /// 资源缺失/加载失败 → **静默降级**（只警告）：激光是表现增益，不把"缺特效资产"当对局失败
        /// （同角色 prefab 灰盒/准心 HUD 缺失口径）。
        /// </summary>
        private async UniTask AcquireLaserSightAsync(CancellationToken ct)
        {
            try
            {
                _laserLease = await _content.AcquireAsync<GameObject>(LaserSightPrefab, ct: ct);
                _viewScope.Register(_laserLease);
                _laserBeam = UnityEngine.Object.Instantiate(_laserLease.Asset, _viewRoot);
                _laserBeam.name = "LaserSight";
                _laserBeam.SetActive(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                UnityEngine.Debug.LogWarning($"[Battle] 激光束 prefab 缺失/加载失败，瞄准激光降级:{LaserSightPrefab}（{ex.Message}）");
                _laserLease = null;
                _laserBeam = null;
            }
        }

        /// <summary>
        /// 挂瞄准激光（<see cref="BattleLaserDriver"/>——端点判定与显隐归驱动；本方法只装配）。
        /// 束实例缺 <see cref="UnityEngine.LineRenderer"/>（资产被改）或 Sim 未建（装配序理论不达——
        /// <see cref="AttachView"/> 在 StartGame 之后）→ 警告并降级，不失败。
        /// </summary>
        private void AttachLaserSight()
        {
            if (_laserBeam == null) return;                      // 资产缺失已在取件处降级（警告过）
            if (_context.Sim == null)
            {
                UnityEngine.Debug.LogWarning("[Battle] Sim 未建——瞄准激光本局降级（AttachView 早于 StartGame 的装配序）");
                return;
            }
            var line = _laserBeam.GetComponent<UnityEngine.LineRenderer>();
            if (line == null)
            {
                UnityEngine.Debug.LogWarning($"[Battle] 激光束 prefab 缺 LineRenderer——瞄准激光降级:{LaserSightPrefab}");
                return;
            }
            _laser = new BattleLaserDriver(_view, _input, _context.Sim, _context.Map, line,
                aimPointOf: _aimPointOf);                       // 瞄准地面点注入（准心同源——端点压准心）
            UnityEngine.Debug.Log("[Battle] laser-attached");
        }

        /// <summary>
        /// 相机是否真的就绪（"有服务"不等于"接上了 vcam"——服务常驻，vcam 随场景）。
        /// **按需解析**：此刻视图还没建立、<see cref="ICameraService.Follow"/> 一次都没调过，
        /// 光读状态必然是"未接线"，所以这里主动让适配器再解析一次（场景可能刚被 Main 切到训练场）。
        /// 非 Cinemachine 实现（测试替身等）视为就绪——由它自己保证。
        /// </summary>
        private bool CameraReady()
        {
            if (_camera == null) return false;
            var cinemachine = _camera as CinemachineCameraService;
            return cinemachine != null ? cinemachine.EnsureCamera() : true;
        }

        /// <summary>
        /// 建视图并接线：SimView（镜像/插值/静默门/相机服务）。
        ///
        /// **相机不由本阶段创建**：装配根建好
        /// <see cref="ICameraService"/>（内含场景配置好的虚拟相机）并跨对局复用，本阶段只把端口
        /// 交给 SimView，并在开局 <see cref="ICameraService.Reset"/> 一次（镜头重新落位，
        /// 不从上局位置飞过来）。构图/档位/阻尼全归 vcam 的场景配置，代码里没有第二处事实源。
        /// </summary>
        private void AttachView()
        {
            _view = new SimView(_context.Sim.State, _viewRoot,
                factory: InstantiateView,
                recycler: null,                                   // 自有池（EntityService 接线归表现壳批）
                camera: _camera);
            _context.AttachView(_view);

            _locomotion = new CharacterLocomotionDriver(_view);   // 移动动画：视图速度 → 播放器 → Animator 后端（§7 更新次序的 Driver 段）

            AttachCrosshair();                                  // 对局准心（HUD 第一件——视图/输入均已就绪，场景对象按名解析）
            _aimPointOf = AimPointOf(_input);                   // 瞄准点（世界）→ 相机构图 z 偏移曲线 + 激光收敛端点（准心同源）
            AttachLaserSight();                                 // 瞄准激光（束实例已取，接线驱动——端点直落准心标记的地面点）
            AttachInput();
            UnityEngine.Debug.Log($"[Battle] view-attached prefab={EntityPrefab}");
        }

        /// <summary>
        /// 挂对局准心（战斗 HUD 第一件）：场景对象（训练场 <c>/Battle HUD</c>——
        /// 构建器确定性生成，AgentScripts/BuildBattleHud.cs）由本阶段按名解析；**缺失静默降级**
        /// （只警告）——HUD 是表现增益，不把"场景没配 HUD"当对局失败（同角色 prefab 灰盒降级口径）。
        /// 准心画布无 GraphicRaycaster 且 CanvasGroup 不拦射线——**永不参与输入**；上下文门被拦
        /// （模态 UI 开）时由驱动自己藏准心还系统光标。
        /// </summary>
        private void AttachCrosshair()
        {
            GameObject root = GameObject.Find(HudRootName);
            if (root == null)
            {
                UnityEngine.Debug.LogWarning($"[Battle] 场景缺 '{HudRootName}'——准心静默降级（玩法场景未配 HUD 对象）");
                return;
            }

            RectTransform plane = root.transform as RectTransform;
            RectTransform reticle = root.transform.Find(CrosshairPath) as RectTransform;
            if (plane == null || reticle == null)
            {
                UnityEngine.Debug.LogWarning($"[Battle] '{HudRootName}' 结构不符（Canvas 根缺 RectTransform 或缺 {CrosshairPath}）——准心静默降级");
                return;
            }

            _crosshair = new BattleCrosshairDriver(_view, _input, plane, reticle,
                hip: FindChildGameObject(root, CrosshairPath + "/" + HipName),
                ads: FindChildGameObject(root, CrosshairPath + "/" + AdsName),
                screenPosition: ScreenPositionOf(_input));           // 鼠标位来自设备源适配器（R12：InputSystem 只在边界内）
            UnityEngine.Debug.Log("[Battle] crosshair-attached");
        }

        /// <summary>
        /// 准心的鼠标位来源：设备源适配器的 <see cref="NewInputIntentSource.MouseScreenPosition"/>。
        /// 非该实现（测试替身/无设备形态）返回 null——驱动回退屏幕零点。**不在此 import InputSystem**
        /// （纪律 R12：适配器 import 只许在 Platform.Unity 边界目录内）。
        /// </summary>
        private static Func<Vector2> ScreenPositionOf(IInputService input)
        {
            return input != null && input.Source is NewInputIntentSource source
                ? () => source.MouseScreenPosition
                : null;
        }

        /// <summary>
        /// 瞄准点来源：设备源适配器解算的鼠标→地面交点（世界坐标；与准心共用同一解算结果）。
        /// 非该实现（测试替身/无设备形态）返回 null——相机保持场景构图。**不在此 import InputSystem**（R12）。
        /// </summary>
        private static Func<Vector3?> AimPointOf(IInputService input)
        {
            return input != null && input.Source is NewInputIntentSource source
                ? (Func<Vector3?>)(() => source.TryGetAimPoint(out Vector3 point) ? point : (Vector3?)null)
                : null;
        }

        /// <summary>按子路径取对象（形态组缺失 = 单形态退化——准心驱动容忍 null）。</summary>
        private static GameObject FindChildGameObject(GameObject root, string path)
        {
            Transform t = root.transform.Find(path);
            return t != null ? t.gameObject : null;
        }

        /// <summary>
        /// 本地玩家是否处于瞄准态（右键 ADS）——喂 <see cref="ICameraService.SetAiming"/> 的语义源。
        /// 读 <see cref="SimView.IsAiming"/>（本地预测态：连续位进 PredictedButtons，与本地手感同帧），
        /// 按实体 Id 解析槽位（同准心驱动的读法）；未对齐/视图未建 = false。
        /// </summary>
        private bool IsLocalAiming()
        {
            return _view != null && _view.LocalEntityId != 0
                && _view.TryGetSlot(_view.LocalEntityId, out int slot)
                && _view.IsAiming(slot);
        }

        /// <summary>
        /// 输入接线（§3 输入三件）：**设备源已在装配根装好**（<see cref="InputModule"/>），
        /// 本阶段只清上一局的派发状态并把服务交给对局上下文。
        /// **上下文门不在这里裁决**——拦截源在装配根按名登记（`ui.modal`），由服务采样时统一裁决
        /// 并报出是**谁**拦的。
        ///
        /// 设备源驻留装配根（不随对局 new/撤）的理由：Action 资产与订阅是进程级资源，
        /// 每局重建会重复付 Enable/资产解析的代价，且"对局结束时设备源被撤"会让根级的其他
        /// 消费者（未来的暂停菜单/调试面板）拿不到输入。对局进出只影响**意图是否被消费**。
        /// </summary>
        private void AttachInput()
        {
            if (_input == null) return;                           // 无服务（替身/测试装配）= 无输入形态
            _input.Reset();                                       // 上一局的待用意图/派发状态不带进本局

            // **对局相机注入**：设备源持有的是装配根 boot 相机——叠加开发形态下它
            // **不随场景销毁**，`Camera.main` 回落也拿错机（两台相机在场），瞄准/相机相对移动会
            // 全部算在错机上。这里把真对局渲染相机（brain 所在 Camera）显式换进去。
            if (_camera is CinemachineCameraService ccs && _input.Source is NewInputIntentSource source)
            {
                ccs.EnsureCamera();                                // 视图未建时 Follow 还没跑过——先解析 vcam
                Camera renderCamera = ccs.TryGetRenderCamera();
                if (renderCamera != null) source.SetAimCamera(renderCamera);
                else UnityEngine.Debug.LogWarning("[Battle] 对局渲染相机未解析到——瞄准/移动继续用设备源现持相机");
            }

            _context.AttachInput(_input);
            _camera?.Reset();                                     // 镜头重新落位（不从上一局位置飞过来）
        }

        private GameObject InstantiateView(string location, Transform parent)
        {
            if (_prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(_prefab, parent);
            go.name = location;
            return go;
        }

        private void DetachView()
        {
            if (_input != null) _input.Reset();   // 离场即清派发状态（设备源与拦截源都是装配根的，不在此摘）
            _locomotion?.Dispose();               // 先停动画驱动（视图消费者），再拆视图本体
            _locomotion = null;
            _crosshair?.Dispose();                // 准心驱动（还系统光标——同属视图消费者，先于视图本体拆）
            _crosshair = null;
            _laser?.Dispose();                    // 激光驱动（束实例随 _viewRoot 销毁；租约随 _viewScope.Dispose 归还）
            _laser = null;
            _laserBeam = null;
            _laserLease = null;
            _aimPointOf = null;                   // 瞄准点来源随本局设备源一起摘（方法组不跨局持有）
            _camera?.SetAiming(false);            // 瞄准机还原（优先级/Follow 归还场景值——离场不得遗留接管态）
            _context?.AttachInput(null);
            _view = null;                                         // 视图实例随 _viewRoot 销毁
            if (_viewRootGo != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_viewRootGo);
                else UnityEngine.Object.DestroyImmediate(_viewRootGo);
                _viewRootGo = null;
            }
            _viewRoot = null;
            _prefab = null;
            _prefabLease = null;                                  // 租约随 _viewScope.Dispose 归还
            _viewScope?.Dispose();
            _viewScope = null;
        }

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
            if (_context == null) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 主动离场热键（测试入口，与 ProcedureMain F9 进对局同门禁）：Leave → Ended(Leave) → 收尾回 Main。
            // 此处只留热键，不进正式 UI（正式离场入口由 UI 层提供）。
            if (UnityEngine.Input.GetKeyDown(LeaveKey)) _context.Leave();
            if (UnityEngine.Input.GetKeyDown(TeleportKey)) TestModeRuntime.TeleportRequested = true;   // T = 传送到准心（测试模式）
            if (TestModeRuntime.ExitRequested)                                                          // GM 面板退出：离场并关模式
            {
                TestModeRuntime.ExitRequested = false;
                TestModeRuntime.Active = false;
                _context.Leave();
            }
#endif

            // ① 渲染帧采样（上下文门在此裁决：登记源任一成立 → 本帧输入为空，见 IInputService）。
            //    瞄准参照原点 = **逻辑枪口**（Sim 预测态派生：Pos+Yaw 经 CombatConfig.MuzzleOrigin 单源——
            //    不读视图 Transform，平滑量不回灌输入）——瞄准向量 = 枪口 → 准心地面点方向，
            //    子弹（权威枪口同式出射）**过准心正上方**，消除本体锚点与枪口出射的平行差。
            //    瞄准相机：ADS 视角由 SetAiming 每帧喂本地预测态瞄准位，适配器抬场景 /Aim Camera
            //    优先级接管，跟随目标与主相机同源，不引用预制体参考点。
            if (_input != null && _context.Sim != null)
                _input.SampleOnRenderFrame(_context.LocalMuzzlePosition);

            _context.Tick(elapseSeconds);         // ② 网络双泵 + 输入上行 + 预测推进 + 表现视图（唯一驱动入口）
            _locomotion?.Tick(elapseSeconds);    // 移动动画：视图位置已更新，再解析目标姿态（视图的下游消费者）
            _crosshair?.Tick();                  // 对局准心（HUD）：位置/形态/光标——视图状态的最后一个消费者
            _laser?.Tick();                     // 瞄准激光（世界空间）：端点 = 开火射线（实体/障碍截停）——视图消费者
            _camera?.SetAiming(_aimGate.Feed(IsLocalAiming(), elapseSeconds)); // 瞄准相机接管（每帧幂等；适配器按沿生效——视图态已解析；滞回门控短按，防频繁点按来回重启混合）
            if (_camera != null)                  // 瞄准点（世界）→ 构图 z 偏移曲线（适配器按到焦点的前向投影换算）
            {
                Vector3? aimPoint = _aimPointOf?.Invoke();
                _camera.SetAimPoint(aimPoint ?? default, aimPoint.HasValue);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (TestModeRuntime.TeleportRequested)               // 测试模式传送：解析准心点 → 交权威侧（本地服）分发
            {
                TestModeRuntime.TeleportRequested = false;
                if (TestModeRuntime.Active && TestModeRuntime.TeleportEnabled
                    && _aimPointOf != null && _aimPointOf() is Vector3 target)
                {
                    SimTestRules.TeleportTarget = new SimVector3(target.x, target.y, target.z);
                    SimTestRules.TeleportDispatch = true;      // 交权威侧（本地服宿主）执行
                }
                else UnityEngine.Debug.LogWarning("[TestMode] 传送请求忽略（未开测试模式/未启用/无瞄准点）");
            }
#endif
        }

        /// <summary>等对局结束（Ended 恰好一次；ct 打断 = 宿主关闭路径）。</summary>
        private static async UniTask<BattleContext.EndReason> WaitEnded(BattleContext context, CancellationToken ct)
        {
            var ended = new UniTaskCompletionSource<BattleContext.EndReason>();
            void Handler(BattleContext.EndReason reason) => ended.TrySetResult(reason);
            context.Ended += Handler;
            try
            {
                return await ended.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                context.Ended -= Handler;         // 一次性等待：结束即退订
            }
        }
    }
}
