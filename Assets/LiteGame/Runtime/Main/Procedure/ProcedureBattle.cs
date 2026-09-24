using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim;
using LiteSim.View;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 对局流程（C2 批①：会话与对局上下文；批②：表现视图与输入接线）：
    /// OnEnter 建 <see cref="BattleContext"/>（Match Scope 全套）→ 会话建立后挂
    /// <see cref="SimView"/> 与 <see cref="PlayerController"/> → 每帧驱动；Ended（主动离场 / 会话失败）
    /// → 逆序收尾（视图 → BattleContext → Match Scope → Account Scope，§6.2 关闭序）→ 回 Main。
    ///
    /// 驱动形态：StageMachine 的 OnUpdate 即帧驱动入口（容器 Seal 后不可再注册 ITickable——
    /// 对局期驱动走阶段生命周期，天然随离场停止）。取消语义：阶段 CTS 链接宿主根（基类），
    /// 宿主关闭时等待 Ended 的异步被打断，finally 仍保证收尾（离场无 Match 残留的流程侧保证）。
    ///
    /// **视图所有权**：SimView 与玩家实体 prefab 的租约由本阶段持有（<see cref="IContentService"/> 注入）；
    /// 离场时先拆视图再拆上下文——视图是上下文的消费者，反向释放会读到已拆的 Sim。
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

        private readonly IContentService _content;
        private readonly IVFXService _vfx;

        private BattleContext _context;
        private ClientScope _account;
        private ClientScope _viewScope;          // 视图资源（prefab 租约/视图根）自有作用域
        private SimView _view;
        private PlayerController _input;
        private Transform _viewRoot;
        private GameObject _viewRootGo;
        private GameObject _prefab;
        private AssetLease<GameObject> _prefabLease;

        public ProcedureBattle(IContentService content, IVFXService vfx = null, CancellationToken rootToken = default)
            : base(rootToken)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _vfx = vfx;
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
                // 关闭序（§6.2）：视图（含租约/实例）→ BattleContext（Match Scope 一并 Dispose）→ Account Scope
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
            if (battle.Client.LastSnapshotFrame >= 0) return;   // 已在局中（幂等：不重复等）

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
        /// 取玩家实体 prefab（经内容服务租约，登记进视图作用域）。内容包暂无该资源时**回退灰盒**——
        /// 灰盒期美术资源未入库属正常，不让整局进错误态；正式角色资源随前置批落地后此分支自然消失。
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
        /// 建视图并接线：SimView（镜像/插值/静默门/相机）+ PlayerController（输入采集，上下文门接 UI 输入锁）。
        /// </summary>
        private void AttachView()
        {
            Camera cam = Camera.main;
            var rig = new BattleCameraRig(ResolveCameraTransform(cam), BattleCameraRig.Rig.Default);
            if (cam != null) rig.ApplyLens(cam);

            _view = new SimView(_context.Sim.State, _viewRoot,
                factory: InstantiateView,
                recycler: null,                                   // 自有池（EntityService 接线归表现壳批）
                camera: rig);
            _context.AttachView(_view);

            _input = new PlayerController(cam);
            _input.SetGate(() => !IsUiBlocking);                  // 上下文门：UI 打开 → 意图全零

            _context.AttachInput(CollectInput);
            UnityEngine.Debug.Log($"[Battle] view-attached prefab={EntityPrefab}");
        }

        /// <summary>无相机时自建一台（对局必须有可跟随的相机；场景相机会被优先复用）。</summary>
        private Transform ResolveCameraTransform(Camera sceneCamera)
        {
            if (sceneCamera != null) return sceneCamera.transform;
            var go = new GameObject("[BattleCamera]");
            var created = go.AddComponent<Camera>();
            created.fieldOfView = BattleCameraRig.Rig.Default.FieldOfView;
            return go.transform;
        }

        /// <summary>UI 是否正在拦截游戏输入（上下文门数据源；U2 导航/模态栈接入后细化）。</summary>
        private bool IsUiBlocking { get; set; }

        private GameObject InstantiateView(string location, Transform parent)
        {
            if (_prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(_prefab, parent);
            go.name = location;
            return go;
        }

        /// <summary>输入采集（BattleContext 的 inputProvider）：本地位置取自 Sim 预测态——
        /// 不读视图 Transform（平滑过的表现量会把误差回灌进输入）。</summary>
        private SimInputFrame CollectInput()
        {
            if (_input == null || _context == null || _context.Sim == null) return default;
            SimVector3 origin = _context.LocalPosition;
            return _input.Collect(origin);
        }

        private void DetachView()
        {
            _context?.AttachInput(null);
            _input = null;
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

            _context.Tick(elapseSeconds);         // 网络双泵 + 输入上行 + 预测推进 + 表现视图（唯一驱动入口）
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
