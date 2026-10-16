using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
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
    /// **视图域全部归 <see cref="BattleViewRig"/>**（资源租约 / SimView / 七个表现件装配 / 每帧驱动 /
    /// 逆序拆除）：本类只保留"取会话 → 等信令 → 每帧驱动 → 收尾"的编排与对局级裁决（相机就绪门、
    /// 测试模式热键、瞄准世界注入），不再逐个持有表现件。
    ///
    /// 驱动形态：StageMachine 的 OnUpdate 即帧驱动入口（容器 Seal 后不可再注册 ITickable——
    /// 对局期驱动走阶段生命周期，天然随离场停止）。取消语义：阶段 CTS 链接宿主根（基类），
    /// 宿主关闭时等待 Ended 的异步被打断，finally 仍保证收尾（离场无 Match 残留的流程侧保证）。
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
        private readonly AudioService _audio;        // 声音壳（装配根注册；对局音效的组+代理后端）
        private readonly CommandCenter _commands;    // 命令中心（传送命令化：撤销/重做/排队派发）
        private readonly IInputService _input;       // 输入服务
        private readonly ICameraService _camera;     // 相机服务（Cinemachine 适配器；装配根建、跨对局复用）
        private readonly bool _requireCamera;        // 缺相机 = 装配缺口（见 ctor 注释）
        private readonly LiteFramework.IWorldClock _worldClock;   // 世界钟（伤害数字寿命/合并窗）
        private readonly IMatchSessionFactory _matchSessions;
        private readonly PageFlow _pages;            // 五页页面流（批C-④；可空 = 测试装配不接 UI）
        private readonly ISceneService _scenes;      // 对局场景组卸载（可空 = 测试装配）
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
            CrosshairFeelBook crosshairFeel = null, ISceneService scenes = null)
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
                // 相机缺失 = 装配缺口（场景没配 vcam / 进对局前没切到玩法场景）：
                // 不静默降级成"跑得动但看不见"的对局，在开打前显性失败。
                if (_requireCamera && !CameraReady())
                    throw new InvalidOperationException(
                        "对局相机未就绪：场景里没有接上的 CinemachineVirtualCamera"
                        + "（相机配置归场景——确认已切到配了 vcam 的玩法场景）");

                // 视图域：自有 BattleView 作用域 + 视图根 + 六个取件（缺角色资源回退程序化灰盒）
                _rig = new BattleViewRig(_content, _input, _camera, _account, _vfx, _worldClock, _audio, _commands,
                    crosshairFeel: _crosshairFeel);   // 准心手感投影（可空 = 测试装配）
                await _rig.LoadAsync(ct);

                _matchSession = _matchSessions?.Create(accountSession)
                    ?? throw new InvalidOperationException("缺少 MatchSessionFactory（完整 DI 装配要求由工厂创建 Match 域）");
                _context = _matchSession.Context;
                RoomBridge.BindSession(new RoomSessionFace(battleClient));   // 房间会话门面（房主开局批）——页面经 Bridge.room 读房主事实/发提前开局；收尾必摘
                Log.Info("context-created (Match Scope built)", LogTag.Battle);

                await WaitStartGame(battleClient, ct);        // Sim 就绪 = 视图可建（SimView 要读预测态）
                if (_context.Sim != null)
                {
                    _rig.Attach(_context, openBattleMenu: () => _pages?.OpenBattleSettings());
                    _pages?.EnableBattleMenuBack();           // 对局中"ESC＝菜单"（与 HUD 菜单钮同源）
                    _pages?.CloseRoomIfOpen();               // 开局 → 关房间页（批C-④；对局场景接管画面）
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
                UnloadBattleScenes();                     // 离场卸载对局场景组（Rig ＋ 地图）——回 Main 只有启动场景
            }

            if (next.HasValue) m.Request(next.Value, nextArgs);

            // 页面流（批C-④）：正常结束 → 结算页（带本局结果）；失败/主动离场 → 房间页在则替换回大厅。
            // 在 finally 之后（对局资源已收尾，页面切换不与之竞争）；错误态路径（next=Error）不动页面。
            if (next == ProcedureId.Main) _pages?.OnMatchEnded(completed, matchResult, localPlayerId);
        }

        /// <summary>离场卸载对局场景组（`BattleScenes` 单源；幂等——未加载 = 服务侧 no-op）。
        /// 失败只记日志不阻塞离场（场景留驻是表现问题，不是会话正确性问题）；
        /// 编辑器"领养"形态（场景手工已在场）下卸载是 no-op——手测时属预期。</summary>
        private void UnloadBattleScenes()
        {
            if (_scenes == null) return;
            UnloadOne(BattleScenes.Arena);
            UnloadOne(BattleScenes.Training);
            UnloadOne(BattleScenes.Rig);
        }

        private void UnloadOne(string location)
        {
            try { _scenes.UnloadAdditiveAsync(location).Forget(); }
            catch (Exception ex) { Log.Warning($"[Battle] 离场卸载场景失败 {location}:{ex.Message}", LogTag.Scene); }
        }

        /// <summary>
        /// 等 StartGame（Reliable 信令；Sim 未建则视图无源）。已建（重连/重进）直接放行。
        ///
        /// **判据是"StartGame 是否已处理"，不是"快照是否已到"**：快照与 StartGame 是两条独立路径
        /// （Unreliable vs Reliable），快照先到完全正常；拿 LastSnapshotFrame 当"已在局中"的代理，
        /// 会让本方法提前返回、后面读空的 Sim。
        /// </summary>
        private static UniTask WaitStartGame(BattleClient battle, CancellationToken ct)
            => StageSignal.Wait<LiteNet.Proto.StartGame>(
                h => battle.Client.OnStartGame += h,
                h => battle.Client.OnStartGame -= h,
                alreadyDone: battle.Client.HasStartGame && battle.Client.StartGame != null,
                ct);

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
