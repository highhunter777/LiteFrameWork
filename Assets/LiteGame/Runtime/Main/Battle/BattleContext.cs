using System;
using System.Threading;
using LiteFramework;
using LiteNet;
using LiteNet.Protocol;
using LiteSim;
using LiteSim.View;

namespace LiteGame
{
    /// <summary>
    /// 对局上下文（C2 批①，《商业级通用客户端框架总设计》§19 C2"BattleContext 创建/销毁、网络/Sim/View 接线"）：
    /// 一局对局的**全部运行态编排**——网络事件 → 持久镜像重建（协议单源 SnapshotReassembler）→
    /// 预测/和解（RollbackSim）→（批②：SimView/相机/动画）→ 输入上行。
    ///
    /// 逻辑镜像 <c>Tests/LiteNet.Tests/HeadlessClient.cs</c>（已验证的集成形态），差异只有三点：
    /// 传输由 <see cref="BattleClient"/> 持有、地图走 <see cref="SimMapData.StandardBattleMap"/> 单源、
    /// 生命周期挂 Match Scope（本类创建并持有，Dispose 即拆——离场无 Match 残留的载体）。
    ///
    /// **断线恢复策略（C2 会话子集口径）**：SuspectedLost 即自动 <see cref="BattleClient.BeginReconnect"/>
    /// （凭 JoinAck 票据）；Failed（重连超时/拒绝/版本不符）→ <see cref="Ended"/>(SessionFailed) 交流程层
    /// 裁决回主菜单——不在上下文内静默重建会话（正式重试 UI 归 G3）。
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
        }

        /// <summary>首版房间规模（RoomConfig 默认 2 人房——两端 StartGame 世界重建的定容依据）。</summary>
        public const int ExpectedPlayers = 2;

        /// <summary>对局结束（恰好一次；Dispose 不触发——那是清理不是结束）。</summary>
        public event Action<EndReason> Ended;

        public ClientSessionPhase SessionPhase => _battle.Phase;
        public bool Connected => _battle.Connected;
        public int PlayerId => _battle.Client.PlayerId;

        /// <summary>本地玩家实体 Id（0 = 尚未对齐——首份快照按 Slot==PlayerId 解析，HeadlessClient 同口径）。</summary>
        public long LocalEntityId => _localEntityId;

        /// <summary>本地预测/和解 Sim（StartGame 后可用；null = 对局尚未建立）。</summary>
        public RollbackSim Sim => _sim;

        /// <summary>最近一次收到的快照帧号（视点帧推导来源——批② SimView 消费）。</summary>
        public int LastSnapshotFrame => _battle.Client.LastSnapshotFrame;

        /// <summary>和解次数（本地预测被权威覆盖的次数；诊断/DevHUD）。</summary>
        public long ReconcileCount => _reconcileCount;

        private readonly BattleClient _battle;
        private readonly ClientScope _matchScope;
        private readonly SimMapData _map;
        private Func<SimInputFrame> _inputProvider;

        private RollbackSim _sim;
        private SimWorldState _mirror;                       // 持久权威镜像（增量快照只在它上面累积才完整，§5.5）
        private readonly SimWorldStateSnapshot[] _viewSnapshots = new SimWorldStateSnapshot[2];   // 视图插值源（轮转：上一份/最新一份）
        private int _viewSnapWrite;                          // 下一次写入下标（0/1 轮转）
        private long _localEntityId;
        private long _reconcileCount;
        private bool _disposed;
        private int _ended;

        /// <summary>表现视图（C2 批② SimView 建后挂上；null = 无视图——纯会话/测试形态仍完整可跑）。</summary>
        public SimView View { get; private set; }

        /// <param name="inputProvider">本地输入采集（批② PlayerController 注入；null = 空输入——纯会话/测试形态）。</param>
        public BattleContext(BattleClient battle, ClientScope accountScope,
            Func<SimInputFrame> inputProvider = null, SimMapData map = null)
        {
            _battle = battle ?? throw new ArgumentNullException(nameof(battle));
            if (accountScope == null) throw new ArgumentNullException(nameof(accountScope));
            _inputProvider = inputProvider;
            _map = map ?? SimMapData.StandardBattleMap();

            _matchScope = accountScope.CreateChild("Match");

            // 订阅 + 退订登记（LIFO：晚挂先退——Dispose 即拆，不靠调用方记得退订）
            _battle.Client.OnStartGame += OnStartGame;
            _battle.Client.OnSnapshot += OnSnapshot;
            _battle.Client.OnReconnectResponse += OnReconnectResponse;
            _battle.Client.OnPhaseChanged += OnPhaseChanged;
            _matchScope.Register(new DelegatedDisposable(() =>
            {
                _battle.Client.OnStartGame -= OnStartGame;
                _battle.Client.OnSnapshot -= OnSnapshot;
                _battle.Client.OnReconnectResponse -= OnReconnectResponse;
                _battle.Client.OnPhaseChanged -= OnPhaseChanged;
            }));
        }

        /// <summary>
        /// 挂上表现视图（C2 批②——须在 StartGame 之后调用：SimView 要本地预测态 <see cref="RollbackSim.State"/>）。
        /// 视图生命周期由调用方（ProcedureBattle）随 Match Scope 收尾；本方法只做接线与首帧对齐。
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
        /// 挂输入采集口（C2 批②：PlayerController 的注入点；null = 恢复空输入）。
        /// 与 ctor 的 inputProvider 同义，供"视图建在上下文之后"的装配序使用。
        /// </summary>
        public void AttachInput(Func<SimInputFrame> provider) => _inputProvider = provider;

        /// <summary>本地玩家**预测**位置（输入瞄准的参照原点——不读视图 Transform，避免平滑误差回灌输入）。
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
        /// 每帧驱动（ProcedureBattle.OnUpdate 调——唯一驱动入口）：网络双泵 → 输入上行 + 预测推进 → 表现视图。
        /// Sim 未建（StartGame 未达）时只泵网络；输入只在对局中发送。
        /// </summary>
        public void Tick(float realDelta)
        {
            if (_disposed) return;
            _battle.TickIncoming();
            _battle.TickOutgoing();

            if (_sim == null) return;

            var local = _inputProvider != null ? _inputProvider() : default;
            local.EntityId = _localEntityId;

            // 视点帧 = 最新快照帧 + 插值帧数（《状态同步专项设计》§3.4.1：玩家所见帧落后最新快照）
            int snapshotFrame = _battle.Client.LastSnapshotFrame;
            int viewFrame = snapshotFrame >= 0 ? snapshotFrame + SimConfig.InterpFrames : 0;

            if (_battle.Connected)
                _battle.Client.SendInput(_sim.State.Frame + 1, local, viewFrame: viewFrame);

            _sim.Tick(realDelta);
            View?.Tick(realDelta);               // 表现视图（网络/Sim 之后：本帧权威已应用）
        }

        /// <summary>主动离场（幂等）——流程层据 <see cref="Ended"/> 收尾回 Main。</summary>
        public void Leave() => End(EndReason.Leave);

        private void End(EndReason reason)
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0) return;   // 恰好一次
            Ended?.Invoke(reason);
        }

        // ---- 网络事件处理（HeadlessClient 已验证形态的镜像）----

        /// <summary>StartGame：按服务器 seed 重建同构世界（预测的前提——两端世界构造必须逐位一致）。</summary>
        private void OnStartGame(LiteNet.Proto.StartGame sg)
        {
            if (_disposed || _sim != null) return;   // 幂等（重连不重发 StartGame）

            var world = new SimWorldState { RngState = (ulong)sg.Seed };
            for (int i = 0; i < ExpectedPlayers; i++)
            {
                SimVector3 spawn = _map.SpawnPoints[i % _map.SpawnPointCount];
                world.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = spawn, Yaw = 0f }, out int _);
            }

            var template = new SimInputFrame[ExpectedPlayers];
            for (int i = 0; i < ExpectedPlayers; i++) template[i].EntityId = i;   // 服务器覆写防伪；本地预测按 playerId 对齐

            _sim = new RollbackSim(world, _map, template);

            // 批②：视图若已挂（AttachView 早于 StartGame），补上回滚/和解/帧事件接线
            if (View != null)
            {
                _sim.OnRollback = View.OnRollback;
                _sim.OnReconcile = View.OnReconcile;
                _sim.OnFrameEvents = View.OnFrameEvents;
            }
        }

        /// <summary>
        /// 快照：镜像重建 → 和解（checksum 比对/覆盖/重放）→ 无和解时刷新非预测量（弹药/CD/比赛状态——
        /// P0 分层：不预测的量只能随包来）→ 视图插值源推进。首份快照对齐本地实体 Id。
        /// </summary>
        private void OnSnapshot(LiteNet.Proto.StateSnapshot snapshot)
        {
            if (_disposed || _sim == null) return;   // Sim 未建：丢弃（Reliable StartGame 随后即到）

            ResolveLocalEntity(snapshot);

            _mirror = _mirror ?? new SimWorldState();
            SnapshotReassembler.Apply(snapshot, _mirror, out uint checksum);
            if (_sim.OnAuthoritativeSnapshot(snapshot.Frame, _mirror, checksum))
            {
                _reconcileCount++;
                _battle.Client.SendMismatch(snapshot.Frame);
            }
            else
            {
                SnapshotReassembler.OverlayPrivateAndMatch(snapshot, _sim.State);
            }

            PushViewSnapshot();                      // 视图插值源：上一份/最新一份轮转
        }

        /// <summary>
        /// 重连恢复（§9.3 步骤 3~5）：仅 Restoring 相位可应用——权威全量重建镜像 → 和解到该帧 →
        /// <see cref="RoomClient.CompleteRestore"/> 发恢复 ACK（服务器收到前抑制本席位增量广播）。
        /// </summary>
        private void OnReconnectResponse(LiteNet.Proto.ReconnectResponse response)
        {
            if (_disposed) return;
            if (!response.Ok || _battle.Phase != ClientSessionPhase.Restoring) return;

            _mirror = _mirror ?? new SimWorldState();
            SnapshotReassembler.Apply(response.Snapshot, _mirror, out uint checksum);

            if (_localEntityId == 0 && _battle.Client.PlayerId >= 0)
                ResolveLocalEntity(response.Snapshot);

            if (_sim != null)
                _sim.OnAuthoritativeSnapshot(response.Snapshot.Frame, _mirror, checksum);

            PushViewSnapshot();                      // 恢复快照同样是插值源（重连后不跳帧）
            View?.AlignLocal(_localEntityId);        // 重连可能换了实体/复活的命：重新落位

            _battle.Client.CompleteRestore();
        }

        /// <summary>
        /// 把当前镜像拷进视图插值源（两块轮转）。用 <see cref="SimWorldStateSnapshot.CaptureFull"/>
        /// 而非增量摘要——视图要的是**完整**槽位，而 <c>SnapshotReassembler</c> 只保证"缺席 = 未变化"
        /// （增量包的槽位可能没被这帧指到，必须从持久镜像整体取）。
        /// </summary>
        private void PushViewSnapshot()
        {
            if (View == null) return;
            var snap = _viewSnapshots[_viewSnapWrite];
            if (snap == null)
            {
                snap = new SimWorldStateSnapshot();
                _viewSnapshots[_viewSnapWrite] = snap;
            }
            snap.CaptureFull(_mirror);
            _viewSnapWrite ^= 1;                     // 轮转：下一份写另一块，保留当前这份供插值

            View.OnAuthoritativeSnapshot(snap);
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

        private void ResolveLocalEntity(LiteNet.Proto.StateSnapshot snapshot)
        {
            if (_localEntityId != 0 || _battle.Client.PlayerId < 0) return;
            foreach (LiteNet.Proto.SlotDelta slot in snapshot.Slots)
            {
                if (slot.Slot == _battle.Client.PlayerId)
                {
                    _localEntityId = slot.Id;
                    View?.AlignLocal(_localEntityId);     // 视图首帧直接落位（不从上一条命的位置飞过去）
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
            _mirror = null;
            _matchScope.Dispose();                        // 订阅退订经 Scope 登记面执行（含 DisposeFailures 聚合）
        }
    }
}
