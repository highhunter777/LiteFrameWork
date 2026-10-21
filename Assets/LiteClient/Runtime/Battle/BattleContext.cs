using System;
using System.IO;
using System.Threading;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteNet;
using LiteNet.Protocol;
using LiteSim;
using LiteView;

namespace LiteClient
{
    /// <summary>
    /// 对局上下文：一局对局的**运行态编排**——网络事件 → 持久镜像重建（协议单源 SnapshotReassembler）→
    /// 预测/和解（RollbackSim）→ 表现视图（SimView/相机/动画）→ 输入上行。传输由 <see cref="BattleClient"/>
    /// 持有、地图经装配注入（`IMapProvider` 从同一份 `Map/tbmap` 投影——D22 无回退）、生命周期挂
    /// Match Scope（本类创建并持有，Dispose 即拆——离场无 Match 残留的载体）。
    ///
    /// **两个协作件**（各管一件，本类只编排）：
    /// <see cref="BattleInputPipeline"/>（输入三件的顺序与上行——必须在本地服步进之前）、
    /// <see cref="BattleSnapshotMirror"/>（持久镜像 + 和解判定 + 视图插值源）。
    ///
    /// **断线恢复策略**：SuspectedLost 即自动 <see cref="BattleClient.BeginReconnect"/>（凭 JoinAck 票据）；
    /// Failed（重连超时/拒绝/版本不符）→ <see cref="Ended"/>(SessionFailed) 交流程层裁决回主菜单——
    /// 不在上下文内静默重建会话。
    ///
    /// 生命周期：ctor 建 Match Scope → 挂 RoomClient 事件（退订经 <see cref="DelegatedDisposable"/> 登记进
    /// Scope，LIFO 保证晚挂先退）→ Dispose 拆订阅 → Match Scope.Dispose。事件处理在 Dispose 后一律短路。
    /// </summary>
    public sealed class BattleContext : IDisposable
    {
        /// <summary>对局结束原因（Ended 事件载荷；流程层据此回 Main 或报错）。</summary>
        public enum EndReason
        {
            /// <summary>玩家主动离场。</summary>
            Leave = 0,
            /// <summary>会话失败（重连超时/被拒/版本不符——无票据的断线同归此类）。</summary>
            SessionFailed,
            Completed,
            /// <summary>未开局即散场（等待期房间被服务器中止——等待超时/运维关闭/排空；
            /// 无结果可结算，不进结算页）。</summary>
            Aborted,
        }

        /// <summary>未标注形态的默认房间规模（2 人房；JoinAck.expected_players = 0 时回落——
        /// 嵌入式/测试直构形态与地图的"首图口径"同款先例）。</summary>
        public const int DefaultExpectedPlayers = 2;

        /// <summary>本局房间规模（**定容依据**：预测世界席位/输入模板/本地服席位——JoinAck 定值）。
        /// 两端 StartGame 世界重建同用此值（＝房间 `RoomConfig.ExpectedPlayers`）。</summary>
        public int ExpectedPlayers { get; }

        /// <summary>对局结束（恰好一次；Dispose 不触发——那是清理不是结束）。</summary>
        public event Action<EndReason> Ended;
        public EndReason? EndedReason { get; private set; }

        public ClientSessionPhase SessionPhase => _battle.Phase;
        public bool Connected => _battle.Connected;
        public int PlayerId => _battle.Client.PlayerId;
        /// <summary>本局标识（StartGame.MatchId；空 = 未开局）——日志关联/现场包共用。</summary>
        public string MatchId => _matchId;
        /// <summary>本局玩法配置摘要（ConfigHash 复算值；0 = 未校验）——现场包身份面。</summary>
        public uint ConfigDigest => _configDigest;
        public LiteNet.Proto.MatchEnded MatchResult => _battle.Client.MatchResult;

        /// <summary>本地玩家实体 Id（0 = 尚未对齐——首份快照按 Slot==PlayerId 解析）。</summary>
        public long LocalEntityId => _localEntityId;

        /// <summary>本地预测/和解 Sim（StartGame 后可用；null = 对局尚未建立）。</summary>
        public RollbackSim Sim => _sim;

        /// <summary>权威快照镜像（首份快照落地前为 null）。**公共事实 HUD 的读源**（排行榜 KDA/
        /// 阵亡复活帧/保护期等）——预测态只服务本地手感，不得回灌公共展示（否则预测击杀会先上板、
        /// 预测死亡会驱动阵亡面板）。</summary>
        public SimWorldState AuthorityState => _mirror.State;

