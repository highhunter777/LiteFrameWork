using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient.UI;
using LiteSim;
using LiteView;
using LiteClient;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 对局流程（**只做编排**）：
    /// OnEnter 建 <see cref="BattleContext"/>（Match Scope 全套）→ 会话建立后由
    /// <see cref="BattleViewRig"/> 取件并挂视图/输入接线 → 每帧驱动（上下文 + 视图消费者）；
    /// Ended（主动离场 / 会话失败）→ 逆序收尾（视图 → BattleContext → Match Scope → Account Scope，
    /// §6.2 关闭序）→ 回 Main。
    ///
    /// **对局场景组（Rig ＋ 地图）装载与卸载都在本阶段**（加载链修订）：StartGame 到达后加载
    /// （<see cref="LoadBattleScenesAsync"/>，全程进"正在进入对局…"遮罩），离场收尾卸载
    /// （<see cref="UnloadBattleScenes"/>）——避免"未开局即付费"与跨阶段所有权分叉。
    ///
    /// **视图域全部归 <see cref="BattleViewRig"/>**（资源租约 / SimView / 七个表现件装配 / 每帧驱动 /
    /// 逆序拆除）：本类只保留"取会话 → 等信令 → 每帧驱动 → 收尾"的编排与对局级裁决（相机就绪门、
    /// 测试模式热键、瞄准世界注入），不再逐个持有表现件。
    ///
    /// 驱动形态：StageMachine 的 OnUpdate 即帧驱动入口（容器 Seal 后不可再注册 ITickable——
    /// 对局期驱动走阶段生命周期，天然随离场停止）。取消语义：阶段 CTS 链接宿主根（基类），
    /// 宿主关闭时等待 Ended 的异步被打断，finally 仍保证收尾（离场无 Match 残留的流程侧保证）；
    /// **仅宿主关闭**的收尾不含场景卸载（该路径 Scene 行退出动作＝释放租约，见 <see cref="UnloadBattleScenes"/>），
    /// 主动离场/被外部打断照卸。
    ///
    /// **输入所有权**：服务本身归装配根（跨对局复用、拦截源在装配根登记一次），
    /// 本阶段只持**本局的设备源**（相机是局内对象）并在离场时清派发状态；流程不持任何
    /// "UI 是否拦截"的判断——那是登记进服务的具名拦截源（`ui.modal`）。
    /// </summary>
    public sealed class ProcedureBattle : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>主动离场热键（测试入口）。</summary>
        /// <summary>测试模式传送键（传送到当前准心点）。</summary>
        private const UnityEngine.KeyCode TeleportKey = UnityEngine.KeyCode.T;
