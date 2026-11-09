using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteSim;
using UnityEngine;
using UnityEngine.SceneManagement;
using LiteGame;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;

using LiteSync.Client;
namespace LiteGame
{
    /// <summary>
    /// 对局流程（**只做编排**）： OnEnter 经 IBattleRunFactory 建对局运行端口（Match Scope 全套）→ 会话建立后由 IBattleViewGroup 取件并挂视图/输入接线 → 每帧驱。
    /// </summary>
    public sealed class ProcedureBattle : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>开发形态传送键（传送到当前准心点；端口全关时按键无效果）。</summary>
        private const UnityEngine.KeyCode TeleportKey = UnityEngine.KeyCode.T;

        private const string UnloadFailureTitle = "离场清理失败";
        private const string UnloadFailureMessage = "对局场景未能安全卸载，已保留房间占用。请重新启动客户端后再进入房间。";
        private const string UnloadFailureMergeKey = "battle.unload-failure";

        private readonly IInputService _input;       // 输入服务
        private readonly ICameraService _camera;     // 相机服务（Cinemachine 适配器；装配根建、跨对局复用）
        private readonly IBattleCommands _commands;  // 战斗命令端口（传送命令化；命令类型归战斗域）
        private readonly IBattleViewGroupFactory _views;   // 视图组工厂（表现层实现；装订根注入）
        private readonly IRoomSessionBridge _roomSession;   // 房间会话门面装订口（适配器实现；房主开局批）
        private readonly RoomFlowLifetime _lifetime;         // 房间操作寿命（会话接管与清理通知；随流程段注入）
        private readonly bool _requireCamera;        // 缺相机 = 装配缺口（见 ctor 注释）
        private readonly IBattleRunFactory _runs;    // 对局运行工厂（Battle 域产物；流程不构造 Match/Battle 会话）
        private readonly PageFlow _pages;            // 五页页面流（批C-④；可空 = 测试装配不接 UI）
        private readonly ISceneService _scenes;      // 对局场景组加载与卸载（可空 = 测试装配）
        private readonly IMapProvider _maps;         // 地图表（多图批：场景路径查表行 scene 列／离场卸载按表枚举）
        private readonly FeedbackService _feedback;  // 开局入场加载遮罩（可空 = 测试装配不接 UI）
        private readonly LiteGame.IDevSession _devSession;     // 开发会话端口（dev 投影读本局规则；可空 = 测试装配）

        private IBattleRun _run;                     // 对局运行端口（Battle 域实现；流程唯一状态入口）

        /// <summary>视图域（经运行端口消费——Battle 阶段不直接持有生命周期）。</summary>
        private IBattleViewGroup _rig => _run?.View;
        private ClientScope _account;
        private Scene _sceneBeforeBattle;            // 进场前活动场景（离场还原端点——UnloadBattleScenesAsync 一次性消费）
        private readonly List<string> _ownedScenes = new();   // 本局实际加载并持有的场景（最多 Rig+地图；离场只卸这份）

        /// <summary>
        /// 上下文门是登记进服务的具名拦截源（`ui.modal` 由装配根登记），流程只负责本局的设备源与 清派发状态（《角色状态与动作专项设计》§3"输入三件"）。
        /// </summary>
        /// <param name="input">输入服务（装配根注册的跨对局实例）。**流程不持"UI 是否拦截"这类判断**：
        /// <param name="camera">相机服务（Cinemachine 适配器，装配根建，**跨场景常驻**）。**相机构图归
        /// <param name="requireCamera">缺相机时是否拒绝对局。**装配根按场景是否配了虚拟相机决定**：
        /// <param name="views">视图组工厂（表现层实现；本流程只认端口——不持有视图装配件类型）。</param>
        /// <param name="roomSession">房间会话门面装订口（房间会话适配器实现；房主开局批）。</param>
        internal ProcedureBattle(IInputService input, ICameraService camera, IBattleCommands commands,
            IBattleViewGroupFactory views, IRoomSessionBridge roomSession, RoomFlowLifetime lifetime,
            CancellationToken rootToken = default, bool requireCamera = false,
            IBattleRunFactory runs = null, PageFlow pages = null,
            ISceneService scenes = null, IMapProvider maps = null, FeedbackService feedback = null,
            LiteGame.IDevSession devSession = null)
            : base(rootToken)
        {
            _input = input;
            _camera = camera;
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _views = views ?? throw new ArgumentNullException(nameof(views));
            _roomSession = roomSession ?? throw new ArgumentNullException(nameof(roomSession));
            _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _requireCamera = requireCamera;
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _pages = pages;
            _scenes = scenes;
            _maps = maps;
            _feedback = feedback;
            _devSession = devSession ?? NullDevSession.Instance;   // 发布/测试装配 = 全关端口
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, req.RoomOperation, ct).ForgetLogged("ProcedureBattle");   // 一行转发，仅此而已——禁止 async void