        /// <summary>判定用地图（两端单源：同一份 `Map/tbmap.bytes` 经共享投影 `MapTableProjection` 产出
        /// ——服务端 `ServerMapLoader`／客户端 `IMapProvider` 同代码同数据；表现消费者读它——
        /// 瞄准激光的障碍射线等 View 侧判定共享同一份，不另建第二事实源）。</summary>
        public SimMapData Map => _map;

        /// <summary>最近一次收到的快照帧号（诊断/兜底——视点帧请用 <see cref="ViewRenderFrame"/>）。</summary>
        public int LastSnapshotFrame => _battle.Client.LastSnapshotFrame;

        /// <summary>视图当前渲染帧（tick 帧轴；延迟补偿的 `viewFrame` 单源）。视图未挂/尚无快照时
        /// 回落 <see cref="LastSnapshotFrame"/>（纯会话/测试形态）。</summary>
        public int ViewRenderFrame => View != null && View.RenderFrame >= 0
            ? View.RenderFrame
            : (LastSnapshotFrame >= 0 ? LastSnapshotFrame : 0);

        /// <summary>和解次数（本地预测被权威覆盖的次数；诊断/DevHUD）。</summary>
        public long ReconcileCount => _reconcileCount;

        /// <summary>公共战斗事件面（命中/击杀/开火的统一入口；独立事件批 <c>PacketType.CombatEvents</c>）。
        /// 取代原 <c>KillFeed</c>/<c>FireFeed</c>/<c>HitFeed</c> 三件——一条去重分发路径，按 kind 分流。</summary>
        public PublicCombatFeed CombatFeed => _combatFeed;

        /// <summary>网络质量观测（会话面透传——HUD 展示；表现/诊断面，不参与判定）。</summary>
        public NetworkQuality NetworkQuality => _battle.Quality;

        private readonly BattleClient _battle;
        private readonly ClientScope _matchScope;
        private readonly SimMapData _map;
        private readonly uint _gameplayDigestBase;        // 本地装载产物的玩法数据身份基摘要（批 E 校验点用）
        private readonly BattleInputPipeline _inputs;      // 输入三件（顺序与上行）
        private readonly BattleSnapshotMirror _mirror = new BattleSnapshotMirror();   // 持久镜像 + 和解 + 插值源
        private readonly PublicCombatFeed _combatFeed = new PublicCombatFeed();       // 公共战斗事件面（独立事件批 → 按 kind 分流入队）

        private RollbackSim _sim;
        private long _localEntityId;
        private long _reconcileCount;
        private bool _disposed;
        private int _ended;

        // ---- 诊断现场包（mismatch/配置身份不符/托管异常时落本地；不在正常路径产生 IO）----
        private string _matchId = string.Empty;
        private uint _configDigest;
        private DiagnosticsBundleWriter _diagnosticsBundles;
        private bool _diagnosticsRateLimited;   // 同局限频只提示一次（防 mismatch 刷屏 Warning）
        private readonly object _diagnosticsGate = new object();   // mismatch（主线程）与异常钩子（任意线程）互斥

        /// <summary>表现视图（SimView 建后挂上；null = 无视图——纯会话/测试形态仍完整可跑）。</summary>
        public SimView View { get; private set; }