#endif

        private readonly IContentService _content;
        private readonly IVFXService _vfx;
        private readonly AudioService _audio;        // 音频服务（装配根注册；对局音效的组+代理后端）
        private readonly CommandCenter _commands;    // 命令中心（传送命令化：撤销/重做/排队派发）
        private readonly IInputService _input;       // 输入服务
        private readonly ICameraService _camera;     // 相机服务（Cinemachine 适配器；装配根建、跨对局复用）
        private readonly bool _requireCamera;        // 缺相机 = 装配缺口（见 ctor 注释）
        private readonly LiteFramework.IWorldClock _worldClock;   // 世界钟（伤害数字寿命/合并窗）
        private readonly IMatchSessionFactory _matchSessions;
        private readonly PageFlow _pages;            // 五页页面流（批C-④；可空 = 测试装配不接 UI）
        private readonly ISceneService _scenes;      // 对局场景组加载与卸载（可空 = 测试装配）
        private readonly IMapProvider _maps;         // 地图表（多图批：场景路径查表行 scene 列／离场卸载按表枚举）
        private readonly FeedbackService _feedback;  // 开局入场加载遮罩（可空 = 测试装配不接 UI）
        private readonly CrosshairFeelBook _crosshairFeel;   // 准心手感投影（tbcrosshair；可空 = 测试装配回落 Default）

        private BattleViewRig _rig;                  // 视图域（资源租约 / 视图与消费者 / 逆序拆除）
        private BattleContext _context;
        private MatchSession _matchSession;
        private ClientScope _account;

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
            IVFXService vfx = null, CancellationToken rootToken = default, bool requireCamera = false,
            LiteFramework.IWorldClock worldClock = null, IMatchSessionFactory matchSessions = null,
            AudioService audio = null, CommandCenter commands = null, PageFlow pages = null,
            CrosshairFeelBook crosshairFeel = null, ISceneService scenes = null, IMapProvider maps = null,
            FeedbackService feedback = null)
            : base(rootToken)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _input = input;
            _camera = camera;
            _vfx = vfx;
            _audio = audio;
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _requireCamera = requireCamera;
            _worldClock = worldClock;
            _matchSessions = matchSessions ?? throw new ArgumentNullException(nameof(matchSessions));
            _pages = pages;
            _scenes = scenes;
            _maps = maps;
            _feedback = feedback;
            _crosshairFeel = crosshairFeel;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, req.AccountSession, ct).Forget();   // 一行转发，仅此而已——禁止 async void

        /// <summary>async 方法不能带 <c>in</c> 参数（CS1988）——payload 在转发处取值，async 体只收所需字段。</summary>
        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, AccountSession accountSession,
            CancellationToken ct)
        {
            ProcedureId? next = null;
            ProcedureArgs nextArgs = default;
            bool completed = false;                                       // 正常结束（MatchEnded 冻结结果在）——结算页判据
            LiteNet.Proto.MatchEnded matchResult = null;
            int localPlayerId = -1;
            try
            {
                accountSession = accountSession ?? throw new InvalidOperationException("Battle 阶段缺少 AccountSession（必须由 Match 移交）");
                _account = accountSession.Scope;
                BattleClient battleClient = accountSession.Battle;

                // 视图域：自有 BattleView 作用域 + 视图根 + 六个取件（缺角色资源回退程序化灰盒）
                _rig = new BattleViewRig(_content, _input, _camera, _account, _vfx, _worldClock, _audio, _commands,
                    crosshairFeel: _crosshairFeel);   // 准心手感投影（可空 = 测试装配）
                await _rig.LoadAsync(ct);

                _matchSession = _matchSessions?.Create(accountSession)
                    ?? throw new InvalidOperationException("缺少 MatchSessionFactory（完整 DI 装配要求由工厂创建 Match 域）");
                _context = _matchSession.Context;
                RoomBridge.BindSession(new RoomSessionFace(battleClient));   // 房间会话门面（房主开局批）——页面经 Bridge.room 读房主事实/发提前开局；收尾必摘
                Log.Info("context-created (Match Scope built)", LogTag.Battle);

                bool started = await WaitStartGameOrEnded(battleClient, _context, ct);   // Sim 就绪 = 视图可建（SimView 要读预测态）
                if (!started)
                {
                    // 未开局即散场（等待超时/运维关闭/排空——MatchEnded 先于 StartGame 到达）：
                    // 跳过视图建立，经下方 WaitEnded 的 earlyResult 立即收尾（回 Main；房间页由页面流替换回大厅）。
                    Log.Info("room aborted before start (MatchEnded arrived before StartGame)", LogTag.Battle);
                }
                else if (_context.Sim != null)
                {
                    // 开局入场（加载链修订）：**对局场景组在本阶段、StartGame 之后加载**（Rig ＋ 地图，行键取
                    // JoinAck 权威事实）——本阶段独有装载与卸载（不在 Match 载、避免"未开局即付费"与跨阶段
                    // 所有权分叉）。全程进加载遮罩，视图建成才收：玩家从加载页直入对局画面。
                    FeedbackService.LoadingScope entry = _feedback?.BeginLoading("正在进入对局…") ?? default;
                    try
                    {
                        await LoadBattleScenesAsync(battleClient.Client.MapId, ct);
                        // 相机缺失 = 装配缺口（场景没配 vcam / 没切到玩法场景）：不静默降级成"跑得动但看不见"
                        // 的对局，在开打前显性失败——vcam 随 Rig 场景，故硬门必须在场景就位之后裁决。
                        if (_requireCamera && !CameraReady())
                            throw new InvalidOperationException(
                                "对局相机未就绪：场景里没有接上的 CinemachineVirtualCamera"
                                + "（相机配置归场景——确认已切到配了 vcam 的玩法场景）");
                        _rig.Attach(_context, openBattleMenu: () => _pages?.OpenBattleSettings());
                        _pages?.EnableBattleMenuBack();       // 对局中"ESC＝菜单"（与 HUD 菜单钮同源）
                    }
                    finally { entry.Dispose(); }              // 视图建成才收遮罩；失败路径遮罩一并收（异常走 Error）

                    // **先收加载系统面，再关房间页**：反序（先关 Bottom 房间页、后关 System 加载面）会让
                    // 房间页关闭卡在被加载面挡住的模态队列里，整局留着全屏阻断、人物移不动
                    // （实测日志 Form[13]/Form[202] 直到对局结束 1529/1540 才关闭）。
                    _pages?.CloseRoomIfOpen();                // 开局 → 关房间页（批C-④；对局场景接管画面）
                }
                else
                {
                    // **StartGame 被拒**（批 E：玩法数据身份不符——`BattleContext` 校验点已 End(SessionFailed)）：
                    // SimView 构造要读预测态，无 Sim 进 Attach 必炸——按会话失败收尾，不把"数据分叉拒局"变成错误态。
                    // 下面的 WaitEnded 经 earlyResult 立即拿到原因（Ended 已在上游发出）。
                    Log.Warning("StartGame 未建立解读世界（玩法数据身份不符/被拒）——跳过视图建立，按会话失败收尾", LogTag.Battle);
                }

                BattleContext.EndReason reason = await WaitEnded(_context, ct);
                Log.Info($"ended reason={reason} reconciles={_context.ReconcileCount}", LogTag.Battle);
                completed = reason == BattleContext.EndReason.Completed;
                matchResult = _context.MatchResult;          // 冻结结果（MatchEnded → 结算页数据源；批C-④）
                localPlayerId = _context.PlayerId;
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
                // 关闭序（§6.2）：输入接线 → 视图（含租约/实例/消费者）→ BattleContext（Match Scope 一并 Dispose）→ Account Scope
                DetachView();
                _matchSession?.Dispose();
                _matchSession = null;
                _context = null;
                accountSession?.Dispose();
                _account = null;
                // 收尾卸载对局场景组——**只在宿主关闭时跳过**：该路径的 Scene 行退出动作是"释放资源租约"
                // （§6.2；SceneService.ReleaseAll 随 ContainerModule 关闭执行），且关闭期资源系统可能已先行销毁
                // （编辑器退出时 YooAsset 驱动先跑 `YooAssets.Destroy()`，卸载经 AsyncOperationSystem 必抛）。
                // 判据取**宿主根令牌**（根取消 = 宿主关闭）而非本阶段令牌：主动离场（房间页「离开」/设置页
                // 「退出对局」走外部 `Request(Main)`）也会取消本阶段令牌，但那不是关闭、容器仍在运行——
                // 对局场景组必须照卸，否则残留在大厅画面里（换图重进还会累积）。
                if (!RootToken.IsCancellationRequested) UnloadBattleScenes();
            }

            if (next.HasValue) m.Request(next.Value, nextArgs);

            // 页面流（批C-④）：正常结束 → 结算页（带本局结果）；失败/主动离场 → 房间页在则替换回大厅。
            // 在 finally 之后（对局资源已收尾，页面切换不与之竞争）；错误态路径（next=Error）不动页面。
            if (next == ProcedureId.Main) _pages?.OnMatchEnded(completed, matchResult, localPlayerId);
        }

        /// <summary>开局入场加载对局场景组（**本阶段职责**——加载链修订：Rig ＋ 本局地图在 StartGame 之后加载）：
        /// 行键取 JoinAck 权威事实（多图批：`RoomClient.MapId` → <see cref="BattleScenes.ResolveMapId"/> 裁决 →
        /// 场景路径查地图表 `scene` 列）；必须早于 <see cref="BattleViewRig.Attach"/>（视图件按名解析场景对象）
        /// 与相机硬门（vcam 随 Rig 场景）。进度经开局入场遮罩（"正在进入对局…"）。
        /// 未注入场景服务（测试装配）= 跳过；**注入了场景服务但缺地图表 = 配置缺失 fail-fast**（不给"静默不加载"留口）；
        /// 加载失败向上抛（RunAsyncCore 的失败分支统一收口进 Error，finally 照卸已载场景）。</summary>
        private async UniTask LoadBattleScenesAsync(int joinedMapId, CancellationToken ct)
        {
            if (_scenes == null) return;
            if (_maps == null)
                throw new InvalidOperationException(
                    "装配缺地图表提供者（IMapProvider）——对局场景组的地图场景路径查表行 scene 列（多图批；ContainerModule 注入）");
            // 等遮罩真正开面再压加载：开面是异步入队（转场/加载 prefab），开面在途时若走到收场，
            // 排队中的开面随后落地即成孤儿面（落地即收，但会闪一下）——等就绪再载，过渡更稳。
            for (int i = 0; i < 120 && _feedback != null && !_feedback.LoadingVisible; i++)
                await UniTask.Yield(ct);
            int mapId = BattleScenes.ResolveMapId(joinedMapId);      // 0 = 服务器未标注 → 首图口径（单源裁决）
            string map = _maps.ScenePath(mapId);
            _feedback?.ReportLoading("正在加载对局场景…", 0.05f);
            if (!_scenes.IsLoaded(BattleScenes.Rig))
                await _scenes.LoadAdditiveAsync(BattleScenes.Rig, ct);
            _feedback?.ReportLoading("正在加载地图…", 0.5f);
            if (!_scenes.IsLoaded(map))
                await _scenes.LoadAdditiveAsync(map, ct);
            _feedback?.ReportLoading("场景就绪", 0.95f);
            UnityEngine.Debug.Log($"[Battle] 对局场景组已加载:Rig={BattleScenes.Rig} Map={map}（map_id={mapId} 叠加数 {_scenes.AdditiveCount}）");
        }

        /// <summary>离场卸载对局场景组（Rig ＋ 表内全部地图场景；幂等——未加载 = 服务侧 no-op）。
        /// 多图批：地图场景清单按地图表枚举（`IMapProvider.ScenePaths`——加图不改卸载代码）；
        /// 表面不可用（未装载/缺 scene 列）只记日志并退化为只卸 Rig（离场不因表现面失败而阻塞）。
        /// 失败只记日志不阻塞离场（场景留驻是表现问题，不是会话正确性问题）；
        /// 编辑器"领养"形态（场景手工已在场）下卸载是 no-op——手测时属预期。
        /// **只跳过宿主关闭**（<see cref="ProcedureStageBase{TId,TReq}.RootToken"/> 已取消——句柄归零由
        /// SceneService.ReleaseAll 负责，且关闭期资源系统可能已先行销毁）；其余离场出口
        /// （正常结束/失败/主动离场/被外部 `Request` 打断）都调本方法。</summary>
        private void UnloadBattleScenes()
        {
            if (_scenes == null) return;
            if (_maps != null)
            {
                try
                {
                    IReadOnlyList<string> mapScenes = _maps.ScenePaths();
                    for (int i = 0; i < mapScenes.Count; i++) UnloadOne(mapScenes[i]);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Battle] 离场卸载：读地图场景清单失败（只卸 Rig）:{ex.Message}", LogTag.Scene);
                }
            }
            UnloadOne(BattleScenes.Rig);
        }

        /// <summary>单场景卸载（异步、不阻塞离场）。失败经 <c>Forget</c> 回调落 Warning——async 体内的异常
        /// 进的是返回任务而非调用点，裸 <c>Forget()</c> 会让"失败只记日志"失效（转 UnobservedTaskException 红栈）。</summary>
        private void UnloadOne(string location)
            => _scenes.UnloadAdditiveAsync(location)
                .Forget(ex => Log.Warning($"[Battle] 离场卸载场景失败 {location}:{ex.Message}", LogTag.Scene));

        /// <summary>
        /// <summary>
        /// 等 StartGame，**与对局终止竞速**：等待期房间被服务器中止（等待超时/运维关闭/排空——
        /// MatchEnded 先于 StartGame 到达）时返回 false，调用方跳过视图建立、经 WaitEnded 的
        /// earlyResult 立即收尾——不竞速会让流程永远停在"等开局"（房间已销毁，信令永不再来）。
        /// 两路信号先到先得，败者随 finally 一并退订（<see cref="StageSignal"/> 是单源单事件等待，
        /// 双源竞速在此就地实现并循同一退订纪律）。
        /// </summary>
        private static async UniTask<bool> WaitStartGameOrEnded(BattleClient battle, BattleContext context,
            CancellationToken ct)
        {
            if (battle.Client.HasStartGame && battle.Client.StartGame != null) return true;
            if (context.EndedReason.HasValue) return false;

            var ready = new UniTaskCompletionSource<bool>();
            void OnStart(LiteNet.Proto.StartGame _) => ready.TrySetResult(true);
            void OnEnd(BattleContext.EndReason _) => ready.TrySetResult(false);
            battle.Client.OnStartGame += OnStart;
            context.Ended += OnEnd;
            try
            {
                return await ready.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                battle.Client.OnStartGame -= OnStart;   // 胜者败者一并退——一次性等待不留悬挂订阅
                context.Ended -= OnEnd;
            }
        }

        /// <summary>
        /// 相机是否真的就绪（"有服务"不等于"接上了 vcam"——服务常驻，vcam 随场景）。
        /// **按需解析**：此刻视图还没建立、<see cref="ICameraService.Follow"/> 一次都没调过，
        /// 光读状态必然是"未接线"，所以这里主动让适配器再解析一次（场景可能刚被 Main 切到 TrainingGround）。
        /// 非 Cinemachine 实现（测试替身等）视为就绪——由它自己保证。
        /// </summary>
        private bool CameraReady()
        {
            if (_camera == null) return false;
            var cinemachine = _camera as CinemachineCameraService;
            return cinemachine != null ? cinemachine.EnsureCamera() : true;
        }

        /// <summary>离场收尾：摘房间会话门面 → 摘对局菜单 Back 策略 → 清输入派发状态 → 拆视图域（逆序：消费者 → 视图 → 租约作用域）。</summary>
        private void DetachView()
        {
            RoomBridge.UnbindSession();            // 会话门面先摘（页面订阅不得命中死会话——未在途回调随作废）
            _pages?.DisableBattleMenuBack();   // 对局菜单 Back 随视图拆除（ESC 恢复原 Back 链——对局外不得开设置页）
            _input?.Reset();   // 离场即清派发状态（设备源与拦截源都是装配根的，不在此摘）
            _rig?.Dispose();
            _rig = null;
        }

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
            if (_context == null) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 主动离场热键（测试入口，与 ProcedureMain F9 进对局同门禁）：Leave → Ended(Leave) → 收尾回 Main。
            // 此处只留热键，不进正式 UI（正式离场入口由 UI 层提供）。
            // 离场入口＝UI（ESC → 设置页 → 退出对局），无热键（《UI工作台设计》§5 入口矩阵）
            if (UnityEngine.Input.GetKeyDown(TeleportKey)) TestModeRuntime.RequestTeleport();      // T = 传送到准心（测试模式）
            if (TestModeRuntime.TakeExitRequest())                                                          // GM 面板退出：离场并关模式（读即清）
            {
                TestModeRuntime.Active = false;
                _context.Leave();
            }
