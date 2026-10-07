using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 命中延迟补偿 = 服务器回溯（《状态同步实施方案》§3.4.1 + 《M10实施指导》决策 8）。
    ///
    /// 客户端在 Input 包里带 `viewFrame`（= 它开枪那一刻"看到的"帧号）。服务器按 §3.4.1 对齐到自己的时间轴：
    /// <c>serverFrame = viewFrame + (服务器当前帧 − 客户端 ack 快照帧)</c>——两边帧号同源同义（StartGame 种子统一起步），
    /// clientLead 就是客户端的领先量。直接把 viewFrame 当服务器帧号用会回溯得过深（把领先量算成了额外延迟）。
    ///
    /// 回溯动作（**服务器单侧，不触 Sim 纯函数红线**）：暂存权威态 → 环回到目标帧 → **只跑 ShootingSystem**
    /// （不推进 Frame、不跑其余系统）→ 命中产出的**命令/事件落到当前帧**（结算回到权威当前帧，防"幽灵命中"）→ 还原权威态。
    ///
    /// 历史输入来源：<see cref="RecordInputs"/> 每帧记下"该帧实际消费的输入"（缺席 = 空输入）——
    /// 回溯用的是**当时真正用的那一份**，不是重新采一份（否则判定与权威史不符）。
    ///
    /// 退化路径（关闭 / 窗口外 / 无历史）：不回溯，按**当前帧**判定（<see cref="Outcome.OutOfWindow"/>）——
    /// 行为等价于"没有补偿"，是 §3.4.1 的指定退路。
    /// </summary>
    public sealed class LagCompensator
    {
        /// <summary>一次回溯判定的结果（诊断/Ops；也是用例断言点）。</summary>
        public enum Outcome
        {
            /// <summary>无开火位（玩家没按开火）。</summary>
            NoFire = 0,
            /// <summary>回溯成功并执行了判定。</summary>
            Compensated = 1,
            /// <summary>超出回溯窗口 / 补偿关闭 → 退化为当前帧判定（打开火的走 <see cref="Outcome.DegradedFire"/>）。</summary>
            OutOfWindow = 2,
            /// <summary>退化路径下确实执行了判定（当前帧开火）。</summary>
            DegradedFire = 3,
            /// <summary>输入里的实体无法解析（已死亡/伪造）→ 不判定。</summary>
            InvalidShooter = 4,
        }

        private readonly SimWorldState _auth;
        private readonly SimMapData _map;
        private readonly SnapshotRing _ring;
        private readonly int _playerCount;
        private readonly int _historyCapacity;

        /// <summary>历史输入：玩家 → 帧号 → 该帧实际消费的输入（窗口容量 = LagCompHistory + 1）。</summary>
        private readonly InputHistory[] _history;
        /// <summary>权威态暂存（回溯前拷贝、回溯后还原——复用一份，零运行期分配）。</summary>
        private readonly SimWorldState _scratch;
        /// <summary>单系统执行用的输入槽（长度 = 玩家数，只放开火者那一条——其余玩家不参与判定）。</summary>
        private readonly SimInputFrame[] _fireInputs;

        public Outcome LastOutcome { get; private set; } = Outcome.NoFire;
        public int LastTargetFrame { get; private set; } = -1;
        /// <summary>本次判定（回溯或退化）是否产生了命中事件。</summary>
        public bool LastHit { get; private set; }
        public long CompensatedCount;
        public long DegradedCount;

        public LagCompensator(SimWorldState auth, in SimMapData map, int playerCount, SnapshotRing ring)
        {
            _auth = auth;
            _map = map;                                  // 障碍是静态判定数据（回溯窗内逐帧同值——历史帧与当前帧同一份）
            _playerCount = playerCount;
            _ring = ring;                                   // 与 RoomRuntime.SnapshotHistory 同一份（权威循环每帧 Capture）
            _scratch = new SimWorldState();
            _fireInputs = new SimInputFrame[playerCount];
            _historyCapacity = SimConfig.LagCompHistory + 1;
            _history = new InputHistory[playerCount];
            for (int i = 0; i < playerCount; i++) _history[i] = new InputHistory(_historyCapacity, playerCount);
        }

        /// <summary>
        /// 权威循环每帧 Step 后调用：记录该帧实际消费的输入（回溯判定的历史来源）。
        /// <see cref="SimConfig.LagCompHistory"/> = 0（关闭补偿）时这里不记历史——判据在
        /// <see cref="CompensateFire"/> 内联（编译期常量分支，写在这里会被编译器判为不可达）。
        /// </summary>
        public void RecordInputs(int frame, SimInputFrame[] consumed)
        {
            for (int i = 0; i < _playerCount; i++) _history[i].Record(frame, consumed, null);
        }

        /// <summary>
        /// 处理一条开火输入：回溯到玩家所见帧判定，命中结果以命令/事件形式落到**当前帧**。
        /// 调用时机：<see cref="RoomRuntime"/> 单帧步进之后（权威当前帧 = 本步刚跑完的帧）。
        ///
        /// <paramref name="viewFrame"/> = **客户端看到的权威帧号**（§3.4.1：ackSnapshot 对应的权威帧 + 插值帧）。
        /// 它已经是服务器帧轴上的值（快照帧号两端同源），所以服务器只做两步：
        /// ① 减掉插值量 → 玩家真正渲染的那一帧；② clamp 到 <c>[当前帧 − LagCompHistory, 当前帧]</c>。
        /// 第 ② 步是 §3.4.1 的反作弊边界：捏造超大/超小的 viewFrame 只会被夹到窗口边界，**拉不长回溯窗口**；
        /// "ackSnapshot ≤ 服务器已广播帧号"是另一条 clamp，在 <see cref="InputGate"/> 落地（收包时校验）。
        /// </summary>
        public Outcome CompensateFire(int playerId, long entityId, int viewFrame, int clientAckSnapshot)
        {
            if (!_auth.TryResolve(entityId, out int shooterSlot)) return Set(Outcome.InvalidShooter, -1, false);

            int targetFrame = viewFrame - SimConfig.InterpFrames;

            // 窗口 clamp（反作弊第一刀）：早于窗口下界 → 下界（用最老的历史态判定）；不早于当前帧 → 无回溯必要
            int oldest = _auth.Frame - SimConfig.LagCompHistory;
            if (targetFrame < oldest) targetFrame = oldest;
            if (targetFrame < 0) targetFrame = 0;

            SimInputFrame fire = default;
            fire.EntityId = entityId;
            fire.Buttons = SimInputFrame.ButtonFire | SimInputFrame.ButtonFireFlag;   // 回溯补判标记（服务器内部构造）

            if (SimConfig.LagCompHistory <= 0 || targetFrame >= _auth.Frame) return RunDegraded(playerId, fire, targetFrame);
            if (!_ring.ContainsFrame(targetFrame)) return RunDegraded(playerId, fire, targetFrame);
            if (!TryGetHistorical(playerId, targetFrame, out SimInputFrame historical)) return RunDegraded(playerId, fire, targetFrame);

            // 瞄准/移动取"当时所见"——补偿的全部意义所在（位置由环上的历史态给出，弹道由历史
            // AimPoint 给出）。**AimPoint 必须带**（单口径：判定/爆头/朝向都读它——《固定斜视角
            // 射击方案专项设计》§4）——漏带 ⇒ 回溯判定退化为"无点无效开火"、与客户端预测不一致
            // （准心命中头部却判不中）。
            fire.MoveX = historical.MoveX;
            fire.MoveZ = historical.MoveZ;
            fire.AimPointX = historical.AimPointX;
            fire.AimPointY = historical.AimPointY;
            fire.AimPointZ = historical.AimPointZ;

            _auth.CopyTo(_scratch);                          // ① 暂存权威当前态
            if (!_ring.TryRestore(targetFrame, _auth))       // ② 环竞态兜底（ContainsFrame 与 Restore 之间不可能变，防御）
            {
                _scratch.CopyTo(_auth);
                return RunDegraded(playerId, fire, targetFrame);
            }

            ulong rngBefore = _auth.RngState;                // 回溯判定不消费权威随机数（还原时一并回滚）
            ClearFireBuffers();
            _fireInputs[0] = fire;
            ShootingSystem.Run(_auth, _map, _fireInputs);    // ③ 单系统执行（不 Step）

            FrameEventBuffer events = _auth.Events;          // 值类型快照（Items 引用不变）
            CommandBuffer cmds = _auth.Cmds;
            int eventCount = events.Count;
            bool hit = HasHit(events, eventCount);

            _scratch.CopyTo(_auth);                          // ④ 还原权威当前态（帧号/RngState 一起回当前帧）
            _auth.RngState = rngBefore;
            _auth.Events = events;                           // 命令/事件落到当前帧缓冲，由本步后续的 FlushCommands 结算
            _auth.Cmds = cmds;
            _ = shooterSlot;
            CompensatedCount++;
            return Set(Outcome.Compensated, targetFrame, hit || cmds.Count > 0);
        }

        /// <summary>
        /// 退化路径：不回溯，按当前帧判定（关闭补偿 / 窗口外 / 无历史的统一退路）。
        ///
        /// **AimPoint 口径**：退化只放弃"位置回溯"，**不放弃瞄准点**——能用**当前帧**历史输入就取它
        /// （补判的开火者就是当前帧的射手，其当前帧输入由 <see cref="RecordInputs"/> 记着），
        /// 取不到（补偿关闭/本帧未记录）即保持零值 = **无点无效开火**（只写 Fire 不产命中）。
        /// 否则退化路径会恒无瞄准点 ⇒ 窗口外/关补偿的玩家**永远打不出爆头**，
        /// 且与同帧客户端预测不一致（《固定斜视角射击方案专项设计》§4）。
        /// </summary>
        private Outcome RunDegraded(int playerId, SimInputFrame fire, int targetFrame)
        {
            // 当前帧 AimPoint 补齐（只补点，不补 EntityId/Buttons——那两项由调用方构造）。
            // 失败（无当前帧历史）即保持零值，不阻断退化判定。
            if (TryGetHistorical(playerId, _auth.Frame, out SimInputFrame current))
            {
                fire.AimPointX = current.AimPointX;
                fire.AimPointY = current.AimPointY;
                fire.AimPointZ = current.AimPointZ;
            }

            ClearFireBuffers();
            _fireInputs[0] = fire;
            ShootingSystem.Run(_auth, _map, _fireInputs);

            FrameEventBuffer events = _auth.Events;
            CommandBuffer cmds = _auth.Cmds;
            bool hit = HasHit(events, events.Count);
            _auth.Events = events;
            _auth.Cmds = cmds;
            DegradedCount++;
            return Set(Outcome.DegradedFire, targetFrame, hit || cmds.Count > 0);
        }

        private void ClearFireBuffers()
        {
            _auth.Cmds.Clear();
            _auth.Events.Clear();
        }

        private bool TryGetHistorical(int playerId, int frame, out SimInputFrame input)
        {
            input = default;
            if (!_history[playerId].TryGet(frame, out SimInputFrame[] inputs, out bool[] _)) return false;
            input = inputs[playerId];
            return true;
        }

        /// <summary>是否产出命中事件（<b>Hit 与 Crit 都算命中</b>——爆头走 <see cref="FrameEventKind.Crit"/>，
        /// 只认 Hit 会让爆头在回溯/退化路径上不计命中，统计与反馈口径随之失真。
        /// 三维化裁决见《固定斜视角射击方案专项设计》§5。 </summary>
        private static bool HasHit(FrameEventBuffer events, int count)
        {
            for (int i = 0; i < count; i++)
            {
                FrameEventKind k = events.Items[i].Kind;
                if (k == FrameEventKind.Hit || k == FrameEventKind.Crit) return true;
            }
            return false;
        }

        private Outcome Set(Outcome outcome, int targetFrame, bool hit)
        {
            LastOutcome = outcome;
            LastTargetFrame = targetFrame;
            LastHit = hit;
            return outcome;
        }
    }
}