        /// <param name="inputProvider">本地输入源（<see cref="IInputService"/>：
        /// 采样/上下文门/帧边界门都在服务里；本类只按逻辑帧推进并取用。null = 空输入——纯会话/测试形态）。</param>
        /// <param name="map">判定地图（**必填**——装配点经 `IMapProvider` 注入；D22：无回退地图，
        /// 测试直构须显式传图——Unity 侧无 TestMaps 源链接，就地同值构建）。</param>
        /// <param name="gameplayDigestBase">本地装载产物的玩法数据身份**基摘要**
        /// （`ConfigService.GameplayDigestBase`——装配经 `MatchSessionFactory` 注入；见
        /// <see cref="OnStartGame"/> 校验点）。0 = 无表身份形态（测试直构）——与真服务端的房间摘要比对必不等的
        /// 形态由 L1 用例显式构造，生产链恒为装载产物。</param>
        public BattleContext(BattleClient battle, ClientScope accountScope,
            IInputService inputProvider = null, SimMapData map = null, uint gameplayDigestBase = 0)
        {
            _battle = battle ?? throw new ArgumentNullException(nameof(battle));
            if (accountScope == null) throw new ArgumentNullException(nameof(accountScope));
            _map = map ?? throw new ArgumentException(
                "地图必填——装配经 IMapProvider 注入（D22：无回退地图）", nameof(map));
            _gameplayDigestBase = gameplayDigestBase;
            // 定容依据＝**JoinAck 权威事实**（开房参数批：玩家开房时可选人数；0 = 未标注 → 默认 2 座回落）。
            // 构造点晚于 JoinAck（同地图：MapId 已定值）——见 MatchSessionFactory 装配序。
            ExpectedPlayers = battle.Client.ExpectedPlayers > 0
                ? battle.Client.ExpectedPlayers
                : DefaultExpectedPlayers;
            _inputs = new BattleInputPipeline(_battle, inputProvider, ExpectedPlayers);
            _matchScope = accountScope.CreateChild("Match");

            // 订阅 + 退订登记（LIFO：晚挂先退——Dispose 即拆，不靠调用方记得退订）
            _battle.Client.OnStartGame += OnStartGame;
            _battle.Client.OnSnapshot += OnSnapshot;
            _battle.Client.OnCombatEvents += OnCombatEvents;
            _battle.Client.OnMatchEnded += OnMatchEnded;
            _battle.Client.OnReconnectResponse += OnReconnectResponse;
            _battle.Client.OnPhaseChanged += OnPhaseChanged;
            // 托管异常捕获（T0-2）：Exception/Assert 级日志 → 客户端现场包（任意线程可能触发，
            // 写盘走 _diagnosticsGate 与 mismatch 路径互斥）
            UnityEngine.Application.logMessageReceivedThreaded += OnUnityLogForDiagnostics;
            _matchScope.Register(new DelegatedDisposable(() =>
            {
                _battle.Client.OnStartGame -= OnStartGame;
                _battle.Client.OnSnapshot -= OnSnapshot;
                _battle.Client.OnCombatEvents -= OnCombatEvents;
                _battle.Client.OnMatchEnded -= OnMatchEnded;
                _battle.Client.OnReconnectResponse -= OnReconnectResponse;
                _battle.Client.OnPhaseChanged -= OnPhaseChanged;
                UnityEngine.Application.logMessageReceivedThreaded -= OnUnityLogForDiagnostics;
            }));

            // **补上迟到的 StartGame**：OnStartGame 是边缘事件，可能在应用层建好订阅之前就到达
            // （开局广播由房主发起，与本地建对象/重连补帧存在竞态）。不补的话本上下文会永远
            // 停在"Sim 未建"：每帧只泵网络、不建世界、不发输入，表现为"进了对局但画面不动"。
            var late = _battle.Client.StartGame;
            if (late != null) OnStartGame(late);
            if (_battle.Client.MatchResult != null) OnMatchEnded(_battle.Client.MatchResult);
        }

