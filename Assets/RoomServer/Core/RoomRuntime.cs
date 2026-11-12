using System;
using System.Collections.Generic;
using LiteSim;
using LiteSync.Shared;
using RoomServer.Runtime.Diagnostics;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 房间运行时（R1：《商业级通用服务端框架总设计》§8.1 RoomRuntime 纯化）——**房间内确定性状态的唯一持有者**： 权威 Sim + 席位表 + 输入闸门/预存 + 快照回溯源 + 命中回溯 + 输入历史。
    /// </summary>
    public sealed class RoomRuntime
    {
        /// <summary>装配参数（容量/房间号/seed 策略）。</summary>
        public readonly RoomConfig Config;

        /// <summary>期望人数（转发 <see cref="Config"/>；席位/输入槽/实体表按此定容）。</summary>
        public int ExpectedPlayers => Config.ExpectedPlayers;

        /// <summary>本房地图行键（转发 <see cref="Config"/>；多图批——随 JoinAck 权威下发，0 = 未标注）。</summary>
        public int MapId => Config.MapId;

        /// <summary>取走本局录制（仅房间静默后调用；重复调用返回同一份）。null = 未启用录制。</summary>
        public RoomRunRecord TakeRunRecord() => _runRecorder?.Seal();

        /// <summary>
        /// 请求录制器即时快照（任意线程可调——mismatch 上报路径）：置位后由房间线程在下一
        /// StepFrame 末尾封印独立快照并回调宿主（异步落盘），录制本身不受影响。
        /// </summary>
        public void RequestRecordSnapshot() => _runRecorder?.RequestSnapshot();

        public readonly string RoomId;
        /// <summary>宿主分配的局标识；直驱测试可显式注入，缺省沿用 RoomId。</summary>
        public readonly string MatchId;
        public readonly SimWorldState AuthSim;          // 权威唯一真相
        public readonly SimMapData Map;
        public readonly SnapshotRing SnapshotHistory;   // 回溯环（容量 LagCompHistory）
        public readonly InputGate Gate;
        public readonly LagCompensator LagComp;         // 命中回溯

        /// <summary>
        /// 公共战斗事件窗口（**唯一的公共事件面**：命中/暴击/死亡/开火/着墙/复活全在这一条链上）。
        /// </summary>
        public readonly CombatEventWindow Events;

        /// <summary>房间创建时刻固定的不可变玩法配置快照（§4/P0-5：替代进程全局可变 CombatConfig）。</summary>
        public readonly FixedCombatConfig FixedConfig;
        public readonly MatchRules Rules;

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

        /// <summary>该开火输入对应的目标逻辑帧（C4 修复：defer 位只挂到这一帧，不再无条件挂 Frame+1）。</summary>
        private readonly int[] _pendingFireFrame;

        /// <summary>席位分配高水位（最高已分配席位号 + 1；**只升不降**——未开局离场释放席位后仍保持，
        /// 作为各处席位遍历的上界。真值语义见 <see cref="AllocatedSeats"/>）。</summary>
        public int NextPlayerId;

        /// <summary>在册席位数（已分配且未释放——未开局离场即释放；对局中含断线保留席位）。
        /// Lobby 房间列表"人数"的上报口径——区别于高水位（曾发放过即计数，会把已离场者虚报）。</summary>
        public int AllocatedSeats
        {
            get
            {
                int count = 0;
                for (int i = 0; i < NextPlayerId; i++)
                    if (_seats[i] != null) count++;
                return count;
            }
        }

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

        /// <summary>测试房规则持有件（null = 常规对局）——与客户端预测同实例（GM 中局开合两端同值）。
        /// **非 readonly**（联机测试房批）：运行期 GM 开关经 <see cref="SetTestRules"/> 在权威循环内替换引用。</summary>
        private MatchTestRules _testRules;

        /// <summary>本局命令日志录制器（null = 关闭；纯内存，R11 安全）。</summary>
        private readonly RoomRunRecorder _runRecorder;

        // ---- Ops 计数 ----
        public long StepsCount;
        public long FireInputsProcessed;        // 走回溯路径的开火输入数

        /// <summary>
        /// <paramref name="testRules"/> = 测试房规则持有件（LiteGame 本地服形态注入； null = 常规对局——免死/无限子弹关闭）。
        /// </summary>
        public RoomRuntime(RoomConfig config, CombatValues? combat = null, WeaponTable weapons = null, string matchId = null,
            MatchTestRules testRules = null, uint gameplayDigestBase = 0, RoomRunRecorder recorder = null)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            RoomId = config.RoomId;
            MatchId = matchId ?? RoomId;
            _runRecorder = recorder;
            // 广播参数（房级）——范围/缺省在装载期与这里双重把守：坏配置拒建房，不静默钳制。
            if (config.SnapshotHz < ProtocolConstants.MinSnapshotHz || config.SnapshotHz > ProtocolConstants.MaxSnapshotHz)
                throw new ArgumentException(
                    $"RoomConfig.SnapshotHz 越界（允许 {ProtocolConstants.MinSnapshotHz}..{ProtocolConstants.MaxSnapshotHz}）："
                    + config.SnapshotHz, nameof(config));
            if (config.Aoi == null)
                throw new ArgumentException("RoomConfig.Aoi 必填（缺省 AoiSettings.Default，不得为 null）", nameof(config));
            // 玩法数值/武器表实例：装配方显式传入（技术债 #1——不再从全局静态面 capture）；缺省=默认表值
            // （嵌入式/测试形态）。生产链：HostAssembly.Inputs（必填）→ ServerHost → RoomInstance。
            CombatValues values = combat ?? CombatValues.Default;
            WeaponTable table = weapons ?? WeaponTable.Default;
            _testRules = testRules;
            // 房间创建时固定（此后进程任何变化不改本房间）；摘要 = 表字节基摘要 + 本局规则初值同刻绑定。
            FixedConfig = FixedCombatConfig.Capture(values, table, gameplayDigestBase);
            Rules = new MatchRules(config.Rules.DurationFrames, config.Rules.KillLimit,
                config.Rules.RespawnDelayFrames, config.Rules.SpawnProtectionFrames);
            // 判定地图：装配注入必填（D22——回退构造已删；缺图 = 无图不得起权威局，fail-fast 不静默兜底）。
            Map = config.Map ?? throw new ArgumentException(
                "RoomConfig.Map 必填——装配经 ServerMapLoader 装载注入（D22：无回退地图）", nameof(config));
            // 出生点必须覆盖满员：不足时 SpawnPointSelector 会退化为重叠选点（同点双人出生）——
            // 地图是共享资源、人数是房型配置，故校验放房间构造（烘焙期无权威人数）。
            if (config.ExpectedPlayers > 0 && Map.SpawnPointCount < config.ExpectedPlayers)
                throw new ArgumentException(
                    $"地图出生点不足：spawns={Map.SpawnPointCount} < ExpectedPlayers={config.ExpectedPlayers}（满员会重叠出生）",
                    nameof(config));
            AuthSim = new SimWorldState();
            SnapshotHistory = new SnapshotRing(SimConfig.LagCompHistory);
            int players = ExpectedPlayers;                     // 配置定容（席位/输入槽/实体表/回溯环一致）
            Gate = new InputGate(players, values);
            LagComp = new LagCompensator(AuthSim, Map, players, SnapshotHistory, values, table);
            Events = new CombatEventWindow();
            _seats = new PlayerSession[players];
            _entityIds = new long[players];
            _frameInputs = new SimInputFrame[players];
            _pendingFireView = new int[players];
            _pendingFireAck = new int[players];
            _hasPendingFire = new bool[players];
            _pendingFireFrame = new int[players];
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

        /// <summary>
        /// 射手当前世界位（AimPoint 距离闸用）：按 playerId 解析实体 → 槽位 → 权威态位置。
        /// </summary>
        private bool TryResolveShooterPos(int playerId, out SimVector3 pos)
        {
            pos = default;
            if (!AuthSim.TryResolve(_entityIds[playerId], out int slot)) return false;
            if (slot < 0 || slot >= SimConfig.MaxEntities) return false;
            pos = AuthSim.Entities[slot].Pos;
            return true;
        }

        /// <summary>成员号列表（App：JoinAck.Members；playerId 升序）。</summary>
        public int[] MemberIds()
        {
            var ids = new List<int>(NextPlayerId);
            for (int i = 0; i < NextPlayerId; i++)
                if (_seats[i] != null) ids.Add(i);
            return ids.ToArray();
        }

        /// <summary>席位实时面快照（playerId 升序：在座集合 + 在线态——<see cref="SeatPhase.Active"/> 才算在线；
        /// 未占席位不列）。产出点：进房 / 断线 / 恢复完成（<see cref="SeatsChanged"/>）。</summary>
        private RoomSeatState[] BuildSeatStates()
        {
            var list = new List<RoomSeatState>(NextPlayerId);
            for (int i = 0; i < NextPlayerId; i++)
                if (_seats[i] != null) list.Add(new RoomSeatState(i, _seats[i].Phase == SeatPhase.Active));
            return list.ToArray();
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
            // 诊断录制必须在 dispatch 之前：拒绝的命令也是真实消费序的一部分（重放要复现同样的拒绝）。
            _runRecorder?.RecordCommand(in cmd);

            // 联机测试房批：GM 规则位是运行态事实，随每份快照全量下发。
            // 在**命令入口**同步（权威单线程），跨线程写被命令入口挡住——不暴露直写字段。
            AuthSim.Match.GmFlags = GmFlagsOf(_testRules);

            switch (cmd.Kind)
            {
                case RoomCommandKind.AuthenticatedJoin: ExecuteJoin(cmd.ConnectionId, outputs); break;
                case RoomCommandKind.ClientInput: ExecuteInput(cmd.PlayerId, cmd.Input); break;
                case RoomCommandKind.Disconnect: ExecuteDisconnect(cmd.ConnectionId, outputs); break;
                case RoomCommandKind.Rebind: ExecuteRebind(cmd.PlayerId, cmd.NewConnectionId); break;
                case RoomCommandKind.RestoreAck: ExecuteRestoreAck(cmd.PlayerId, outputs); break;
                case RoomCommandKind.StartEarly: ExecuteStartEarly(cmd.PlayerId, outputs); break;
                case RoomCommandKind.Tick: ExecuteTick(cmd.NowMs, outputs); break;
                case RoomCommandKind.Shutdown: ExecuteShutdown(cmd.Reason, cmd.NowMs, outputs); break;
            }
        }

        /// <summary>
        /// 运行期替换测试规则（联机测试房批）：**只允许在权威循环内调用**（宿主 GM 动作经 <c>Pump</c> 派发，
        /// 与 RoomCommand 同线程约束）。null = 关闭全部 GM。规则不进摘要（运行态事实，不是配置身份）。
        /// </summary>
        public void SetTestRules(MatchTestRules rules)
        {
            _testRules = rules;
            AuthSim.Match.GmFlags = GmFlagsOf(rules);
            _runRecorder?.RecordTestRules(AuthSim.Frame, rules);
        }

        /// <summary>规则位掩码（1 = 免死 / 2 = 无限弹药；null = 0）。</summary>
        private static int GmFlagsOf(MatchTestRules rules) => rules?.GmFlags ?? 0;

        private void ExecuteJoin(int connectionId, List<RoomOutput> outputs)
        {
            if (MatchStateMachine.IsTerminal(Match.Phase))
            {
                outputs.Add(new JoinRejectedOutput(connectionId, "房间已关闭"));
                return;
            }
            // 对局中拒加入（开房裁决）：StartMatch 只给**开局时已入座**的席位生成实体， 中途加入者会拿到无实体的空席位（EntityId 恒 0——射击/快照/结算全对不上）。
            if (Match.Phase == MatchPhase.Starting || Match.Phase == MatchPhase.Running
                || Match.Phase == MatchPhase.Finishing || Match.Phase == MatchPhase.Settling)
            {
                outputs.Add(new JoinRejectedOutput(connectionId, "对局进行中，无法加入"));
                return;
            }
            // 空位分配（未开局离场即释放——空位可复用）：取**最低空位**（确定性），全满才拒。
            int playerId = -1;
            for (int i = 0; i < ExpectedPlayers; i++)
                if (_seats[i] == null) { playerId = i; break; }
            if (playerId < 0)
            {
                outputs.Add(new JoinRejectedOutput(connectionId, "房间已满"));
                return;
            }

            if (Match.Phase == MatchPhase.Created)                       // 首个连接 → 等待玩家
                Transition(MatchPhase.WaitingForPlayers, "first-join", _lastNowMs, outputs);

            if (playerId + 1 > NextPlayerId) NextPlayerId = playerId + 1;   // 高水位只升不降（迭代上界）
            var seat = new PlayerSession(playerId, connectionId);
            // 开房者 = 首个入座者（owner 位，随 JoinAck 下发）。空位复用后 0 号位不再等价"首入座"——
            // 判据改为"房内尚无 owner"（原房主释放离场时 owner 位已迁移，新入座者不得凭号位二次称主）。
            seat.IsOwner = !AnySeatOwner();
            _seats[playerId] = seat;
            _connectionToPlayer[connectionId] = playerId;
            outputs.Add(new SignalOutput(playerId, new PlayerAdmitted(playerId, MemberIds(), seat.IsOwner)));
            outputs.Add(new SignalOutput(-1, new SeatsChanged(BuildSeatStates())));   // 席位实时面：在座集合变化广播

            // 满员不自动开局（用户裁决）：开局唯一入口 = 房主 <see cref="ExecuteStartEarly"/>——
            // 满员后再入座被"房间已满"拒（上面那闸），房间停在 WaitingForPlayers 等房主点开局。
        }

        /// <summary>房内是否已有 owner 位持有者（进房分配 owner 的判据——空位复用形态下号位不再等价身份）。</summary>
        private bool AnySeatOwner()
        {
            for (int i = 0; i < NextPlayerId; i++)
                if (_seats[i] != null && _seats[i].IsOwner) return true;
            return false;
        }

        /// <summary>
        /// 房主开局（**本房唯一的开局入口**——用户裁决取消满员自动开局）：WaitingForPlayers 下 owner 席位触发 → 立即 <see cref="StartMatch"/>（满员与否皆可。）。
        /// </summary>
        private void ExecuteStartEarly(int playerId, List<RoomOutput> outputs)
        {
            if (Match.Phase != MatchPhase.WaitingForPlayers)
                return;                                                   // 已开局/已关闭：幂等忽略（无反馈——UI 状态本就不该可点）
            if (playerId < 0 || playerId >= ExpectedPlayers || _seats[playerId] == null)
            {
                outputs.Add(new StartRejectedOutput(playerId, "席位无效"));
                return;
            }
            if (!_seats[playerId].IsOwner)
            {
                outputs.Add(new StartRejectedOutput(playerId, "只有房主可以开始对局"));
                return;
            }
            StartMatch(outputs);
        }

        private void ExecuteInput(int playerId, in ClientInputBatch batch)
        {
            if (!Started) return;                                        // 仅 Running 消费输入
            PlayerSession seat = SeatOf(playerId);
            if (seat == null) return;

            bool accepted = Gate.Store(batch, playerId, _entityIds[playerId], AuthSim.Frame,
                out int acceptedFrame, out SimInputFrame acceptedInput,
                shooterPos: TryResolveShooterPos(playerId, out SimVector3 sp) ? sp : null);
            if (!accepted) return;

            // 开火 + 带视点帧 → 记下待回溯判定（下一帧步进后执行——那时环里才有"开火帧"的历史态）。
            // 回溯对齐参考（pendingFireAck）用闸门**钳位后**的记账值。
            bool fired = (acceptedInput.Buttons & SimInputFrame.ButtonFire) != 0u;
            if (fired && batch.ViewFrame > 0)
            {
                _pendingFireView[playerId] = batch.ViewFrame;
                _pendingFireAck[playerId] = Gate.LastClampedAckSnapshot;
                _hasPendingFire[playerId] = true;
                // C4：记下这条开火输入的**目标帧**（闸门钳位后的 acceptedFrame）——
                // defer 位必须挂到该帧，而不是无条件挂下一个步进帧。
                _pendingFireFrame[playerId] = acceptedFrame;
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

            // 未开局离场：席位**直接释放**（不保留断线态）——等待期席位不可恢复（App 重连要求 房间已开局，未开局必拒），保留只占容量：坐满后走一人即恒答"房间已满"直到全员散尽。
            if (Match.Phase == MatchPhase.WaitingForPlayers)
            {
                ReleaseSeat(playerId, seat, outputs);
                return;
            }

            seat.Phase = SeatPhase.Disconnected;                              // 对局中：席位保留（重连窗口内可重绑）
            seat.ConnectionId = -1;

            // 房主离席 → owner 位转给最低 playerId 的**在线**其余席位（全断线时退而转最低席位，
            // 防"回来的人都不是房主"死局）；只剩房主一人则保持（空房无人可转）。旧房主重连不夺回——确定性优先。
            if (seat.IsOwner)
                TransferOwner(playerId, outputs);

            outputs.Add(new SignalOutput(-1, new SeatsChanged(BuildSeatStates())));   // 席位实时面：离线态广播

            if (!AllSeatsDisconnected()) return;
            // 全员离场 → 收尾（迁移在席位面广播之后：离线态先落，终态随后）。
            // Running：对局结果可冻结 → Finish（结算并关闭；Aborted 只留给错误/运维终止）。
            if (Match.Phase == MatchPhase.Running)
                Finish(ShutdownReason.AllPlayersLeft, "all-players-left", outputs);
        }

        /// <summary>
        /// 未开局离场的席位释放（<see cref="ExecuteDisconnect"/> 的 WaitingForPlayers 分支）： owner 位先迁移再摘席（在座者接管开局资格）。
        /// </summary>
        private void ReleaseSeat(int playerId, PlayerSession seat, List<RoomOutput> outputs)
        {
            if (seat.IsOwner)
                TransferOwner(playerId, outputs);          // 释放前迁移——在座者接管开局资格
            _seats[playerId] = null;
            outputs.Add(new SignalOutput(-1, new SeatsChanged(BuildSeatStates())));
            if (AllSeatsDisconnected())
                Abort(ShutdownReason.AllPlayersLeft, "all-players-left", outputs);
        }

        /// <summary>
        /// owner 位迁移（原房主 <paramref name="departing"/> 离席）：**在线席位（Active/Restoring） 优先**，按最低 playerId 取。
        /// </summary>
        private void TransferOwner(int departing, List<RoomOutput> outputs)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < NextPlayerId; i++)
                {
                    if (i == departing || _seats[i] == null) continue;
                    if (pass == 0 && _seats[i].Phase == SeatPhase.Disconnected) continue;   // 第一轮只看在线席位
                    _seats[departing].IsOwner = false;
                    _seats[i].IsOwner = true;
                    outputs.Add(new SignalOutput(-1, new OwnerChanged(i)));
                    return;
                }
            }
        }

        private bool AllSeatsDisconnected()
        {
            for (int i = 0; i < NextPlayerId; i++)
                if (_seats[i] != null && _seats[i].Phase != SeatPhase.Disconnected) return false;
            return NextPlayerId > 0;
        }

        private void ExecuteRebind(int playerId, int newConnectionId)
        {
            if (MatchStateMachine.IsTerminal(Match.Phase)) return;               // 终态房间不再接受重绑（App 已拦 Running，此处兜底）
            PlayerSession seat = SeatOf(playerId);
            if (seat == null) return;
            if (seat.ConnectionId >= 0) _connectionToPlayer.Remove(seat.ConnectionId);   // 旧连接映射失效
            seat.ConnectionId = newConnectionId;
            seat.Phase = SeatPhase.Restoring;    // §9.3 步骤 6：恢复完成 ACK 前抑制该席位增量广播（App 管线按相位过滤）
            seat.RebindGeneration++;
            _connectionToPlayer[newConnectionId] = playerId;
            // 重绑本身不产事件（App 负责响应构建）；恢复完成时产 SeatRestored（见 ExecuteRestoreAck）
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
            outputs.Add(new SignalOutput(-1, new SeatsChanged(BuildSeatStates())));   // 席位实时面：恢复即在线广播
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
            if (AuthSim.Match.Phase == SimMatchPhase.Finished)
            {
                ShutdownReason reason = AuthSim.Match.EndReason == MatchEndReason.KillLimit
                    ? ShutdownReason.KillLimit : ShutdownReason.TimeLimit;
                Finish(reason, "gameplay:" + AuthSim.Match.EndReason, outputs);
            }
        }

        private void ExecuteShutdown(ShutdownReason reason, long nowMs, List<RoomOutput> outputs)
        {
            if (MatchStateMachine.IsTerminal(Match.Phase))
            {
                // 幂等：终态重复关闭只记事件，不重迁移（§9.1）
                outputs.Add(new MatchStateChangedOutput(Match.Phase, Match.Phase, "重复关闭（忽略）", MatchId, AuthSim.Frame, nowMs));
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
            MatchSystem.Finish(AuthSim, reason == ShutdownReason.TimeLimit ? MatchEndReason.TimeLimit : MatchEndReason.External);
            Transition(MatchPhase.Finishing, detail, now, outputs);
            int[] members = MemberIds();
            var players = new PlayerMatchResult[members.Length];
            for (int i = 0; i < members.Length; i++)
            {
                int playerId = members[i];
                AuthSim.TryResolve(_entityIds[playerId], out int slot);
                EntitySlot e = slot >= 0 ? AuthSim.Entities[slot] : default;
                players[i] = new PlayerMatchResult(playerId, _entityIds[playerId], e.Kills, e.Deaths);
            }
            outputs.Add(new SettlementReadyOutput(new MatchResultSummary(MatchId, Seed, AuthSim.Frame, reason, members,
                AuthSim.Match.Winner, AuthSim.Match.EndReason, players)));
            Transition(MatchPhase.Settling, "settle", now, outputs);
            Transition(MatchPhase.Closed, "closed", now, outputs);
        }

        /// <summary>中止（任意阶段可达）：只迁移并产事件，无结算输出。成功迁移后补
        /// <see cref="RoomAbortedOutput"/>——App 向在座客户端发终局信令（未开局收尾无结果可结算）。</summary>
        private void Abort(ShutdownReason reason, string detail, List<RoomOutput> outputs)
        {
            MatchPhase from = Match.Phase;
            Transition(MatchPhase.Aborted, detail + ":" + reason, _lastNowMs, outputs);
            if (Match.Phase == MatchPhase.Aborted && from != MatchPhase.Aborted)
                outputs.Add(new RoomAbortedOutput(reason, MatchId, AuthSim.Frame));
        }

        /// <summary>迁移并产结构化事件（§9.1）；非法迁移输出 Reason=Rejected（不改状态、不抛）。</summary>
        private void Transition(MatchPhase to, string reason, long nowMs, List<RoomOutput> outputs)
        {
            MatchPhase from = Match.Phase;
            if (!Match.TryTransition(to, reason, nowMs))
            {
                outputs.Add(new MatchStateChangedOutput(from, from, "Rejected:" + reason, MatchId, AuthSim.Frame, nowMs));
                return;
            }
            outputs.Add(new MatchStateChangedOutput(from, to, reason, MatchId, AuthSim.Frame, nowMs));
        }

        /// <summary>开局迁移的审计原因（唯一入口 = 房主开局——满员自动路径已删，见 <see cref="ExecuteStartEarly"/>）。</summary>
        private const string StartReasonOwnerStart = "owner-start";

        /// <summary>
        /// 开局（§4.5 权威定序——StartMatch 后循环才消费输入）：WaitingForPlayers → Starting → Running， 世界按种子生成，玩家落出生点。
        /// </summary>
        private void StartMatch(List<RoomOutput> outputs)
        {
            Transition(MatchPhase.Starting, StartReasonOwnerStart, _lastNowMs, outputs);

            Seed = Config.Seed != 0 ? Config.Seed : DeriveSeed(_lastNowMs);
            AuthSim.RngState = (ulong)Seed;
            _runRecorder?.RecordSeed((ulong)Seed);
            MatchSystem.Start(AuthSim, Rules);

            uint used = 0u;
            for (int i = 0; i < NextPlayerId; i++)
            {
                PlayerSession seat = _seats[i];
                if (seat == null) continue;
                int spawnIndex = SpawnPointSelector.Pick(Map, used,
                    SpawnPointSelector.InitialHash((ulong)Seed, MatchId, i));
                used |= 1u << spawnIndex;
                SimVector3 spawn = Map.SpawnPoints[spawnIndex];
                AuthSim.Spawn(new EntitySlot { Hp = FixedConfig.Values.EntityHp, Pos = spawn, Yaw = 0f,
                    Flags = EntityFlags.Player, SpawnPointIndex = spawnIndex }, out int slot);
                _entityIds[i] = AuthSim.Entities[slot].Id;
                seat.EntityId = _entityIds[i];
            }

            Transition(MatchPhase.Running, "started", _lastNowMs, outputs);
            outputs.Add(new SignalOutput(-1, new MatchStarted(Seed, AuthSim.Frame, FixedConfig.Digest, AuthSim.Match, MatchId)));
        }

        /// <summary>seed 派生常量（<see cref="DeriveSeed"/>）：LCG 乘数 / 黄金比例混合常量 / 奇化乘子。</summary>
        private const long SeedLcgMultiplier = 0x5DEECE66DL;
        private const long SeedGoldenRatioMix = unchecked((long)0x9E3779B97F4A7C15UL);
        private const long SeedOddMultiplier = 137L;

        /// <summary>
        /// seed=0 的派生（非零、确定性、无墙钟）：以 Tick 时间戳混入黄金比例常量。
        /// </summary>
        private static long DeriveSeed(long nowMs)
        {
            if (nowMs == 0) return SeedLcgMultiplier;
            unchecked { return ((nowMs ^ SeedGoldenRatioMix) * SeedOddMultiplier) | 1L; }
        }

        /// <summary>
        /// 推进一步权威帧（§4.5-3 权威定序）：消费预存输入（缺席沿用空输入）→ Step → 快照环 Capture → 记录历史输入 → 回溯判定（本步各玩家 pending 开火逐个处理）。
        /// </summary>
        private void StepFrame()
        {
            AuthSim.Events.Clear();
            int frame = AuthSim.Frame + 1;   // 本步目标帧号（输入按帧号预存——inputDelay=1 语义）
            for (int i = 0; i < ExpectedPlayers; i++)
            {
                // 缺席沿用：断线/未发包成员用空输入（§4.5-2；数组序即 playerId 升序——与帧号语义一致）
                _frameInputs[i] = Gate.TryConsume(frame, i, out SimInputFrame stored) ? stored : default;
                // C4：只在步进到该开火输入的目标帧时挂 defer 位（原先无条件挂 Frame+1，导致延迟命中落到错帧）。
                if (_hasPendingFire[i] && _pendingFireFrame[i] == frame)
                    _frameInputs[i].Buttons |= SimInputFrame.ButtonDeferHit;
            }

            SimStep.Step(AuthSim, Map, _frameInputs, FixedConfig.Values, FixedConfig.Weapons, advanceMatch: false, rules: _testRules);
            StepsCount++;
            // 只有通过本帧武器门并产 Fire 的请求可回溯；空仓/冷却/死亡输入不能旁路资源门。
            for (int i = 0; i < ExpectedPlayers; i++)
            {
                bool fired = false;
                for (int j = 0; j < AuthSim.Events.Count; j++)
                    if (AuthSim.Events.Items[j].Kind == FrameEventKind.Fire
                        && AuthSim.Events.Items[j].EntityId == _entityIds[i]) fired = true;
                // C4：目标帧已越过（未能在该帧挂上 defer 位）→ 作废，避免待回溯标记永久悬挂。
                if (_hasPendingFire[i] && _pendingFireFrame[i] < frame) fired = false;
                _hasPendingFire[i] &= fired;
                _frameInputs[i].Buttons &= ~SimInputFrame.ButtonDeferHit;
            }
            // 远端火光面：开火事件须在回溯（ConsumePendingFire 会清事件缓冲）之前捕获—— 此刻缓冲仍是前向步进的完整事件集（武器门放行的扣扳机各一条，空仓/被拦/死亡不产）。
            CaptureEvents(AuthSim.Frame, fireOnly: true);
            int steppedFrame = AuthSim.Frame;
            SnapshotHistory.Capture(steppedFrame, AuthSim);   // 每帧捕获（回溯基料）
            _recentInputs.Record(steppedFrame, _frameInputs, null);   // 重连补发基料（Step 已就地排序 → 规范形）
            LagComp.RecordInputs(steppedFrame, _frameInputs);

            ConsumePendingFire(steppedFrame);                 // 回溯判定（本步产生开火输入时；结算死亡即刻入窗）
            MatchSystem.Tick(AuthSim);                        // 整帧（含回溯）结算后才判胜
            SnapshotHistory.Capture(steppedFrame, AuthSim);    // 覆盖本帧的最终计分/比赛状态
            _runRecorder?.RecordStep(steppedFrame, SimChecksum.ComputeChecksum(AuthSim), AuthSim);
            _runRecorder?.PollSnapshot();                     // mismatch 即时快照（房间线程封印→宿主异步写）
        }

        /// <summary>
        /// 回溯判定：开火帧的历史态现在已在环里（本步 Capture 覆盖到"开火帧"本身）， 因此可回溯到所见帧判定；每位射手的命中立即结算，本帧全部完成后再裁决对局结果。
        /// </summary>
        private void ConsumePendingFire(int frame)
        {
            // 前向步进的命中/死亡事件先入窗（未走回溯的射手/同帧直接伤害）——回溯会清事件缓冲
            // （<see cref="LagCompensator.CompensateFire"/> 的 ClearFireBuffers），此处是最后捕获面。
            CaptureEvents(frame, fireOnly: false);

            for (int playerId = 0; playerId < ExpectedPlayers; playerId++)
            {
                if (!_hasPendingFire[playerId]) continue;
                _hasPendingFire[playerId] = false;

                LagComp.CompensateFire(playerId, _entityIds[playerId], _pendingFireView[playerId], _pendingFireAck[playerId]);
                SimStep.FlushCommands(AuthSim); // 每位射手的命中当帧落账；后续射手不得清掉待结算命令
                CaptureEvents(frame, fireOnly: false);   // 该射手的回溯命中/死亡即刻入窗（下一位射手的回溯会清缓冲）
                FireInputsProcessed++;
            }
        }

        /// <summary>
        /// 公共战斗事件捕获（**唯一入口**——取代原先并行的 CaptureHits/CaptureKills/CaptureFires 三段）。
        /// </summary>
        private void CaptureEvents(int frame, bool fireOnly)
        {
            FrameEvent[] items = AuthSim.Events.Items;
            for (int i = 0; i < AuthSim.Events.Count; i++)
            {
                ref FrameEvent e = ref items[i];

                if (fireOnly)
                {
                    if (e.Kind != FrameEventKind.Fire) continue;
                    Events.Add(e.Kind, e.EntityId, e.OtherId, frame, e.Value, in e.Pos);
                    continue;
                }

                switch (e.Kind)
                {
                    case FrameEventKind.Fire:
                        // 前向段已入窗；回溯段的重放 Fire 不再重复记（窗口同键聚合也只留一条，
                        // 但显式跳过更省一次查找，语义也更直白）。
                        continue;
                    case FrameEventKind.Death:
                    {
                        int weaponSlot = -1;
                        if (e.OtherId != 0 && AuthSim.TryResolve(e.OtherId, out int killerSlot))
                            weaponSlot = AuthSim.Entities[killerSlot].SelectedWeapon;
                        Events.Add(e.Kind, e.EntityId, e.OtherId, frame, weaponSlot, in e.Pos);
                        continue;
                    }
                    default:
                        // Hit / Crit（以及未来任何"主体+对象+数值+位置"同构的事件）：直传，
                        // 同键聚合交窗口（霰弹多弹丸合并、Crit 升档）。
                        Events.Add(e.Kind, e.EntityId, e.OtherId, frame, e.Value, in e.Pos);
                        continue;
                }
            }
        }
    }
}