#endif

            // ① 渲染帧采样（上下文门在此裁决：登记源任一成立 → 本帧输入为空，见 IInputService）。
            //    瞄准 = **AimPoint 单口径**（《固定斜视角射击方案专项设计》§3）：设备源把准心射线与
            //    预测世界求交（实体圆柱 → 地面兜底）解出目标点；朝向与弹道两端自该点派生
            //    ——点不读 Yaw ⇒ "Yaw → 枪口 → 方向 → Yaw"闭环自激不存在，锚点选择问题整体消亡。
            //
            //    **瞄准世界注入（§3）**：设备源要用**同一个 SimRaycast** 把准心射线与实体圆柱求交，
            //    而世界所有权在流程层——设备源不缓存权威状态（否则是与裁决不同帧的陈旧世界）。
            //    未对齐（LocalEntityId=0）时按 slot=-1 注入 ⇒ 设备源退化为纯地面点解算（合法退化）。
            //
            //    采样前的诊断件驱动（自瞄验证件换源）必须**先于采样**——否则自瞄点落后一帧。
            _rig?.TickPreSample();
            if (_input != null && _context.Sim != null)
            {
                // 解析失败一律给 -1（**不可沿用 TryResolve 的 out 值**：失败时它可能是 0，
                // 而 0 是合法槽位——会让设备源跳过错误的实体）。
                int localSlot = _context.LocalEntityId != 0
                    && _context.Sim.State.TryResolve(_context.LocalEntityId, out int resolved) ? resolved : -1;
                _input.SetAimWorld(_context.Sim.State, localSlot);
                _input.SampleOnRenderFrame(_context.LocalPosition);
            }

            _context.Tick(elapseSeconds);         // ② 网络双泵 + 输入上行 + 预测推进 + 表现视图（唯一驱动入口）
            _rig?.Tick(elapseSeconds);            // ③ 视图消费者（动画/准心/HUD/激光/飘字/瞄准相机）——权威已应用

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (TestModeRuntime.TakeTeleportRequest())            // 测试模式传送：入队命令（帧边界派发 + 可撤销；读即清）
            {
                if (TestModeRuntime.Active && TestModeRuntime.TeleportEnabled)
                    _commands.SendQueued(new TeleportToAimCommand());
                else Log.Warning("[TestMode] 传送请求忽略（未开测试模式/未启用）", LogTag.Battle);
            }
#endif
        }

        /// <summary>等对局结束（Ended 恰好一次；ct 打断 = 宿主关闭路径）。</summary>
        private static UniTask<BattleContext.EndReason> WaitEnded(BattleContext context, CancellationToken ct)
            => StageSignal.WaitValue<BattleContext.EndReason>(
                h => context.Ended += h,
                h => context.Ended -= h,
                earlyResult: () => context.EndedReason,
                ct);
    }
}
