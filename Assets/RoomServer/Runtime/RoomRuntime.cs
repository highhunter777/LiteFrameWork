using System;
using System.Collections.Generic;
using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 房间运行时（R1：《商业级通用服务端框架总设计》§8.1 RoomRuntime 纯化）——**房间内确定性状态的唯一持有者**：
    /// 权威 Sim + 席位表 + 输入闸门/预存 + 快照回溯源 + 命中回溯 + 输入历史。
    ///
    /// 纯化红线（§8.1 禁止项，L0 纪律扫描 R11 把守）：
    /// - 不引用 Transport / Socket（发送出口归 App 层 SnapshotPipeline）；
    /// - 不读系统时钟（时间只从 <see cref="RoomCommand.Tick"/> 的 nowMs 进入）；
    /// - 不打印 Console、不访问文件/数据库；
    /// - 不见 proto 类型（输入经 <see cref="ClientInputBatch"/> 纯数据进入，输出经 <see cref="RoomOutput"/> 纯事件产出）；
    /// - 单线程执行（调用方保证同线程驱动——R2 的有界 Mailbox/Worker Pool 负责该保证）。
    ///
    /// 输入/输出契约（§8.1）：命令 = AuthenticatedJoin / ClientInput / Disconnect / Rebind / RestoreAck / Tick / Shutdown；
    /// 输出 = Signal（PlayerAdmitted / MatchStarted / SeatRestored）/ JoinRejected / CloseConnection / MatchStateChanged / SettlementReady。
    ///
    /// 生命周期（§9.1，批② 落地）：<see cref="Match"/> 状态机全表 + 等待/对局时限 + 全员离场收尾；
    /// Finishing 冻结 <see cref="MatchResultSummary"/> 并产 SettlementReady（完整结算信封/Outbox/归档归 R3）。
    ///
    /// 重连闭环（§9.2/§9.3，批③ 落地）：Rebind 把席位置 <see cref="SeatPhase.Restoring"/>（原子换绑、
    /// 旧连接失效）；恢复完成 ACK（RestoreAck）置回 Active 并产 SeatRestored——App 管线在 Restoring
    /// 期间抑制该席位增量广播。快照差分/AOI/背压/编码/发送归 App 层 SnapshotPipeline
    /// （§19 禁第二套快照 DTO 的自然推论）。
    /// </summary>
    public sealed class RoomRuntime
    {
        /// <summary>装配参数（容量/房间号/seed 策略）。</summary>
        public readonly RoomConfig Config;

        /// <summary>期望人数（转发 <see cref="Config"/>；席位/输入槽/实体表按此定容）。</summary>
        public int ExpectedPlayers => Config.ExpectedPlayers;

        public readonly string RoomId;
        public readonly SimWorldState AuthSim;          // 权威唯一真相
        public readonly SimMapData Map;
        public readonly SnapshotRing SnapshotHistory;   // 回溯环（容量 LagCompHistory）
        public readonly InputGate Gate;
        public readonly LagCompensator LagComp;         // 命中回溯

        /// <summary>房间创建时刻固定的不可变玩法配置快照（§4/P0-5：替代进程全局可变 CombatConfig）。</summary>
        public readonly FixedCombatConfig FixedConfig;

        /// <summary>席位表：playerId → 席位（定容数组，下标即 playerId）。</summary>
        private readonly PlayerSession[] _seats;
        /// <summary>connectionId → playerId（Disconnect/Rebind 按连接查席位）。</summary>
        private readonly Dictionary<int, int> _connectionToPlayer = new Dictionary<int, int>();
        /// <summary>playerId → 玩家实体 Id（StartMatch 分配）。</summary>
        private readonly long[] _entityIds;
        /// <summary>本帧聚合输入槽（Step 前重灌）。</summary>
        private readonly SimInputFrame[] _frameInputs;
        /// <summary>全体输入历史（重连补发用；§5.6——环容量 <see cref="SimConfig.MaxInputHistory"/>）。</summary>
        private readonly InputHistory _recentInputs;

        /// <summary>待回溯判定的开火输入——按玩家分槽（多人同 tick 开火不能互相覆盖）。</summary>
        private readonly int[] _pendingFireView;
        private readonly int[] _pendingFireAck;
        private readonly bool[] _hasPendingFire;

        public int NextPlayerId;

        /// <summary>Match 状态机（§9.1）。</summary>
        public readonly MatchStateMachine Match = new MatchStateMachine();

        /// <summary>当前阶段（转发 <see cref="Match"/>）。</summary>
        public MatchPhase Phase => Match.Phase;

        /// <summary>是否处于对局进行中（Running）——权威步进与增量广播的唯一判据。</summary>
        public bool Started => MatchStateMachine.IsRunning(Match.Phase);

        /// <summary>是否已进入终态（Closed/Aborted；幂等关闭的判据）。</summary>
        public bool Closed => MatchStateMachine.IsTerminal(Match.Phase);

        public long Seed;

        /// <summary>最近一次 Tick 携带的单调毫秒（seed 派生/批② 超时判定的时间来源）。</summary>
        private long _lastNowMs;

        // ---- Ops 计数 ----
        public long StepsCount;
        public long FireInputsProcessed;        // 走回溯路径的开火输入数

        public RoomRuntime(RoomConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            RoomId = config.RoomId;
            FixedConfig = FixedCombatConfig.Capture();   // 房间创建时固定（此后进程改配置不改本房间）
            Map = BuildStandardMap();
            AuthSim = new SimWorldState();
            SnapshotHistory = new SnapshotRing(SimConfig.LagCompHistory);
            int players = ExpectedPlayers;                     // 配置定容（席位/输入槽/实体表/回溯环一致）
            Gate = new InputGate(players);
            LagComp = new LagCompensator(AuthSim, Map, players, SnapshotHistory);
            _seats = new PlayerSession[players];
            _entityIds = new long[players];
            _frameInputs = new SimInputFrame[players];
            _pendingFireView = new int[players];
            _pendingFireAck = new int[players];
            _hasPendingFire = new bool[players];
            _recentInputs = new InputHistory(SimConfig.MaxInputHistory, players);
        }

        /// <summary>席位（null = 未占）；越界 playerId 返回 null。</summary>
        public PlayerSession SeatOf(int playerId) =>
            playerId >= 0 && playerId < ExpectedPlayers ? _seats[playerId] : null;

        public bool TryGetSeat(int playerId, out PlayerSession seat)
        {
            seat = SeatOf(playerId);
            return seat != null;
        }

        /// <summary>成员号列表（App：JoinAck.Members；playerId 升序）。</summary>
        public int[] MemberIds()
        {
            var ids = new List<int>(NextPlayerId);
            for (int i = 0; i < NextPlayerId; i++)
                if (_seats[i] != null) ids.Add(i);
            return ids.ToArray();
        }

        public long EntityIdOf(int playerId) =>
            playerId >= 0 && playerId < ExpectedPlayers ? _entityIds[playerId] : 0L;

        /// <summary>该帧全体玩家的历史输入（重连补发用——§5.6：权威快照 + 后续输入历史）。</summary>
        public bool HistoryFor(int frame, out SimInputFrame[] inputs)
            => _recentInputs.TryGet(frame, out inputs, out bool[] _);

        /// <summary>
        /// 命令入口（§8.1：Runtime 的**唯一**入口）。输出追加到 <paramref name="outputs"/>
        /// （调用方负责 Clear——复用列表，零分配）。
        /// </summary>
        public void Execute(in RoomCommand cmd, List<RoomOutput> outputs)
        {
            switch (cmd.Kind)
            {
                case RoomCommandKind.AuthenticatedJoin: ExecuteJoin(cmd.ConnectionId, outputs); break;
                case RoomCommandKind.ClientInput: ExecuteInput(cmd.PlayerId, cmd.Input); break;
                case RoomCommandKind.Disconnect: ExecuteDisconnect(cmd.ConnectionId, outputs); break;
                case RoomCommandKind.Rebind: ExecuteRebind(cmd.PlayerId, cmd.NewConnectionId, outputs); break;
                case RoomCommandKind.RestoreAck: ExecuteRestoreAck(cmd.PlayerId, outputs); break;
                case RoomCommandKind.Tick: ExecuteTick(cmd.NowMs, outputs); break;
                case RoomCommandKind.Shutdown: ExecuteShutdown(cmd.Reason, cmd.NowMs, outputs); break;
            }
        }

        private void ExecuteJoin(int connectionId, List<RoomOutput> outputs)
        {
            if (MatchStateMachine.IsTerminal(Match.Phase))
            {
                outputs.Add(new JoinRejectedOutput(connectionId, "房间已关闭"));
                return;
            }
            if (NextPlayerId >= ExpectedPlayers)
            {
                outputs.Add(new JoinRejectedOutput(connectionId, "房间已满"));
                return;
            }

            if (Match.Phase == MatchPhase.Created)                       // 首个连接 → 等待玩家
                Transition(MatchPhase.WaitingForPlayers, "first-join", _lastNowMs, outputs);

            int playerId = NextPlayerId++;
            var seat = new PlayerSession(playerId, connectionId);
            _seats[playerId] = seat;
            _connectionToPlayer[connectionId] = playerId;
            outputs.Add(new SignalOutput(playerId, new PlayerAdmitted(playerId, MemberIds())));

            if (Match.Phase == MatchPhase.WaitingForPlayers && NextPlayerId >= ExpectedPlayers)
                StartMatch(outputs);                                     // 满员 → Starting → Running
        }

        private void ExecuteInput(int playerId, in ClientInputBatch batch)
        {
            if (!Started) return;                                        // 仅 Running 消费输入
            PlayerSession seat = SeatOf(playerId);
            if (seat == null) return;

            bool accepted = Gate.Store(batch, playerId, _entityIds[playerId], AuthSim.Frame,
                out int _, out SimInputFrame acceptedInput);
            if (!accepted) return;

            // 开火 + 带视点帧 → 记下待回溯判定（下一帧步进后执行——那时环里才有"开火帧"的历史态）。
            // 回溯对齐参考（pendingFireAck）用闸门**钳位后**的记账值。
            bool fired = (acceptedInput.Buttons & SimInputFrame.ButtonFire) != 0u;
            if (fired && batch.ViewFrame > 0)
            {
                _pendingFireView[playerId] = batch.ViewFrame;
                _pendingFireAck[playerId] = Gate.LastClampedAckSnapshot;
                _hasPendingFire[playerId] = true;
            }
        }

        private void ExecuteDisconnect(int connectionId, List<RoomOutput> outputs)
        {
            if (!_connectionToPlayer.TryGetValue(connectionId, out int playerId)) return;
            _connectionToPlayer.Remove(connectionId);
            PlayerSession seat = _seats[playerId];
            if (seat == null || seat.ConnectionId != connectionId) return;   // 已被重绑接管的旧连接：忽略
            seat.Phase = SeatPhase.Disconnected;                              // 席位保留（重连窗口内可重绑）
            seat.ConnectionId = -1;

            // 全员离场且对局进行中 → 收尾（冻结结果并关闭；Aborted 只留给错误/运维终止）
            if (Match.Phase == MatchPhase.Running && AllSeatsDisconnected())
                Finish(ShutdownReason.AllPlayersLeft, "all-players-left", outputs);
        }

        private bool AllSeatsDisconnected()
        {
            for (int i = 0; i < NextPlayerId; i++)
                if (_seats[i] != null && _seats[i].Phase != SeatPhase.Disconnected) return false;
            return NextPlayerId > 0;
        }

        private void ExecuteRebind(int playerId, int newConnectionId, List<RoomOutput> outputs)
        {
            if (MatchStateMachine.IsTerminal(Match.Phase)) return;               // 终态房间不再接受重绑（App 已拦 Running，此处兜底）
            PlayerSession seat = SeatOf(playerId);
            if (seat == null) return;
            if (seat.ConnectionId >= 0) _connectionToPlayer.Remove(seat.ConnectionId);   // 旧连接映射失效
            seat.ConnectionId = newConnectionId;
            seat.Phase = SeatPhase.Restoring;    // §9.3 步骤 6：恢复完成 ACK 前抑制该席位增量广播（App 管线按相位过滤）
            seat.RebindGeneration++;
            _connectionToPlayer[newConnectionId] = playerId;
            _ = outputs;   // 重绑本身不产事件（App 负责响应构建）；恢复完成时产 SeatRestored（见 ExecuteRestoreAck）
        }

        /// <summary>
        /// 恢复完成 ACK（§9.3 步骤 6）：Restoring → Active 并产 <see cref="SeatRestored"/>。
        /// 幂等：席位不存在或非 Restoring（未在恢复中/已 Active）时静默忽略——重复 ACK、迟到 ACK 都无副作用。
        /// </summary>
        private void ExecuteRestoreAck(int playerId, List<RoomOutput> outputs)
        {
            PlayerSession seat = SeatOf(playerId);
            if (seat == null || seat.Phase != SeatPhase.Restoring) return;
            seat.Phase = SeatPhase.Active;
            outputs.Add(new SignalOutput(playerId, new SeatRestored(playerId)));
        }

        private void ExecuteTick(long nowMs, List<RoomOutput> outputs)
        {
            _lastNowMs = nowMs;

            if (Match.Phase == MatchPhase.WaitingForPlayers
                && Config.WaitingTimeoutMs > 0 && nowMs - Match.EnteredMs >= Config.WaitingTimeoutMs)
            {
                Abort(ShutdownReason.WaitingTimeout, "waiting-timeout", outputs);
                return;
            }

            if (!Started) return;                                            // 非 Running 不步进
            if (Config.MatchTimeLimitMs > 0 && nowMs - Match.EnteredMs >= Config.MatchTimeLimitMs)
            {
                Finish(ShutdownReason.TimeLimit, "time-limit", outputs);
                return;
            }

            StepFrame();
        }

        private void ExecuteShutdown(ShutdownReason reason, long nowMs, List<RoomOutput> outputs)
        {
            if (MatchStateMachine.IsTerminal(Match.Phase))
            {
                // 幂等：终态重复关闭只记事件，不重迁移（§9.1）
                outputs.Add(new MatchStateChangedOutput(Match.Phase, Match.Phase, "重复关闭（忽略）", RoomId, AuthSim.Frame, nowMs));
                return;
            }

            if (Match.Phase == MatchPhase.Running)
                Finish(reason, "shutdown", outputs);                          // 有对局可收尾 → Finishing→Settling→Closed
            else
                Abort(reason, "shutdown", outputs);                           // 未开局/起始阶段无结果可结算 → Aborted
        }

        /// <summary>收尾：Finishing 冻结结果摘要 → SettlementReady → Settling → Closed（§9.1；不重算玩法数值）。</summary>
        private void Finish(ShutdownReason reason, string detail, List<RoomOutput> outputs)
        {
            long now = _lastNowMs;
            Transition(MatchPhase.Finishing, detail, now, outputs);
            outputs.Add(new SettlementReadyOutput(new MatchResultSummary(RoomId, Seed, AuthSim.Frame, reason, MemberIds())));
            Transition(MatchPhase.Settling, "settle", now, outputs);
            Transition(MatchPhase.Closed, "closed", now, outputs);
        }

        /// <summary>中止（任意阶段可达）：只迁移并产事件，无结算输出。</summary>
        private void Abort(ShutdownReason reason, string detail, List<RoomOutput> outputs)
        {
            Transition(MatchPhase.Aborted, detail + ":" + reason, _lastNowMs, outputs);
        }

        /// <summary>迁移并产结构化事件（§9.1）；非法迁移输出 Reason=Rejected（不改状态、不抛）。</summary>
        private void Transition(MatchPhase to, string reason, long nowMs, List<RoomOutput> outputs)
        {
            MatchPhase from = Match.Phase;
            if (!Match.TryTransition(to, reason, nowMs))
            {
                outputs.Add(new MatchStateChangedOutput(from, from, "Rejected:" + reason, RoomId, AuthSim.Frame, nowMs));
                return;
            }
            outputs.Add(new MatchStateChangedOutput(from, to, reason, RoomId, AuthSim.Frame, nowMs));
        }

        /// <summary>
        /// 开局（§4.5 权威定序——StartMatch 后循环才消费输入）：WaitingForPlayers → Starting → Running，
        /// 世界按种子生成，玩家落出生点。
        /// seed 策略：<see cref="RoomConfig.Seed"/> 非 0 用之；为 0 时由最近一次 Tick 的 nowMs 派生
        /// （R1：系统墙钟兜底已删除，时间只从命令进入）。
        /// </summary>
        private void StartMatch(List<RoomOutput> outputs)
        {
            Transition(MatchPhase.Starting, "full", _lastNowMs, outputs);

            Seed = Config.Seed != 0 ? Config.Seed : DeriveSeed(_lastNowMs);
            AuthSim.RngState = (ulong)Seed;

            for (int i = 0; i < NextPlayerId; i++)
            {
                PlayerSession seat = _seats[i];
                if (seat == null) continue;
                SimVector3 spawn = Map.SpawnPoints[i % Map.SpawnPointCount];
                AuthSim.Spawn(new EntitySlot { Hp = FixedConfig.EntityHp, Pos = spawn, Yaw = 0f }, out int slot);
                _entityIds[i] = AuthSim.Entities[slot].Id;
                seat.EntityId = _entityIds[i];
            }

            Transition(MatchPhase.Running, "started", _lastNowMs, outputs);
            outputs.Add(new SignalOutput(-1, new MatchStarted(Seed, AuthSim.Frame, FixedConfig.Digest)));
        }

        /// <summary>
        /// seed=0 的派生（非零、确定性、无墙钟）：以 Tick 时间戳混入黄金比例常量。
        /// 尚未收到任何 Tick（L1 直驱且未配 Seed）时回退固定常量——测试需确定性应显式配
        /// <see cref="RoomConfig.Seed"/>；生产恒有 Tick（Loop 先于客户端 Join 启动）。
        /// </summary>
        private static long DeriveSeed(long nowMs)
        {
            if (nowMs == 0) return 0x5DEECE66DL;
            unchecked { return ((nowMs ^ (long)0x9E3779B97F4A7C15UL) * 137L) | 1L; }
        }

        /// <summary>
        /// 推进一步权威帧（§4.5-3 权威定序）：消费预存输入（缺席沿用空输入）→ Step → 快照环 Capture
        /// → 记录历史输入 → 回溯判定（本步各玩家 pending 开火逐个处理）。
        /// 快照广播**不在此**——App 层 SnapshotPipeline 在 Tick 之后自行拉取
        /// <see cref="AuthSim"/>/<see cref="Gate"/> 构建下发（§19 禁第二套快照 DTO）。
        /// </summary>
        private void StepFrame()
        {
            int frame = AuthSim.Frame + 1;   // 本步目标帧号（输入按帧号预存——inputDelay=1 语义）
            for (int i = 0; i < ExpectedPlayers; i++)
            {
                // 缺席沿用：断线/未发包成员用空输入（§4.5-2；数组序即 playerId 升序——与帧号语义一致）
                _frameInputs[i] = Gate.TryConsume(frame, i, out SimInputFrame stored) ? stored : default;
            }

            SimStep.Step(AuthSim, Map, _frameInputs);
            StepsCount++;
            int steppedFrame = AuthSim.Frame;
            SnapshotHistory.Capture(steppedFrame, AuthSim);   // 每帧捕获（回溯基料）
            _recentInputs.Record(steppedFrame, _frameInputs, null);   // 重连补发基料（Step 已就地排序 → 规范形）
            LagComp.RecordInputs(steppedFrame, _frameInputs);

            ConsumePendingFire();                             // 回溯判定（本步产生开火输入时）
        }

        /// <summary>
        /// 回溯判定：开火帧的历史态现在已在环里（本步 Capture 覆盖到"开火帧"本身），
        /// 因此可安全回溯到"玩家所见帧"判定，命中命令落到当前帧由下一次 FlushCommands 结算（当帧延迟语义）。
        /// </summary>
        private void ConsumePendingFire()
        {
            for (int playerId = 0; playerId < ExpectedPlayers; playerId++)
            {
                if (!_hasPendingFire[playerId]) continue;
                _hasPendingFire[playerId] = false;

                LagComp.CompensateFire(playerId, _entityIds[playerId], _pendingFireView[playerId], _pendingFireAck[playerId]);
                FireInputsProcessed++;
            }
        }

        /// <summary>标准灰盒地图：±50 边界 + 16 网格出生点——构造单源在 <see cref="SimMapData.StandardBattleMap"/>
        /// （C2：客户端预测世界共用同一构造，两端地图不一致 = 出生点错位 = 预测不分叉收敛）。</summary>
        private static SimMapData BuildStandardMap() => SimMapData.StandardBattleMap();
    }
}
