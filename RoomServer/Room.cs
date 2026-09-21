using System;
using System.Collections.Generic;
using LiteSim;
using LiteNet.Protocol;
using LiteNet.Proto;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 房间（《状态同步实施方案》§4.5 Room 结构）：权威 Sim + 输入聚合 + 60Hz 定拍循环 +
    /// 快照广播（增量 + AOI + 全量兜底）+ 命中回溯 + E1 背压 + Ops。
    ///
    /// - **AuthSim 唯一真相**：SimWorldState 引用 LiteSim.Core 原生 Step——掉线者输入沿用空输入（§4.5-2 掉线不停帧）；
    /// - **快照环**：每帧 Capture（容量 LagCompHistory=16，M9 类复用）——回溯判定基料（批② 已建）；
    /// - **快照广播**（批③）：30Hz（`TickRate / SnapshotHz` 抽帧）→ <see cref="SnapshotDiffer"/> 增量 +
    ///   <see cref="AoiFilter"/> 可见裁剪 + 全量兜底；**退化为全量时循环结构不变**（决策 7 解耦点兑现）；
    /// - **定拍**：绝对时间锚定（nextTick = start + n×dt——防 PeriodicTimer/相对 Sleep 的漂移累积，风险 5）；
    /// - **成员表**：playerId ↔ entityId ↔ sessionId；输入 EntityId 一律按会话覆写（防伪）；
    /// - **Ops**：帧号/输入收发/丢弃/差分/回溯/背压——属性可断言，周期打印由 <see cref="Ops"/> 负责。
    /// MVP 房间形态：固定 2 人期望（批②/④验收形态），齐员自动 StartGame——无 MatchMaker（决策⑩）。
    /// </summary>
    public sealed class Room
    {
        /// <summary>装配参数（容量/房间号/端口/seed 策略——2026-09-19 审计建议 2 收口）。</summary>
        public readonly RoomConfig Config;

        /// <summary>期望人数（转发 <see cref="Config"/>；席位/输入槽/实体表按此定容）。</summary>
        public int ExpectedPlayers => Config.ExpectedPlayers;

        public readonly string RoomId;
        public readonly SimWorldState AuthSim;         // 权威唯一真相
        public readonly SimMapData Map;
        public readonly SnapshotRing SnapshotHistory; // 回溯环（M9 类，容量 LagCompHistory）
        public readonly InputGate Gate;
        public readonly LagCompensator LagComp;        // 命中回溯（批③）
        public readonly RoomBroadcaster Broadcaster;   // 快照广播（2026-09-19 拆分：广播面独立，本类只管权威模拟与席位）

        /// <summary>成员：playerId → 会话。</summary>
        private readonly Dictionary<int, Session> _members = new Dictionary<int, Session>();
        /// <summary>playerId → 玩家实体 Id（StartGame 分配）。</summary>
        private readonly long[] _entityIds;
        /// <summary>本帧聚合输入槽（Step 前重灌）。</summary>
        private readonly SimInputFrame[] _frameInputs;
        /// <summary>成员序（playerId 升序，广播按此序——确定性）。</summary>
        private readonly Session[] _playerSessions;
        /// <summary>全体输入历史（重连补发用；§5.6 —— 环容量 <see cref="SimConfig.MaxInputHistory"/>，够 32 帧）。</summary>
        private readonly InputHistory _recentInputs;   // ctor 内按配置容量构造

        public int NextPlayerId;
        public bool Started;
        public long Seed;

        // ---- Ops 计数 ----
        public long StepsCount;
        public long FireInputsProcessed;        // 走回溯路径的开火输入数

        // 广播面计数 → 转发 RoomBroadcaster（2026-09-19 拆分：外部引用零改动）
        public long SnapshotSent => Broadcaster.SnapshotSent;
        public long SnapshotFullSent => Broadcaster.SnapshotFullSent;
        public long BackpressureThrottled => Broadcaster.BackpressureThrottled;
        public SnapshotDiffer Differ => Broadcaster.Differ;   // Ops 快照尺寸统计转发

        public Room(RoomConfig config)
        {
            Config = config ?? throw new System.ArgumentNullException(nameof(config));
            RoomId = config.RoomId;
            Map = BuildStandardMap();
            AuthSim = new SimWorldState();
            SnapshotHistory = new SnapshotRing(SimConfig.LagCompHistory);
            int players = ExpectedPlayers;                     // 配置定容（席位/输入槽/实体表/回溯环一致）
            Gate = new InputGate(players);
            LagComp = new LagCompensator(AuthSim, players, SnapshotHistory);
            _entityIds = new long[players];
            _frameInputs = new SimInputFrame[players];
            _playerSessions = new Session[players];
            _pendingFireView = new int[players];
            _pendingFireAck = new int[players];
            _hasPendingFire = new bool[players];
            _recentInputs = new InputHistory(SimConfig.MaxInputHistory, players);
            Broadcaster = new RoomBroadcaster(_playerSessions, _entityIds, new SnapshotDiffer());
        }

        /// <summary>成员就位分配玩家号（StartGame 前调用）；满员返回 -1。</summary>
        public int AssignPlayerId(Session session)
        {
            int playerId = NextPlayerId++;
            if (playerId >= ExpectedPlayers) return -1;
            return AssignPlayerId(session, playerId);
        }

        /// <summary>重连：把会话重挂到**既有席位**（playerId 不变，实体 Id 不变——§5.6 权威快照恢复的前提）。</summary>
        public int AssignPlayerId(Session session, int playerId)
        {
            if (playerId < 0 || playerId >= ExpectedPlayers) return -1;
            Session old = _playerSessions[playerId];
            if (old != null && old != session) old.Disconnected = true;   // 旧连接让位（新连接接管席位）
            _members[playerId] = session;
            _playerSessions[playerId] = session;
            session.PlayerId = playerId;
            session.Room = this;
            if (playerId >= NextPlayerId) NextPlayerId = playerId + 1;
            return playerId;
        }

        /// <summary>该帧全体玩家的历史输入（重连补发用——§5.6：权威快照 + 后续输入历史；环容量 MaxInputHistory）。</summary>
        public bool HistoryFor(int frame, out SimInputFrame[] inputs)
            => _recentInputs.TryGet(frame, out inputs, out bool[] _);

        /// <summary>成员号列表（JoinAck.Members；playerId 升序）。</summary>
        public int[] MemberIds()
        {
            var ids = new List<int>(_members.Keys);
            ids.Sort();
            return ids.ToArray();
        }

        public bool TryGetMember(int playerId, out Session session) => _members.TryGetValue(playerId, out session);

        /// <summary>遍历成员（StartGame 广播用；playerId 升序）。</summary>
        public IEnumerable<KeyValuePair<int, Session>> AllMembers()
        {
            for (int i = 0; i < NextPlayerId; i++)
                if (TryGetMember(i, out Session s)) yield return new KeyValuePair<int, Session>(i, s);
        }

        public long EntityIdOf(int playerId) =>
            playerId >= 0 && playerId < ExpectedPlayers ? _entityIds[playerId] : 0L;

        /// <summary>
        /// StartGame：世界按种子生成，玩家落出生点（§4.5 权威定序——StartGame 后循环才消费输入）。
        /// seed = 0 → 取配置策略（<see cref="RoomConfig.Seed"/> 非 0 用配置值，否则服务器时钟低 31 位，下发客户端）。
        /// </summary>
        public void Start(long seed)
        {
            if (seed == 0) seed = Config.Seed != 0 ? Config.Seed : (System.DateTime.Now.Ticks & 0x7FFFFFFFL);
            Seed = seed;
            AuthSim.RngState = (ulong)seed;

            for (int i = 0; i < _members.Count; i++)
            {
                SimVector3 spawn = Map.SpawnPoints[i % Map.SpawnPointCount];
                AuthSim.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = spawn, Yaw = 0f }, out int slot);
                _entityIds[i] = AuthSim.Entities[slot].Id;
            }

            Started = true;
        }

        /// <summary>
        /// 消费一条已过闸输入（预存到 frame 对应槽——inputDelay=1 语义，权威帧推进到位时消费）。
        ///
        /// 2026-09-19 审查修正：开火判定改用 **`InputGate.Store` 实际接受的那一帧与那份输入**——
        /// 原先 `HasFire` 自己又推了一遍取帧口径（只认"服务器当前帧+1"），当窗口不含该帧而 Store
        /// 向下取到更早的可接受帧时，会出现"输入收了（Sim 真开枪）但没记回溯判定"的不一致。
        /// 现在**取帧口径只有一处**（InputGate），本类只消费其结果。
        /// </summary>
        public void OnInput(Session session, InputMessage msg)
        {
            if (!Started || session.PlayerId < 0) return;

            bool accepted = Gate.Store(msg, session.PlayerId, _entityIds[session.PlayerId], AuthSim.Frame,
                out int acceptedFrame, out SimInputFrame acceptedInput);
            if (!accepted) return;

            if (OnInputAccepted != null) OnInputAccepted(session, msg);

            // 开火 + 带视点帧 → 记下待回溯判定（在下一帧步进后执行——那时环里才有"开火帧"的历史态）。
            // 回溯对齐参考（pendingFireAck）用闸门**钳位后**的记账值；LastAckSnapshot（NeedsFull 判据）
            // 不在此写——它只吃 Room.OnClientAck 验证通过的值（R0-P0-4）。
            bool fired = (acceptedInput.Buttons & SimInputFrame.ButtonFire) != 0u;
            if (fired && msg.ViewFrame > 0)
            {
                int p = session.PlayerId;
                _pendingFireView[p] = msg.ViewFrame;
                _pendingFireAck[p] = Gate.LastClampedAckSnapshot;
                _hasPendingFire[p] = true;
            }
        }

        /// <summary>待回溯判定的开火输入——**按玩家分槽**（4 人房同 tick 多人开火不能互相覆盖）。</summary>
        private readonly int[] _pendingFireView;
        private readonly int[] _pendingFireAck;
        private readonly bool[] _hasPendingFire;

        /// <summary>
        /// 推进一步权威帧（§4.5-3 权威定序）：消费预存输入（缺席沿用空输入）→ Step → 快照环 Capture
        /// → 记录历史输入 → 回溯判定（本步各玩家 pending 开火逐个处理）→ 到点广播快照。
        /// </summary>
        public bool StepFrame()
        {
            if (!Started) return false;

            int frame = AuthSim.Frame + 1;   // 本步目标帧号（输入按帧号预存——inputDelay=1 语义）
            for (int i = 0; i < ExpectedPlayers; i++)
            {
                // 缺席沿用：断线/未发包成员用空输入（§4.5-2；数组序即 playerId 升序——与 M9 帧号语义一致）
                _frameInputs[i] = Gate.TryConsume(frame, i, out SimInputFrame stored) ? stored : default;
            }

            SimStep.Step(AuthSim, Map, _frameInputs);
            StepsCount++;
            int steppedFrame = AuthSim.Frame;
            SnapshotHistory.Capture(steppedFrame, AuthSim);   // 每帧捕获（回溯基料）
            _recentInputs.Record(steppedFrame, _frameInputs, null);   // 重连补发基料（Step 已就地排序 → 规范形）
            LagComp.RecordInputs(steppedFrame, _frameInputs);

            ConsumePendingFire();                             // 回溯判定（本步产生开火输入时）

            Broadcaster.BroadcastIfDue(steppedFrame, AuthSim, Gate);
            return true;
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

        /// <summary>下一次广播强制全量（重连场景：客户端要从零重建）。转调广播器（广播面已拆出）。</summary>
        public void RequestFullSnapshot() => Broadcaster.RequestFullSnapshot();

        // ---- R0 可信 ACK（《商业级通用服务端框架总设计》§5 P0-4；分类计数——Ops 观测）----
        /// <summary>ACK 回退/重复/负值（≤ 已确认水位）拒绝计数。</summary>
        public long AckRejectedStale;
        /// <summary>ACK 超前（> 最近实际发送帧——伪造/时钟错乱）拒绝计数。</summary>
        public long AckRejectedFuture;
        /// <summary>ACK 落在 ledger 窗外（超窗未确认——陈旧）拒绝计数。</summary>
        public long AckRejectedEvicted;
        /// <summary>ACK 违纪合计（= 上三者之和；Ops 汇总口径）。</summary>
        public long AckRejected => AckRejectedStale + AckRejectedFuture + AckRejectedEvicted;

        /// <summary>
        /// 语义 ACK 验证（P0-4：**旧实现"ack 到达即 AckedBytes=SendQueueBytes 全清"可被任意伪造 ack 清空水位**——
        /// 慢客户端伪装成快客户端，E1 降级形同虚设）。四道闸全过才释放：
        /// 非负且单调前进（> LastAcceptedAckFrame）→ 不超前（≤ LastSentSnapshotFrame，只认真实发送过的帧）
        /// → 命中发送 ledger（取该帧发送后的累计字节）。释放 = <c>AckedBytes = ledger.累计值</c>（天然单调）。
        /// <see cref="Session.LastAckSnapshot"/>（NeedsFull 判据）**只在验证后**更新——差分器的全量兜底只吃可信值。
        /// </summary>
        public void OnClientAck(Session session, int ackSnapshot)
        {
            if (ackSnapshot < 0 || ackSnapshot <= session.LastAcceptedAckFrame)
            {
                AckRejectedStale++;
                return;
            }
            if (ackSnapshot > session.LastSentSnapshotFrame)
            {
                AckRejectedFuture++;
                return;
            }
            if (!session.SnapshotLedger.TryGet(ackSnapshot, out long cumulativeAfter))
            {
                AckRejectedEvicted++;
                return;
            }

            session.AckedBytes = cumulativeAfter;               // ledger 累计口径：一次到位
            session.LastAcceptedAckFrame = ackSnapshot;
            session.LastAckSnapshot = ackSnapshot;              // 验证过才更新（P0-4 红线）
        }

        /// <summary>包发送出口（ServerHost 装配时注入；测试用 <c>Room.SendTo = (session, type, msg, reliable) => ...</c> 捕获）。
        /// 广播器与 Room 共用同一出口——快照与信令最终都经它下发。</summary>
        public Action<Session, PacketType, Google.Protobuf.IMessage, bool> SendTo;

        /// <summary>Ops 计数挂点（ServerHost 装配；测试不挂 = 无计数）。计数由宿主/房间各自累加。</summary>
        public Action<Session, InputMessage> OnInputAccepted;

        /// <summary>标准灰盒地图：±50 边界 + 16 网格出生点（MVP 房间形态；正式地图装配归 M11）。</summary>
        private static SimMapData BuildStandardMap()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            for (int i = 0; i < SimMapData.MaxSpawnPoints; i++)
            {
                map.SpawnPoints[i] = new SimVector3(((i % 4) - 1.5f) * 10f, 0f, ((i / 4) - 1.5f) * 10f);
            }
            map.SpawnPointCount = SimMapData.MaxSpawnPoints;
            return map;
        }
    }
}