        /// <summary>
        /// 挂上表现视图（须在 StartGame 之后调用：SimView 要本地预测态 <see cref="RollbackSim.State"/>）。
        /// 视图生命周期由调用方（ProcedureBattle 的视图域）随 Match Scope 收尾；本方法只做接线与首帧对齐。
        /// </summary>
        public void AttachView(SimView view)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BattleContext));
            View = view ?? throw new ArgumentNullException(nameof(view));

            // 回滚/和解 → 视图静默闸；帧事件在**逻辑帧边界**交付（事件是帧内瞬态，事后轮询读不到）
            if (_sim != null)
            {
                _sim.OnRollback = View.OnRollback;
                _sim.OnReconcile = View.OnReconcile;
                _sim.OnFrameEvents = View.OnFrameEvents;
                View.AlignLocal(_localEntityId);
            }
        }

        /// <summary>
        /// 挂输入源（<see cref="IInputService"/> 的注入点；null = 恢复空输入）。
        /// 与 ctor 的 inputProvider 同义，供"视图建在上下文之后"的装配序使用。
        /// </summary>
        public void AttachInput(IInputService provider) => _inputs.Input = provider;

        /// <summary>本地玩家**预测**位置（诊断/瞄准原点族——不读视图 Transform，避免平滑误差回灌输入）。
        /// 未对齐/未开局时返回原点。</summary>
        public SimVector3 LocalPosition
        {
            get
            {
                if (_sim == null || _localEntityId == 0) return default;
                return _sim.State.TryResolve(_localEntityId, out int slot) ? _sim.State.Entities[slot].Pos : default;
            }
        }

        /// <summary>
        /// 客户端输入提前量（帧）：本地采样后按 <c>Frame + 本值</c> 提前上行，服务器 InputGate 预存未来帧。
        /// 必须覆盖 RTT + 发包抖动（实测 RTT≈34ms≈2 帧，取 3 帧留余量）；属客户端传输时序，
        /// **不改 Sim 常量、不牵动共享 BuildHash / Room 发布**。代价是本地输入延迟增加约 2 帧。
        /// </summary>
        public const int InputLeadFrames = 3;

        /// <summary>
        /// 每帧驱动（唯一驱动入口）：网络双泵 → 输入上行与预测推进 → 表现视图。
        /// Sim 未建（StartGame 未达）时只泵网络；输入只在对局中消费与发送。
        ///
        /// **输入的门与顺序**（《角色状态与动作专项设计》§3 输入三件）：采样与上下文门已在渲染帧由
        /// <see cref="IInputService.SampleOnRenderFrame"/> 完成（拦截源成立 → 本帧输入为全零，照常上行）。
        /// 本方法按"网络泵 → 输入三件（<see cref="BattleInputPipeline.FeedAndSendInputs"/>，**先于本地服步进**）
        /// → 本地服步进 → 预测推进 → 追帧补发（<see cref="BattleInputPipeline.ResendCarriedFrames"/>）"的次序做，
        /// 顺序不可换。逻辑帧消费门（同一帧只取一次）与上行节流都在服务内，本方法按 <c>Frame+InputLeadFrames</c> 请求。
        /// </summary>
        public void Tick(float realDelta)
        {
            if (_disposed) return;
            _battle.TickIncoming();

            // ① 输入三件（采样入史 → 同一份上行 → 沿用帧预发）——必须**先于**本地服步进（顺序理由见管线类注释）。
            // viewFrame = 视图当前渲染帧（jitter buffer 帧轴）：开火回溯必须以"玩家看到的过去"为锚。
            int inputFrame = _sim != null ? _sim.State.Frame + InputLeadFrames : -1;
            int viewFrame = ViewRenderFrame;
            if (_sim != null) _inputs.FeedAndSendInputs(_sim, _localEntityId, inputFrame, realDelta, viewFrame);

            _battle.TickOutgoing();          // ② 本地服步进（含快照同步投递）

            if (_sim == null || _ended != 0) return;

            _sim.Tick(realDelta);
            Log.SetFrame(_sim.State.Frame);           // T1 日志关联：帧轴随权威+预测推进（int 赋值，零分配）

            _inputs.ResendCarriedFrames(_sim, _localEntityId, inputFrame, viewFrame);

            View?.Tick(realDelta);               // 表现视图（网络/Sim 之后：本帧权威已应用）
        }

        /// <summary>主动离场（幂等）——流程层据 <see cref="Ended"/> 收尾回 Main。</summary>
        public void Leave() => End(EndReason.Leave);

        private void End(EndReason reason)
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0) return;   // 恰好一次
            EndedReason = reason;
            Ended?.Invoke(reason);
        }

        // ---- 网络事件处理 ----

        /// <summary>StartGame：先过**玩法数据身份校验**（与服务器 `ConfigHash` 复算比对，不等即拒局——
        /// 见方法内注释），再按服务器 seed 重建同构世界（预测的前提——两端世界构造必须逐位一致）。</summary>
        private void OnStartGame(LiteNet.Proto.StartGame sg)
        {
            if (_disposed || _sim != null) return;   // 幂等（重连不重发 StartGame）
            _matchId = sg.MatchId ?? string.Empty;
            // T1 日志关联：开局即登记对局/席位，此后同局日志都带得上（清理在 Dispose，只清自己那局）
            Log.SetMatchContext(_matchId, PlayerId);

            var world = new SimWorldState { RngState = (ulong)sg.Seed };
            if (sg.Match != null) world.Match = SnapshotReassembler.FromProto(sg.Match);
            CombatValues values = CombatConfig.Loaded;   // 装载实例（技术债 #1：房间/世界构造按实例取值）
            WeaponTable weapons = WeaponConfig.Loaded;   // 武器表实例（同链显式传递）

            // 活动局规则持有件：一次性绑定（StartGame 建点）——此后热路径只读注入实例，判定不碰静态。
            // 入口初值同步盖进本地数值副本（与本地服房间快照同口径；null = 无测试规则）。
            // 联机测试房批：权威规则位随 StartGame 快照下发（`gm_flags`）。**不写回 `CombatValues`**——
            // 摘要（`ConfigHash`）不含运行期 GM 位；写回会让本地复算与服务器不一致而误拒局。
            int gmFlags = sg.Match?.GmFlags ?? 0;
            MatchTestRules rules = SimTestRules.ActiveMatch;
            if (gmFlags != 0)
            {
                rules ??= new MatchTestRules();
                rules.NoDeath = (gmFlags & MatchTestRules.FlagNoDeath) != 0;
                rules.InfiniteAmmo = (gmFlags & MatchTestRules.FlagInfiniteAmmo) != 0;
                SimTestRules.ActiveMatch = rules;
            }
            else if (rules != null)
            {
                // 本地服形态（无权威位）：入口初值盖进本地数值副本（与本地服房间快照同口径）
                values.NoDeath = rules.NoDeath;
                values.InfiniteAmmo = rules.InfiniteAmmo;
            }

            // **玩法数据身份校验（批 E）**：服务端 `StartGame.ConfigHash` = 房间固定快照摘要
            // （判定面表字节 + 本局规则位，见 `CombatConfigDigest`）；本地按同一算法、同一份**本地装载字节**
            // 复算——不等 = 两端判据不同源（表数据漏更/回退一端：服务端换表未同批、客户端内容热更滞后……）。
            // 带着分叉数据开局必然和解风暴，故**拒局**：留 Error 痕迹（双方摘要 + 基摘要）→ Ended(SessionFailed)
            // 交流程层回 Main（与"版本不符"同口径——该原因的处理路径已存在）。
            uint expectedDigest = CombatConfigDigest.WithTestRules(_gameplayDigestBase, values.NoDeath, values.InfiniteAmmo);
            if (sg.ConfigHash != expectedDigest)
            {
                Log.Error(
                    $"玩法数据身份不符:StartGame.ConfigHash=0x{sg.ConfigHash:x8} 本地复算=0x{expectedDigest:x8}" +
                    $"（本地基摘要=0x{_gameplayDigestBase:x8} noDead={values.NoDeath} infiniteAmmo={values.InfiniteAmmo}）" +
                    "——两端判定面表数据不同源（服务端换表重启与客户端内容热更未同批？）；本局拒绝建立", LogTag.Battle);
                CaptureDiagnosticsBundle(0, "config-digest-mismatch");
                End(EndReason.SessionFailed);
                return;
            }

            uint used = 0u;
            for (int i = 0; i < ExpectedPlayers; i++)
            {
                int spawnIndex = SpawnPointSelector.Pick(_map, used,
                    SpawnPointSelector.InitialHash((ulong)sg.Seed, sg.MatchId, i));
                used |= 1u << spawnIndex;
                SimVector3 spawn = _map.SpawnPoints[spawnIndex];
                world.Spawn(new EntitySlot { Hp = values.EntityHp, Pos = spawn, Yaw = 0f,
                    Flags = EntityFlags.Player, SpawnPointIndex = spawnIndex }, out int _);
            }

            _configDigest = expectedDigest;

            var template = new SimInputFrame[ExpectedPlayers];
            for (int i = 0; i < ExpectedPlayers; i++) template[i].EntityId = i;   // 服务器覆写防伪；本地预测按 playerId 对齐

            // resolveCombat:false = 客户端预测模式：开火/开火窗/弹药照常预测，但命中/伤害/死亡/
            // 击杀不落本地态（《状态同步专项设计》§6.1/§6.2——权威结果只由快照/权威反馈面驱动）。
            _sim = new RollbackSim(world, _map, template, values, weapons, rules, resolveCombat: false);

            // 视图若已挂（AttachView 早于 StartGame），补上回滚/和解/帧事件接线
            if (View != null)
            {
                _sim.OnRollback = View.OnRollback;
                _sim.OnReconcile = View.OnReconcile;
                _sim.OnFrameEvents = View.OnFrameEvents;
            }
        }

        private void OnMatchEnded(LiteNet.Proto.MatchEnded result)
        {
            if (_disposed) return;
            // 未开局即收到终局（Sim 未建）＝等待期房间被服务器中止：按 Aborted 收尾——流程层
            // 回 Main、房间页替换回大厅（页面流对非 Completed 的处置），不进结算页（空结果无从展示）。
            // 对局中（Sim 已建）的 MatchEnded 照旧 Completed（结算页判据）。
            End(_sim == null ? EndReason.Aborted : EndReason.Completed);
        }

        /// <summary>快照：镜像重建 → 和解（比对/覆盖/重放）→ 视图插值源推进。首份快照对齐本地实体 Id。</summary>
        private void OnSnapshot(LiteNet.Proto.StateSnapshot snapshot)
        {
            if (_disposed || _sim == null) return;   // Sim 未建：丢弃（Reliable StartGame 随后即到）

            ResolveLocalEntity(snapshot);
            // 联机测试房批：权威规则位随每份快照下发——同步到活动规则实例（RollbackSim 持同一引用，
            // 改字段即生效）。联机形态下这是唯一写口：客户端面板动本地副本不会自行生效。
            ApplyAuthoritativeGmFlags(snapshot.Match?.GmFlags ?? 0);

            if (_mirror.ApplyAndReconcile(_sim, snapshot))
            {
                _reconcileCount++;
                _battle.Client.SendMismatch(snapshot.Frame);
                CaptureDiagnosticsBundle(snapshot.Frame, "checksum-mismatch");
            }

            _mirror.PushToView(View);                // 视图插值源：上一份/最新一份轮转
        }

        /// <summary>
        /// Unity 托管异常钩子（任意线程）：Exception/Assert → 客户端现场包。过滤与限频在
        /// <see cref="ClientDiagnosticsBundle.CaptureUnityException"/>（Log/Warning/Error 不捕获——
        /// Error 是可恢复面，逐条落盘会被常规错误刷爆限频额度）。
        /// </summary>
        private void OnUnityLogForDiagnostics(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (_disposed) return;
            try
            {
                lock (_diagnosticsGate)
                {
                    DiagnosticsBundleWriter writer = EnsureDiagnosticsWriter();
                    int frame = _sim?.State?.Frame ?? 0;
                    ClientDiagnosticsBundle.CaptureUnityException(writer, condition, stackTrace, type,
                        _matchId, frame, _localEntityId, (int)Math.Min(_reconcileCount, int.MaxValue),
                        _configDigest);
                }
            }
            catch
            {
                // 诊断钩子不得把异常回灌 Unity 日志（递归触发自身）
            }
        }

        private void CaptureDiagnosticsBundle(int frame, string reason)
        {
            try
            {
                lock (_diagnosticsGate)
                {
                    DiagnosticsBundleWriter writer = EnsureDiagnosticsWriter();

                    // 限频是预期行为（每局上限由 writer 控制）：只在首次提示一次，避免 mismatch 每帧刷 Warning
                    if (!writer.CanAccept(_matchId))
                    {
                        if (!_diagnosticsRateLimited)
                        {
                            _diagnosticsRateLimited = true;
                            Log.Info("诊断现场包同局限频，后续不再落包（每局上限由 writer 控制）", LogTag.Battle);
                        }
                        return;
                    }

                    string path = ClientDiagnosticsBundle.Capture(writer, reason, _matchId, frame,
                        _localEntityId, (int)Math.Min(_reconcileCount, int.MaxValue), _configDigest);

                    if (path != null) Log.Info("诊断现场包已落盘: " + path, LogTag.Battle);
                    else Log.Warning("诊断现场包写出失败（IO/权限）", LogTag.Battle);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("诊断现场包异常: " + ex.Message, LogTag.Battle);
            }
        }

        /// <summary>现场包写入器惰性构造（须在 _diagnosticsGate 内调用——mismatch 与异常钩子双线程安全）。</summary>
        private DiagnosticsBundleWriter EnsureDiagnosticsWriter()
        {
            DiagnosticsBundleWriter writer = _diagnosticsBundles;
            if (writer == null)
            {
                writer = new DiagnosticsBundleWriter(Path.Combine(
                    UnityEngine.Application.persistentDataPath, "diagnostics"));
                _diagnosticsBundles = writer;
            }
            return writer;
        }

        /// <summary>
        /// 公共战斗事件批（独立包）：去重后按 kind 分流到表现面队列。
        /// **与快照解耦**：事件面丢了不影响状态（下次批还带窗口），状态面丢了也不影响事件——
        /// 两条平面各有自己的丢包收敛，这正是拆包的意义。
        /// 命中/击杀/开火的**顺序稳定性**由 <see cref="PublicCombatFeed"/> 的分类 FIFO 保证；
        /// "命中先于死亡"的听感顺序由消费者（<c>BattleViewRig.AuthoritativeCombatFeedback</c>）按先在
        /// 命中队列取完再取击杀队列维持（同帧击杀）。
        /// </summary>
        private void OnCombatEvents(LiteNet.Proto.CombatEventBatch batch)
        {
            if (_disposed) return;
            _combatFeed.Ingest(batch);
        }

        /// <summary>
        /// 权威 GM 规则位 → 活动规则实例（联机测试房批）。常规房（flags=0）不创建规则对象——热路径零分配。
        /// 复用同一实例改字段是**有意**的：<see cref="RollbackSim"/> 构造时持有该引用，运行期无需重建。
        /// </summary>
        private static void ApplyAuthoritativeGmFlags(int flags)
        {
            MatchTestRules rules = SimTestRules.ActiveMatch;
            if (rules == null)
            {
                if (flags == 0) return;
                rules = new MatchTestRules();
                SimTestRules.ActiveMatch = rules;
            }
            rules.NoDeath = (flags & MatchTestRules.FlagNoDeath) != 0;
            rules.InfiniteAmmo = (flags & MatchTestRules.FlagInfiniteAmmo) != 0;
        }

        /// <summary>
        /// 重连恢复（§9.3 步骤 3~5）：仅 Restoring 相位可应用——权威全量重建镜像 → 和解到该帧 →
        /// <see cref="RoomClient.CompleteRestore"/> 发恢复 ACK（服务器收到前抑制本席位增量广播）。
        /// </summary>
        private void OnReconnectResponse(LiteNet.Proto.ReconnectResponse response)
        {
            if (_disposed) return;
            if (!response.Ok || _battle.Phase != ClientSessionPhase.Restoring) return;

            // 重连：不重播断线前事件（恢复后的第一份事件批按"首份"处理——只标记已见）
            _combatFeed.Reset();

            if (_localEntityId == 0 && _battle.Client.PlayerId >= 0)
                ResolveLocalEntity(response.Snapshot);

            _mirror.ApplyReconnect(_sim, response.Snapshot);

            View?.ResetInterpolation();              // 旧时间线快照作废：重连后从恢复快照重新起锚
            _mirror.PushToView(View);                // 恢复快照同样是插值源（重连后不跳帧）
            View?.AlignLocal(_localEntityId);        // 重连可能换了实体/复活的命：重新落位

            _battle.Client.CompleteRestore();
        }

        /// <summary>相位迁移：断线自动重连（凭票据）；Failed → 对局终结（流程层裁决去留）。</summary>
        private void OnPhaseChanged(ClientSessionPhase from, ClientSessionPhase to)
        {
            if (_disposed) return;
            switch (to)
            {
                case ClientSessionPhase.SuspectedLost:
                    _battle.BeginReconnect();            // false = 无票据（内部已转 Failed，下一事件收口）
                    break;
                case ClientSessionPhase.Failed:
                    End(EndReason.SessionFailed);
                    break;
            }
        }

        /// <summary>首份快照按 <c>Slot == PlayerId</c> 解析本地实体 Id（视图首帧直接落位——不从上一条命的位置飞过去）。</summary>
        private void ResolveLocalEntity(LiteNet.Proto.StateSnapshot snapshot)
        {
            if (_localEntityId != 0 || _battle.Client.PlayerId < 0) return;
            foreach (LiteNet.Proto.SlotDelta slot in snapshot.Slots)
            {
                if (slot.Slot == _battle.Client.PlayerId)
                {
                    _localEntityId = slot.Id;
                    View?.AlignLocal(_localEntityId);
                    break;
                }
            }
        }

        /// <summary>拆订阅 → Match Scope.Dispose（LIFO 释放对局资源）。不触发 <see cref="Ended"/>。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sim = null;
            Log.ClearMatchContext(_matchId);          // 只清本局（旧局晚拆不清新局）
            _mirror.Reset();
            _matchScope.Dispose();                        // 订阅退订经 Scope 登记面执行（含 DisposeFailures 聚合）
        }
    }
}
