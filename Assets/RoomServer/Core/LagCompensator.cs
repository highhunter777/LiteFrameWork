using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 命中延迟补偿 = 服务器回溯（《状态同步实施方案》§3.4.1 + 《M10实施指导》决策 8）。
    /// </summary>
    public sealed class LagCompensator
    {
        /// <summary>一次回溯判定的结果（诊断/Ops；也是用例断言点）。</summary>
        public enum Outcome
        {
            /// <summary>无开火位（玩家没按开火；也是 <see cref="LastOutcome"/> 的初始值）。</summary>
            NoFire = 0,
            /// <summary>回溯成功并执行了判定。</summary>
            Compensated = 1,
            /// <summary>退化路径下执行了判定（当前帧开火）——**超窗/补偿关闭/无历史都归此值**：
            /// 退化路径只在"确实有一条开火要判"时才被调用，故不存在"退化了但没开火"的第四态
            /// （原 `OutOfWindow` 值全项目从不返回，P3 退役）。</summary>
            DegradedFire = 2,
            /// <summary>输入里的实体无法解析（已死亡/伪造）→ 不判定。</summary>
            InvalidShooter = 3,
        }

        private readonly SimWorldState _auth;
        private readonly SimMapData _map;
        private readonly SnapshotRing _ring;
        private readonly int _playerCount;
        private readonly int _historyCapacity;
        /// <summary>房间玩法数值实例（回溯单系统共享同一装载值——技术债 #1：不读全局静态面）。</summary>
        private readonly CombatValues _values;
        /// <summary>房间武器表实例（回溯单系统同链——武器门/伤害/射程的判定依据）。</summary>
        private readonly WeaponTable _weapons;

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

        public LagCompensator(SimWorldState auth, in SimMapData map, int playerCount, SnapshotRing ring,
            in CombatValues values, WeaponTable weapons)
        {
            _auth = auth;
            _map = map;                                  // 障碍是静态判定数据（回溯窗内逐帧同值——历史帧与当前帧同一份）
            _playerCount = playerCount;
            _values = values;
            _weapons = weapons;
            _ring = ring;                                   // 与 RoomRuntime.SnapshotHistory 同一份（权威循环每帧 Capture）
            _scratch = new SimWorldState();
            _fireInputs = new SimInputFrame[playerCount];
            _historyCapacity = SimConfig.LagCompHistory + 1;
            _history = new InputHistory[playerCount];
            for (int i = 0; i < playerCount; i++) _history[i] = new InputHistory(_historyCapacity, playerCount);
        }

        /// <summary>
        /// 权威循环每帧 Step 后调用：记录该帧实际消费的输入（回溯判定的历史来源）。
        /// </summary>
        public void RecordInputs(int frame, SimInputFrame[] consumed)
        {
            for (int i = 0; i < _playerCount; i++) _history[i].Record(frame, consumed, null);
        }

        /// <summary>
        /// 处理一条开火输入：回溯到玩家所见帧判定，命中结果以命令/事件形式落到**当前帧**。
        /// </summary>
        public Outcome CompensateFire(int playerId, long entityId, int viewFrame, int clientAckSnapshot)
        {
            if (!_auth.TryResolve(entityId, out int shooterSlot)) return Set(Outcome.InvalidShooter, -1, false);

            int targetFrame = viewFrame;

            // 窗口 clamp（反作弊第一刀）：早于窗口下界 → 下界（用最老的历史态判定）；不早于当前帧 → 无回溯必要
            int oldest = _auth.Frame - SimConfig.LagCompHistory;
            if (targetFrame < oldest) targetFrame = oldest;
            if (targetFrame < 0) targetFrame = 0;

            SimInputFrame fire = default;
            fire.EntityId = entityId;
            fire.Buttons = SimInputFrame.ButtonFire | SimInputFrame.ButtonFireFlag;   // 回溯补判标记（服务器内部构造）

            if (SimConfig.LagCompHistory <= 0 || targetFrame >= _auth.Frame) return RunDegraded(playerId, fire, targetFrame);
            if (!_ring.ContainsFrame(targetFrame)) return RunDegraded(playerId, fire, targetFrame);
            if (!TryGetHistorical(playerId, entityId, _auth.Frame, out SimInputFrame historical)) return RunDegraded(playerId, fire, targetFrame);

            // 瞄准点取本次已准入开火输入，不能套用视点帧的旧瞄准点。
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

            // ③ **射手取当前位**：客户端是从"当前预测位/当前朝向"开枪的，把射手连同武器行也回溯到 历史帧会让枪口起点/遮挡关系与玩家操作错位（移动射击时系统性偏后）。
            CopyCurrentShooter(shooterSlot);

            ulong rngBefore = _scratch.RngState;            // 恢复当前权威随机流，不能倒退到历史帧的 RNG
            ClearFireBuffers();
            _fireInputs[0] = fire;
            if (_scratch.Entities[shooterSlot].LifeStartFrame <= targetFrame)
                ShootingSystem.Run(_auth, _map, _fireInputs, _values, _weapons); // 不接受上一生命的开火
            FilterHistoricalResults(targetFrame);

            FrameEventBuffer events = _auth.Events;          // 值类型快照（Items 引用不变）
            CommandBuffer cmds = _auth.Cmds;
            int eventCount = events.Count;
            bool hit = HasHit(events, eventCount);

            _scratch.CopyTo(_auth);                          // ④ 还原权威当前态（帧号/RngState 一起回当前帧）
            _auth.RngState = rngBefore;
            _auth.Events = events;                           // 命令/事件落到当前帧缓冲，由本步后续的 FlushCommands 结算
            _auth.Cmds = cmds;
            CompensatedCount++;
            return Set(Outcome.Compensated, targetFrame, hit || cmds.Count > 0);
        }

        /// <summary>
        /// 退化路径：不回溯，按当前帧判定（关闭补偿 / 窗口外 / 无历史的统一退路）。
        /// </summary>
        private Outcome RunDegraded(int playerId, SimInputFrame fire, int targetFrame)
        {
            // 当前帧 AimPoint 补齐（只补点，不补 EntityId/Buttons——那两项由调用方构造）。
            // 失败（无当前帧历史）即保持零值，不阻断退化判定。
            if (TryGetHistorical(playerId, fire.EntityId, _auth.Frame, out SimInputFrame current))
            {
                fire.AimPointX = current.AimPointX;
                fire.AimPointY = current.AimPointY;
                fire.AimPointZ = current.AimPointZ;
            }

            ClearFireBuffers();
            _fireInputs[0] = fire;
            ShootingSystem.Run(_auth, _map, _fireInputs, _values, _weapons);

            FrameEventBuffer events = _auth.Events;
            CommandBuffer cmds = _auth.Cmds;
            bool hit = HasHit(events, events.Count);
            _auth.Events = events;
            _auth.Cmds = cmds;
            DegradedCount++;
            return Set(Outcome.DegradedFire, targetFrame, hit || cmds.Count > 0);
        }

        /// <summary>
        /// 把射手槽的**当前权威态**（位置/朝向/标志/武器行）写回已回溯到历史帧的 <paramref name="auth"/>。
        /// </summary>
        private void CopyCurrentShooter(int shooterSlot)
        {
            if (shooterSlot < 0 || shooterSlot >= SimConfig.MaxEntities) return;

            ref EntitySlot current = ref _scratch.Entities[shooterSlot];
            ref EntitySlot historical = ref _auth.Entities[shooterSlot];

            // 几何/输入语境取当前（客户端从当前预测位开枪）：
            historical.Pos = current.Pos;
            historical.Vel = current.Vel;
            historical.Yaw = current.Yaw;
            historical.Flags = current.Flags;
            historical.SelectedWeapon = current.SelectedWeapon;
            historical.SemiFireArmed = current.SemiFireArmed;

            // **生命/武器账本不同步回写**：同帧互杀时，先结算的射手可能已把后者 Hp 打到 0 并清空其武器行——但后者在视点帧确实开了这一枪，回溯必须用其**开火时**的生命与武器 （历史态）。
            if (current.Hp > 0)
            {
                int weaponBase = shooterSlot * SimConfig.WeaponSlotsPerEntity;
                System.Array.Copy(_scratch.Weapons, weaponBase, _auth.Weapons, weaponBase,
                    SimConfig.WeaponSlotsPerEntity);
            }
        }

        private void ClearFireBuffers()
        {
            _auth.Cmds.Clear();
            _auth.Events.Clear();
        }

        /// <summary>历史射线只确认几何；当前生命/保护仍以当前权威态为准。</summary>
        private void FilterHistoricalResults(int targetFrame)
        {
            int count = 0;
            for (int i = 0; i < _auth.Cmds.Count; i++)
            {
                SimCommand cmd = _auth.Cmds.Items[i];
                if (cmd.Kind == SimCommandKind.Damage && !CanApplyHistoricalHit(cmd.Target, targetFrame)) continue;
                _auth.Cmds.Items[count++] = cmd;
            }
            _auth.Cmds.Count = count;
            count = 0;
            for (int i = 0; i < _auth.Events.Count; i++)
            {
                FrameEvent e = _auth.Events.Items[i];
                if ((e.Kind == FrameEventKind.Hit || e.Kind == FrameEventKind.Crit)
                    && !CanApplyHistoricalHit(e.EntityId, targetFrame)) continue;
                _auth.Events.Items[count++] = e;
            }
            _auth.Events.Count = count;
        }

        private bool CanApplyHistoricalHit(long entityId, int targetFrame)
        {
            if (!_scratch.TryResolve(entityId, out int slot)) return false;
            ref EntitySlot current = ref _scratch.Entities[slot];
            return current.Hp > 0 && current.LifeStartFrame <= targetFrame
                && _scratch.Frame >= current.InvulnerableUntilFrame;
        }

        private bool TryGetHistorical(int playerId, long entityId, int frame, out SimInputFrame input)
        {
            input = default;
            if (!_history[playerId].TryGet(frame, out SimInputFrame[] inputs, out bool[] _)) return false;
            // SimStep 将输入按 EntityId 排序；缺席玩家的默认输入会移到前面，数组下标不再等于 playerId。
            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].EntityId != entityId) continue;
                input = inputs[i];
                return true;
            }
            return false;
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
