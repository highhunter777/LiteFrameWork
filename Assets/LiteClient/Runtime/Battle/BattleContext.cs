using System;
using System.Threading;
using LiteFramework;
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
        public LiteNet.Proto.MatchEnded MatchResult => _battle.Client.MatchResult;

        /// <summary>本地玩家实体 Id（0 = 尚未对齐——首份快照按 Slot==PlayerId 解析）。</summary>
        public long LocalEntityId => _localEntityId;

        /// <summary>本地预测/和解 Sim（StartGame 后可用；null = 对局尚未建立）。</summary>
        public RollbackSim Sim => _sim;

        /// <summary>判定用地图（两端单源：同一份 `Map/tbmap.bytes` 经共享投影 `MapTableProjection` 产出
        /// ——服务端 `ServerMapLoader`／客户端 `IMapProvider` 同代码同数据；表现消费者读它——
        /// 瞄准激光的障碍射线等 View 侧判定共享同一份，不另建第二事实源）。</summary>
        public SimMapData Map => _map;

        /// <summary>最近一次收到的快照帧号（视点帧推导来源——SimView 消费）。</summary>
        public int LastSnapshotFrame => _battle.Client.LastSnapshotFrame;

        /// <summary>和解次数（本地预测被权威覆盖的次数；诊断/DevHUD）。</summary>
        public long ReconcileCount => _reconcileCount;

        /// <summary>公共击杀面（全局击杀提示的待展示队列；HUD 驱动逐条取用）。</summary>
        public PublicKillFeed KillFeed => _killFeed;

        /// <summary>公共开火面（远端开火火光的待播队列；表现驱动逐条取用）。</summary>
        public PublicFireFeed FireFeed => _fireFeed;

        /// <summary>网络质量观测（会话面透传——HUD 展示；表现/诊断面，不参与判定）。</summary>
        public NetworkQuality NetworkQuality => _battle.Quality;

        private readonly BattleClient _battle;
        private readonly ClientScope _matchScope;
        private readonly SimMapData _map;
        private readonly uint _gameplayDigestBase;        // 本地装载产物的玩法数据身份基摘要（批 E 校验点用）
        private readonly BattleInputPipeline _inputs;      // 输入三件（顺序与上行）
        private readonly BattleSnapshotMirror _mirror = new BattleSnapshotMirror();   // 持久镜像 + 和解 + 插值源
        private readonly PublicKillFeed _killFeed = new PublicKillFeed();             // 公共击杀面（快照携带的最近击杀窗口 → 去重队列）
        private readonly PublicFireFeed _fireFeed = new PublicFireFeed();             // 公共开火面（快照携带的最近开火窗口 → 去重队列）

        private RollbackSim _sim;
        private long _localEntityId;
        private long _reconcileCount;
        private bool _disposed;
        private int _ended;

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
            _battle.Client.OnMatchEnded += OnMatchEnded;
            _battle.Client.OnReconnectResponse += OnReconnectResponse;
            _battle.Client.OnPhaseChanged += OnPhaseChanged;
            _matchScope.Register(new DelegatedDisposable(() =>
            {
                _battle.Client.OnStartGame -= OnStartGame;
                _battle.Client.OnSnapshot -= OnSnapshot;
                _battle.Client.OnMatchEnded -= OnMatchEnded;
                _battle.Client.OnReconnectResponse -= OnReconnectResponse;
                _battle.Client.OnPhaseChanged -= OnPhaseChanged;
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
        /// 每帧驱动（唯一驱动入口）：网络双泵 → 输入上行与预测推进 → 表现视图。
        /// Sim 未建（StartGame 未达）时只泵网络；输入只在对局中消费与发送。
        ///
        /// **输入的门与顺序**（《角色状态与动作专项设计》§3 输入三件）：采样与上下文门已在渲染帧由
        /// <see cref="IInputService.SampleOnRenderFrame"/> 完成（拦截源成立 → 本帧输入为全零，照常上行）。
        /// 本方法按"网络泵 → 输入三件（<see cref="BattleInputPipeline.FeedAndSendInputs"/>，**先于本地服步进**）
        /// → 本地服步进 → 预测推进 → 追帧补发（<see cref="BattleInputPipeline.ResendCarriedFrames"/>）"的次序做，
        /// 顺序不可换。逻辑帧消费门（同一帧只取一次）与上行节流都在服务内，本方法只按 <c>Frame+1</c> 请求。
        /// </summary>
        public void Tick(float realDelta)
        {
            if (_disposed) return;
            _battle.TickIncoming();

            // ① 输入三件（采样入史 → 同一份上行 → 沿用帧预发）——必须**先于**本地服步进（顺序理由见管线类注释）
            int inputFrame = _sim != null ? _sim.State.Frame + 1 : -1;
            if (_sim != null) _inputs.FeedAndSendInputs(_sim, _localEntityId, inputFrame, realDelta);

            _battle.TickOutgoing();          // ② 本地服步进（含快照同步投递）

            if (_sim == null || _ended != 0) return;

            // 视点帧 = 最新快照帧 + 插值帧数（《状态同步专项设计》§3.4.1：玩家所见帧落后最新快照）——
            // 开火回溯对齐参考，随输入发包上行（本渲染帧的输入已在 ① 上行，此处只取用给追帧补发）。
            int snapshotFrame = _battle.Client.LastSnapshotFrame;
            int viewFrame = snapshotFrame >= 0 ? snapshotFrame + SimConfig.InterpFrames : 0;

            _sim.Tick(realDelta);

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

            var world = new SimWorldState { RngState = (ulong)sg.Seed };
            if (sg.Match != null) world.Match = SnapshotReassembler.FromProto(sg.Match);
            CombatValues values = CombatConfig.Loaded;   // 装载实例（技术债 #1：房间/世界构造按实例取值）
            WeaponTable weapons = WeaponConfig.Loaded;   // 武器表实例（同链显式传递）

            // 活动局规则持有件：一次性绑定（StartGame 建点）——此后热路径只读注入实例，判定不碰静态。
            // 入口初值同步盖进本地数值副本（与本地服房间快照同口径；null = 无测试规则）。
            MatchTestRules rules = SimTestRules.ActiveMatch;
            if (rules != null)
            {
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
                End(EndReason.SessionFailed);
                return;
            }

            for (int i = 0; i < ExpectedPlayers; i++)
            {
                SimVector3 spawn = _map.SpawnPoints[i % _map.SpawnPointCount];
                world.Spawn(new EntitySlot { Hp = values.EntityHp, Pos = spawn, Yaw = 0f,
                    Flags = EntityFlags.Player, SpawnPointIndex = i % _map.SpawnPointCount }, out int _);
            }

            var template = new SimInputFrame[ExpectedPlayers];
            for (int i = 0; i < ExpectedPlayers; i++) template[i].EntityId = i;   // 服务器覆写防伪；本地预测按 playerId 对齐

            _sim = new RollbackSim(world, _map, template, values, weapons, rules);

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

            if (_mirror.ApplyAndReconcile(_sim, snapshot))
            {
                _reconcileCount++;
                _battle.Client.SendMismatch(snapshot.Frame);
            }

            _mirror.PushToView(View);                // 视图插值源：上一份/最新一份轮转

            _killFeed.Ingest(snapshot);              // 公共击杀面：最近击杀窗口 → 去重队列（首份快照不重播历史）
            _fireFeed.Ingest(snapshot);              // 公共开火面：最近开火窗口 → 去重队列（首份快照不重播历史）
        }

        /// <summary>
        /// 重连恢复（§9.3 步骤 3~5）：仅 Restoring 相位可应用——权威全量重建镜像 → 和解到该帧 →
        /// <see cref="RoomClient.CompleteRestore"/> 发恢复 ACK（服务器收到前抑制本席位增量广播）。
        /// </summary>
        private void OnReconnectResponse(LiteNet.Proto.ReconnectResponse response)
        {
            if (_disposed) return;
            if (!response.Ok || _battle.Phase != ClientSessionPhase.Restoring) return;

            _killFeed.Reset();                       // 重连：不重播断线前击杀（恢复快照的窗口按"已见"重新标记）
            _fireFeed.Reset();                       // 重连：不重播断线前开火（恢复快照的窗口按"已见"重新标记）

            if (_localEntityId == 0 && _battle.Client.PlayerId >= 0)
                ResolveLocalEntity(response.Snapshot);

            _mirror.ApplyReconnect(_sim, response.Snapshot);

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
            _mirror.Reset();
            _matchScope.Dispose();                        // 订阅退订经 Scope 登记面执行（含 DisposeFailures 聚合）
        }
    }
}
