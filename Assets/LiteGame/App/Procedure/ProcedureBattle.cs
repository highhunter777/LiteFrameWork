using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim;
using LiteSim.View;
using LiteSim.View.Animation;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 对局流程（C2 批①：会话与对局上下文；批②：表现视图；2026-09-26 输入服务批：输入接线）：
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
    /// **输入所有权**（2026-09-26）：服务本身归装配根（跨对局复用、拦截源在装配根登记一次），
    /// 本阶段只持**本局的设备源**（相机是局内对象）并在离场时清派发状态；流程不再持任何
    /// "UI 是否拦截"的判断——那是登记进服务的具名拦截源（`ui.modal`）。
    /// </summary>
    public sealed class ProcedureBattle : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>主动离场热键（C2 测试入口；正式 UI 离场按钮归 U2/G3）。</summary>
        private const UnityEngine.KeyCode LeaveKey = UnityEngine.KeyCode.F10;
#endif

        /// <summary>
        /// 玩家实体 prefab（内容包路径；灰盒期该资源尚未入库——<see cref="AssetService"/> 侧缺失时
        /// **回退程序化灰盒**，不把整局拖进错误态）。正式角色资源随"地图提取/角色动画"前置批落地。
        /// </summary>
        private const string EntityPrefab = SimView.DefaultEntityPrefab;

        /// <summary>对局准心的场景对象名与子路径（训练场 /Battle HUD/Crosshair/{Hip,Ads}——构建器单源）。</summary>
        private const string HudRootName = "Battle HUD";
        private const string CrosshairPath = "Crosshair";
        private const string HipName = "Hip";
        private const string AdsName = "Ads";

        private readonly IContentService _content;
        private readonly IVFXService _vfx;
        private readonly IInputService _input;       // 输入服务（2026-09-26 输入服务批：取代裸 Func<bool> 上下文门）
        private readonly ICameraService _camera;     // 相机服务（Cinemachine 接入，2026-09-26；装配根建、跨对局复用）
        private readonly bool _requireCamera;        // 缺相机 = 装配缺口（见 ctor 注释）

        private BattleContext _context;
        private ClientScope _account;
        private ClientScope _viewScope;          // 视图资源（prefab 租约/视图根）自有作用域
        private SimView _view;
        private CharacterLocomotionDriver _locomotion;   // 移动动画驱动（视图的消费者——先于视图拆除）
        private BattleCrosshairDriver _crosshair;       // 对局准心（战斗 HUD 第一件——场景 /Battle HUD，见 AttachCrosshair）
        private Transform _viewRoot;
        private GameObject _viewRootGo;
        private GameObject _prefab;
        private AssetLease<GameObject> _prefabLease;

        /// <param name="input">输入服务（装配根注册的跨对局实例）。**流程不再持"UI 是否拦截"这类判断**：
        /// 上下文门是登记进服务的具名拦截源（`ui.modal` 由装配根登记），流程只负责本局的设备源与
        /// 清派发状态（《角色状态与动作专项设计》§3"输入三件"）。null = 无输入服务（测试装配/无输入形态）。</param>
        /// <param name="camera">相机服务（Cinemachine 适配器，装配根建，**跨场景常驻**）。**相机构图归
        /// vcam 的场景配置**——流程只做一件事：每局开局 <see cref="ICameraService.Reset"/>（下一帧重新落位，
        /// 不从上局位置飞过来）。服务对象恒非 null；"这一刻有没有接上 vcam"由
        /// <paramref name="requireCamera"/> 在开打前裁决（见下）。</param>
        /// <param name="requireCamera">开打前是否要求相机已接上 vcam。**装配根传 true**：
        /// 没接上的对局会"跑得动、看不见"，属最难查的静默失效——在开打前显性失败，
        /// 而不是静默降级成一台乱飞的相机。</param>
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

                _context = new BattleContext(battleClient, _account);
                UnityEngine.Debug.Log("[Battle] context-created (Match Scope built)");

                await WaitStartGame(battleClient, ct);        // Sim 就绪 = 视图可建（SimView 要读预测态）
                AttachView();

                BattleContext.EndReason reason = await WaitEnded(_context, ct);
                UnityEngine.Debug.Log($"[Battle] ended reason={reason} reconciles={_context.ReconcileCount}");
                next = ProcedureId.Main;              // 会话失败也回 Main（错误恢复 UI 归 G3；日志已留痕）
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
            // 会让本方法提前返回、后面读空的 Sim（实测就是这样崩的）。
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
        /// **相机不再由本阶段创建**（2026-09-26 Cinemachine 接入）：装配根建好
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

            _locomotion = new CharacterLocomotionDriver(_view);   // 移动动画首版：视图速度 → 播放器 → Animator 后端（§7 更新次序的 Driver 段）

            AttachCrosshair();                                  // 对局准心（HUD 第一件——视图/输入均已就绪，场景对象按名解析）
            AttachInput();
            UnityEngine.Debug.Log($"[Battle] view-attached prefab={EntityPrefab}");
        }

        /// <summary>
        /// 挂对局准心（战斗 HUD 第一件，2026-10-02）：场景对象（训练场 <c>/Battle HUD</c>——
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

        /// <summary>按子路径取对象（形态组缺失 = 单形态退化——准心驱动容忍 null）。</summary>
        private static GameObject FindChildGameObject(GameObject root, string path)
        {
            Transform t = root.transform.Find(path);
            return t != null ? t.gameObject : null;
        }

        /// <summary>
        /// 输入接线（§3 输入三件）：**设备源已在装配根装好**（<see cref="InputModule"/>，
        /// 2026-09-26 New Input System 接入），本阶段只清上一局的派发状态并把服务交给对局上下文。
        /// **上下文门不在这里裁决**——拦截源在装配根按名登记（`ui.modal`），由服务采样时统一裁决
        /// 并报出是**谁**拦的。
        ///
        /// 设备源驻留装配根（不再随对局 new/撤）的理由：Action 资产与订阅是进程级资源，
        /// 每局重建会重复付 Enable/资产解析的代价，且"对局结束时设备源被撤"会让根级的其他
        /// 消费者（未来的暂停菜单/调试面板）拿不到输入。对局进出只影响**意图是否被消费**。
        /// </summary>
        private void AttachInput()
        {
            if (_input == null) return;                           // 无服务（替身/测试装配）= 无输入形态
            _input.Reset();                                       // 上一局的待用意图/派发状态不带进本局

            // **对局相机注入**（2026-09-27）：设备源持有的是装配根 boot 相机——叠加开发形态下它
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
            // 正式离场入口（UI 按钮/结算）归 U2/G3——此处只留热键，不进正式 UI。
            if (UnityEngine.Input.GetKeyDown(LeaveKey)) _context.Leave();
#endif

            // ① 渲染帧采样（上下文门在此裁决：登记源任一成立 → 本帧不产生新输入，见 IInputService）。
            //    瞄准参照原点取自 Sim 预测态——不读视图 Transform（平滑过的表现量会把误差回灌进输入）。
            //    2026-09-27 分镜裁决：AimPoint 不进输入/主相机，留给瞄准相机（瞄准态接线时用
            //    SimView.LocalAimPointPosition）。
            if (_input != null && _context.Sim != null)
                _input.SampleOnRenderFrame(_context.LocalPosition);

            _context.Tick(elapseSeconds);         // ② 网络双泵 + 输入上行 + 预测推进 + 表现视图（唯一驱动入口）
            _locomotion?.Tick(elapseSeconds);    // 移动动画：视图位置已更新，再解析目标姿态（视图的下游消费者）
            _crosshair?.Tick();                  // 对局准心（HUD）：位置/形态/光标——视图状态的最后一个消费者
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