        /// <summary>async 方法不能带 <c>in</c> 参数（CS1988）——payload 在转发处取值，async 体只收所需字段。</summary>
        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, RoomFlowOperation operation,
            CancellationToken ct)
        {
            ProcedureId? next = null;
            ProcedureArgs nextArgs = default;
            bool completed = false;                                       // 正常结束（MatchEnded 冻结结果在）——结算页判据
            LiteNet.Proto.MatchEnded matchResult = null;
            int localPlayerId = -1;
            IMatchSession matchSession = null;
            IBattleViewGroup rig = null;          // 未移交运行端口前由本方法持有（异常路径显式释放）
            CancellationTokenSource workCts = null;
            bool tookSession = false;
            try
            {
                operation = operation ?? throw new InvalidOperationException("Battle 阶段缺少房间操作句柄（必须由 Match 排入同一操作）");
                if (!_lifetime.TryTakeSession(operation, out matchSession))
                {
                    // 操作已失效/未排队：不建 run/view，待接管会话仍归 Lifetime；宿主未取消时安全点回 Main。
                    if (!RootToken.IsCancellationRequested && !ct.IsCancellationRequested)
                        next = ProcedureId.Main;
                }
                else
                {
                    tookSession = true;
                    operation.MarkRecoveryGeneration(_pages?.RecoveryGeneration ?? long.MinValue);
                    _account = matchSession.Scope;
                    workCts = CancellationTokenSource.CreateLinkedTokenSource(ct, operation.Token);
                    CancellationToken workToken = workCts.Token;   // 本局异步工作令牌（宿主取消 ∪ 操作失效）

                    // 视图域：工厂建视图组（表现层装配点：自有 BattleView 作用域 + 视图根 + 资源取件；缺角色资源＝无角色视图）。
                    rig = _views.Create(_account, DispatchTeleport);
                    await rig.LoadAsync(workToken);

                    // 对局运行端口（Battle 域构造 Match/Battle 会话；流程不认战斗域内部件）。
                    _run = _runs.Create(matchSession);
                    _run.AttachView(rig);                // 视图域归运行端口持有（拆除由会话统一逆序）
                    rig = null;                           // 所有权已移交：finally 不再重复释放
                    _roomSession.BindSession(_run.RoomSession);   // 房间会话门面（房主开局批）——页面经 IRoomView 读房主事实/发提前开局；收尾必摘
                    Log.Info("context-created (Match Scope built)", LogTag.Battle);

                    bool started = await _run.WaitStartedOrEndedAsync(workToken);   // Sim 就绪 = 视图可建（SimView 要读预测态）
                    if (!started)
                    {
                        // 未开局即散场（等待超时/运维关闭/排空——MatchEnded 先于 StartGame 到达）：
                        // 跳过视图建立，经下方 WaitEnded 的 earlyResult 立即收尾（回 Main；房间页由页面流替换回大厅）。
                        Log.Info("room aborted before start (MatchEnded arrived before StartGame)", LogTag.Battle);
                    }
                    else if (_run.HasSimulation)
                    {
                        // 开局入场（加载链修订）：**对局场景组在本阶段、StartGame 之后加载**（Rig ＋ 地图，行键取 JoinAck 权威事实）——本阶段独有装载与卸载（不在 Match 载、避免"未开局即付费"与跨阶段 所有权分叉）。
                        FeedbackService.LoadingScope entry = _feedback?.BeginLoading("正在进入对局…") ?? default;
                        try
                        {
                            await LoadBattleScenesAsync(_run.MapId, workToken);
                            // 相机缺失 = 装配缺口（场景没配 vcam / 没切到玩法场景）：不静默降级成"跑得动但看不见"
                            // 的对局，在开打前显性失败——vcam 随 Rig 场景，故硬门必须在场景就位之后裁决。
                            if (_requireCamera && !CameraReady())
                                throw new InvalidOperationException(
                                    "对局相机未就绪：场景里没有接上的 CinemachineVirtualCamera"
                                    + "（相机配置归场景——确认已切到配了 vcam 的玩法场景）");
                            _run.ActivateView(openBattleMenu: () => _pages?.OpenBattleSettings());
                            _pages?.EnableBattleMenuBack();       // 对局中"ESC＝菜单"（与 HUD 菜单钮同源）
                        }
                        finally { entry.Dispose(); }              // 视图建成才收遮罩；失败路径遮罩一并收（异常走 Error）

                        // **先收加载系统面，再关房间页**：反序（先关 Bottom 房间页、后关 System 加载面）会让 房间页关闭卡在被加载面挡住的模态队列里，整局留着全屏阻断、人物移不动 （实测日志 Form[13]/Form[202] 直到对局结束 1529/1540 才关闭）。
                        _pages?.CloseRoomIfOpen();                // 开局 → 关房间页（批C-④；对局场景接管画面）
                    }
                    else
                    {
                        // **StartGame 被拒**（批 E：玩法数据身份不符——运行端口上游已 End(SessionFailed)）： SimView 构造要读预测态，无 Sim 进 Attach 必炸——按会话失败收尾，不把"数据分叉拒局"变成错误态。
                        Log.Warning("StartGame 未建立解读世界（玩法数据身份不符/被拒）——跳过视图建立，按会话失败收尾", LogTag.Battle);
                    }

                    BattleRunOutcome outcome = await WaitEnded(_run, workToken);
                    Log.Info($"ended reason={outcome} reconciles={_run.ReconcileCount}", LogTag.Battle);
                    completed = outcome == BattleRunOutcome.Completed;
                    matchResult = _run.MatchResult;              // 冻结结果（运行端口读口 → 结算页数据源；批C-④）
                    localPlayerId = _run.PlayerId;
                    next = ProcedureId.Main;              // 会话失败也回 Main（日志已留痕）
                }
            }
            catch (OperationCanceledException)
            {
                // 宿主关闭/操作失效/流程被打断：finally 收尾，不流转。
            }
            catch (Exception ex)
            {
                // 分类在抛出点声明：场景加载失败已标 Recoverable；未标记的程序员/装配错误仍为 Fatal。
                ErrorKind kind = ErrorRecoveryService.Classify(ex);
                bool fatal = kind == ErrorKind.Fatal
                    || kind == ErrorKind.Compatibility
                    || kind == ErrorKind.Security;
                if (fatal)
                {
                    Fail(m, ex, nameof(RunAsyncCore));
                    next = ProcedureId.Error;
                    nextArgs = new ProcedureArgs(ex);
                }
                else
                {
                    Log.Error($"[Battle] 对局准备失败（{kind}），清理后回 Main:{ex.Message}", LogTag.Battle);
                    if (tookSession && operation != null)
                        operation.MarkRoomFailure("对局准备失败", "本局准备未能完成，已清理资源，请重试");
                    next = ProcedureId.Main;
                }
            }
            // 关闭序（§6.2）：操作清理通知 → 输入接线 → 视图（含租约/实例/消费者）→ 运行端口（Battle Scope 一并 Dispose）→ Match Scope → 原场景卸载。
            if (operation != null && !operation.CleanupDone) _lifetime.BeginCleanup(operation);
            rig?.Dispose();                        // 未移交运行端口（factory 抛异常）：视图/租约由流程侧收回
            DetachView();
            _run?.Dispose();
            _run = null;
            matchSession?.Dispose();             // 接管后所有权归本阶段：仅此处回收一次
            matchSession = null;
            _account = null;

            // 流程流转先于卸载：清理期间仍由操作占用阻断新入场，但不把阶段机钉在 Battle。
            if (next.HasValue) m.Request(next.Value, nextArgs);

            // 必要场景卸载必须 await 完成后再 NotifyCleaned：Forget 会提前释放占用，下一局在旧场景上入场。
            // 宿主关闭（RootToken）才跳过卸载；阶段离场取消不阻断必要卸载。
            bool unloadFailed = false;
            if (tookSession && !RootToken.IsCancellationRequested)
            {
                try
                {
                    await UnloadBattleScenesAsync(RootToken);
                }
                catch (Exception ex)
                {
                    unloadFailed = true;
                    Log.Error($"[Battle] 离场必要场景卸载失败，保留操作占用:{ex.Message}", LogTag.Scene);
                }
            }
            workCts?.Dispose();

            if (unloadFailed)
            {
                // 清理失败是停机态：不假 NotifyCleaned、不回可操作大厅；保留 owned 未卸场景与操作占用。
                operation?.MarkRoomFailure(UnloadFailureTitle, UnloadFailureMessage);
                operation?.SuppressRecovery();                 // 若后续误通知，也不再自动导航
                Log.Error($"[Battle] 离场清理失败，保留占用；未卸场景=[{string.Join(", ", _ownedScenes)}]", LogTag.Scene);
                // 不在离场收尾再 Request(Error)：阶段已按 next 安全流转（通常 Main），此处只停机+busy。
                if (!RootToken.IsCancellationRequested && _feedback != null)
                {
                    try
                    {
                        await _feedback.ShowErrorAsync(UnloadFailureTitle, UnloadFailureMessage,
                            retryable: false, mergeKey: UnloadFailureMergeKey, ct: RootToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception feedbackError)
                    {
                        Log.Error($"[Battle] 清理失败反馈未完成:{feedbackError.Message}", LogTag.Battle);
                    }
                }
                else if (_feedback == null)
                {
                    Log.Error("[Battle] 清理失败且未接线 FeedbackService：需重新启动客户端", LogTag.Battle);
                }
                return;                                        // 不 NotifyCleaned：旧场景仍在，禁止下一局入场
            }

            if (operation != null && tookSession)
            {
                if (next == ProcedureId.Error)
                {
                    // 真正的 Fatal：错误阶段自管恢复，页面流不抢写。
                    operation.SuppressRecovery();
                }
                else
                {
                    // 正常结束→结算；普通主动离房/取消→大厅。只有账号重入/宿主退出才由 SuppressRecovery 抑制。
                    operation.MarkBattleOutcome(next == ProcedureId.Main && completed,
                        next == ProcedureId.Main ? matchResult : null,
                        next == ProcedureId.Main ? localPlayerId : -1);
                }
            }

            if (operation != null && !operation.CleanupDone) _lifetime.NotifyCleaned(operation);
        }

        /// <summary>
        /// 开局入场加载对局场景组（**本阶段职责**——加载链修订：Rig ＋ 本局地图在 StartGame 之后加载）： 行键取 JoinAck 权威事实（多图批。）。
        /// </summary>
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
            _sceneBeforeBattle = SceneManager.GetActiveScene();      // 离场还原端点（进场前活动场景）
            int mapId = BattleScenes.ResolveMapId(joinedMapId);      // 0 = 服务器未标注 → 首图口径（单源裁决）
            string map = _maps.ScenePath(mapId);
            _feedback?.ReportLoading("正在加载对局场景…", 0.05f);
            await LoadOwnedSceneAsync(BattleScenes.Rig, ct);
            _feedback?.ReportLoading("正在加载地图…", 0.5f);
            await LoadOwnedSceneAsync(map, ct);
            // 对局活动场景 = 地图（显式裁决，不依赖"Rig 先、地图后"的加载顺序隐含态）：清屏天空盒与
            // 环境光/雾按**活动场景**的 RenderSettings 取——视觉决策归地图场景资产（启动底色批）。
            Scene mapScene = SceneManager.GetSceneByPath(map);
            if (mapScene.IsValid())
            {
                if (SceneManager.GetActiveScene() != mapScene) SceneManager.SetActiveScene(mapScene);
            }
            else
            {
                Log.Warning($"[Battle] 对局场景已加载但按路径找不到场景对象，活动场景保持现状:{map}", LogTag.Scene);
            }
            _feedback?.ReportLoading("场景就绪", 0.95f);
            Log.Info($"[Battle] 对局场景组已加载:Rig={BattleScenes.Rig} Map={map}（map_id={mapId} 叠加数 {_scenes.AdditiveCount}）", LogTag.Battle);
        }

        /// <summary>加载本局场景并登记持有责任；await 抛取消但场景已落地时也按 IsLoaded 登记，避免漏卸。</summary>
        private async UniTask LoadOwnedSceneAsync(string location, CancellationToken ct)
        {
            if (_scenes == null || string.IsNullOrEmpty(location) || _scenes.IsLoaded(location)) return;
            try
            {
                try
                {
                    await _scenes.LoadAdditiveAsync(location, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ClientErrorException)
                {
                    throw;                             // 抛出点已分类（含 Fatal），不在这里降级
                }
                catch (Exception ex)
                {
                    // 实际场景加载失败属于房间可恢复：清理本局后再走 RecoveryStep 确认回大厅。
                    throw new ClientErrorException(ErrorKind.Recoverable,
                        "对局场景加载失败:" + location, ex);
                }
            }
            finally
            {
                if (_scenes.IsLoaded(location) && !_ownedScenes.Contains(location))
                    _ownedScenes.Add(location);
            }
        }

        /// <summary>离场只逆序卸载本局登记持有的场景；未实际加载的条目不调用服务。</summary>
        private async UniTask UnloadBattleScenesAsync(CancellationToken ct)
        {
            if (_scenes == null)
            {
                _ownedScenes.Clear();
                return;
            }
            RestoreActiveSceneBeforeUnload();

            for (int i = _ownedScenes.Count - 1; i >= 0; i--)
            {
                string location = _ownedScenes[i];
                if (!_scenes.IsLoaded(location))
                {
                    _ownedScenes.RemoveAt(i);
                    continue;
                }
                await UnloadOne(location, ct);
                _ownedScenes.RemoveAt(i);
            }
        }

        /// <summary>卸载前显式还原活动场景到进场前值：不依赖 Unity 对"卸载活动场景"的自动回落
        /// （回落目标的选择顺序未文档化——地图先卸会短暂落回 Rig 的环境设置）。</summary>
        private void RestoreActiveSceneBeforeUnload()
        {
            if (!_sceneBeforeBattle.IsValid()) return;
            if (SceneManager.GetActiveScene() != _sceneBeforeBattle)
                SceneManager.SetActiveScene(_sceneBeforeBattle);
            _sceneBeforeBattle = default;                // 一次性消费，不跨场次持留
        }

        /// <summary>单场景卸载返回任务，由调用方串行 await；失败向上抛给离场清理判据。</summary>
        private UniTask UnloadOne(string location, CancellationToken ct)
            => _scenes.UnloadAdditiveAsync(location, ct);

        /// <summary>
        /// 相机是否真的就绪（"有服务"不等于"接上了 vcam"——服务常驻，vcam 随场景）。
        /// </summary>
        private bool CameraReady()
        {
            if (_camera == null) return false;
            return _camera.EnsureReady();
        }

        /// <summary>离场收尾：摘房间会话门面 → 摘对局菜单 Back 策略 → 清输入派发状态 → 拆视图域（逆序：消费者 → 视图 → 租约作用域）。</summary>
        private void DetachView()
        {
            _roomSession.UnbindSession();           // 会话门面先摘（页面订阅不得命中死会话——未在途回调随作废）
            _pages?.DisableBattleMenuBack();   // 对局菜单 Back 随视图拆除（ESC 恢复原 Back 链——对局外不得开设置页）
            _input?.Reset();   // 离场即清派发状态（设备源与拦截源都是装配根的，不在此摘）
            _run?.DetachView();                // 视图域拆除归会话（消费者 → 视图 → 租约作用域）
        }

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
            if (_run == null) return;

            // 主动离场热键（开发入口）：Leave → Ended(Leave) → 收尾回 Main。
            if (UnityEngine.Input.GetKeyDown(TeleportKey)) _devSession.RequestTeleport();   // T = 传送到准心（开发形态）
            if (_devSession.TakeExitRequest())                                              // GM 面板退出：离场并关模式（读即清）
            {
                _devSession.SetActive(false);
                _run.Leave();
            }

            // Leave() 会在本调用栈内同步走完 Ended → finally 收尾（_run 置空）——
            // 上面的顶门检过后本帧仍可能已被拆空，此处必须复检，否则本帧余下驱动面读空引用。
            if (_run == null) return;

            // ① 渲染帧采样（上下文门在此裁决：登记源任一成立 → 本帧输入为空，见 IInputService）。
            _rig?.TickPreSample();

            // ② 运行端口推进：瞄准世界注入 → 输入采样 → 网络双泵/上行/预测/表现视图（唯一驱动入口）。
            //    世界所有权归运行端口——设备源不缓存权威状态（否则是与裁决不同帧的陈旧世界）。
            _run.Tick(elapseSeconds, _input);
            _rig?.Tick(elapseSeconds);            // ③ 视图消费者（动画/准心/HUD/激光/飘字/瞄准相机）——权威已应用

            if (_devSession.TakeTeleportRequest())                // 开发形态传送：入队命令（帧边界派发 + 可撤销；读即清）
            {
                if (_devSession.Active && _devSession.TeleportEnabled)
                    _commands.RequestTeleportToAim();
                else Log.Warning("[TestMode] 传送请求忽略（未开测试模式/未启用）", LogTag.Battle);
            }
        }

        /// <summary>传送分发单点（本地服宿主钩子；T 键/GM 面板/撤销共用同一通道）。</summary>
        private void DispatchTeleport(SimVector3 pos)
        {
            ILocalTestHooks hooks = _devSession?.LocalTest;
            if (hooks == null) return;
            hooks.TeleportTarget = pos;
            hooks.TeleportDispatch = true;
        }

        /// <summary>等对局结束（Ended 恰好一次；ct 打断 = 宿主关闭路径）。</summary>
        private static UniTask<BattleRunOutcome> WaitEnded(IBattleRun run, CancellationToken ct)
            => StageSignal.WaitValue<BattleRunOutcome>(
                h => run.Ended += h,
                h => run.Ended -= h,
                earlyResult: () => run.Outcome,
                ct);
    }
}
