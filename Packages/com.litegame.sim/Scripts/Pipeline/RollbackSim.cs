using System;

namespace LiteSim
{
    /// <summary>
    /// 回滚执行器（《状态同步实施方案》§5.3–5.4）：
    /// 预测推进（沿用上一帧、开火不预测）+ 真实输入判定（不符则 Restore(F-1) → 重放 F..last）
    /// + 越界退化（停预测前进，§5.4 正确性兜底）+ 单渲染帧回滚上限（防雪崩）。
    ///
    /// - FrameDriver 只承担累加器/追帧；逐逻辑帧输入经 onLogicalFrame 回调在帧间刷新
    ///   （多逻辑帧/渲染帧时每帧各自的预测输入）。
    /// - 帧号约定：帧号 = 已执行步数（Step 末 Frame 递增）；"输入 k"由第 k 步消费（Frame k-1 → k）；
    ///   Capture 在每步后记录帧 k，构造期先把初始状态锚定为帧 0——第 1 步的回滚（Restore(0)）天然可用。
    ///   收到步 F 的真实输入时 Frame ≥ F 即"已用预测跑过"，target = 帧 F-1（= F 步执行前状态），与 §5.4 伪码自洽。
    /// - 同运行时红线：全部验收在 .NET 侧闭环；跨运行时存在 FMA 1-ULP 底噪。
    /// - 所有权：initialState 由调用方构造后移交本类（§8.3：BattleContext 显式 new，禁入容器）。
    /// </summary>
    public sealed class RollbackSim
    {
        private readonly SimMapData _map;
        private readonly int _playerCount;
        private readonly SimWorldState _state;
        private readonly SnapshotRing _ring;
        private readonly InputHistory _history;
        private readonly FrameDriver _driver;
        private readonly CombatValues _values;          // 玩法数值实例（装载传入——技术债 #1：运行期不读全局）
        private readonly WeaponTable _weapons;          // 武器表实例（同上——同族参数化）
        private readonly MatchTestRules _rules;         // 本局测试规则（可变持有件——与权威侧同实例，中局开合即时生效）
        private readonly bool _resolveCombat;            // 预测模式：false = 不产权威伤害/命中/死亡（客户端）
        private readonly SimInputFrame[] _tickInputs;   // 当前逻辑帧使用的输入（帧间经 OnLogicalFrame 刷新）
        private readonly bool[] _tickPredicted;
        private bool _halted;
        private int _rollbacksThisFrame;
        private int _rollbackCount;
        private int _deferredCount;
        private int _haltCount;
        private int _reconcileCount;
        private SimWorldState _probe;           // 和解比对探针态（惰性创建，复用——低频事件不违预分配精神）

        /// <summary>回滚回调（View.Realign 接缝：参数 = 重放到的帧号）。Sim 不做 IO——订阅方自理；
        /// event 形态：多家订阅互不覆盖（解订阅对称）。</summary>
        public event Action<int> OnRollback;

        /// <summary>和解回调（客户端上报 MismatchReport 的接缝：参数 = 和解帧号）。event 形态同 <see cref="OnRollback"/>。</summary>
        public event Action<int> OnReconcile;

        /// <summary>
        /// 帧事件交付（**消费帧事件的唯一时机**——<see cref="FrameDriver"/> 在回调返回后立即清空
        /// 事件缓冲，事后轮询永远读不到；参数 = 本逻辑帧的状态，事件在 <c>s.Events</c>）。
        ///
        /// 与 <see cref="OnRollback"/>/<see cref="OnReconcile"/> 的区别：后两者是**偶发**的和解/重放信号，
        /// 本回调**每逻辑帧**都来（追帧时一帧一次）——表现层（SimView 的事件静默门）据此消费开火/命中/死亡。
        /// 重放段（<see cref="ExecuteRollback"/>/<see cref="OnAuthoritativeSnapshot"/> 内部）不走本回调：
        /// 那些 Step 的事件不消费即清，正是不重播一次性副作用的来源。
        /// </summary>
        public event Action<SimWorldState> OnFrameEvents;

        /// <summary><paramref name="rules"/> = 本局测试规则持有件（null = 无规则）——绑定后随每一次
        /// 步进/重放注入（含回滚重放段，两端同一实例口径见 <see cref="MatchTestRules"/>）。</summary>
        public RollbackSim(SimWorldState initialState, SimMapData map, SimInputFrame[] inputTemplate,
            in CombatValues values, WeaponTable weapons, MatchTestRules rules = null, bool resolveCombat = true)
        {
            _state = initialState;
            _map = map;
            _values = values;
            _weapons = weapons;
            _rules = rules;
            _resolveCombat = resolveCombat;
            _playerCount = inputTemplate.Length;
            _ring = new SnapshotRing(SimConfig.MaxRollbackFrames + 1);
            _history = new InputHistory(SimConfig.MaxInputHistory, inputTemplate.Length);
            _driver = new FrameDriver();
            _tickInputs = new SimInputFrame[inputTemplate.Length];
            Array.Copy(inputTemplate, _tickInputs, inputTemplate.Length); // 身份基线（EntityId 必带、控制量建议零）——冷启动预测起点
            _tickPredicted = new bool[inputTemplate.Length];
            _ring.Capture(0, _state);      // 帧号锚点：初始状态 = 帧 0（第 1 步回滚的 Restore 目标，帧前状态唯一来源）
        }

        public SimWorldState State => _state;
        public bool Halted => _halted;
        public int RollbackCount => _rollbackCount;
        public int DeferredRollbacks => _deferredCount;
        public int HaltCount => _haltCount;
        public int ReconcileCount => _reconcileCount;

        /// <summary>诊断/测试用：重放段逐帧修正验证。</summary>
        public SnapshotRing Ring => _ring;

        /// <summary>
        /// 本渲染帧将推进的逻辑帧数（纯查询，见 <see cref="FrameDriver.PeekSteps"/>）——
        /// 预发"即将执行的沿用帧"用：多步渲染帧要把额外帧的输入**先于步进**上行，
        /// 否则进程内权威先走完的那几帧只能吃空输入兜底（与本地沿用逐帧分叉）。
        /// </summary>
        public int PeekSteps(float realDelta) => _driver.PeekSteps(realDelta);

        /// <summary>
        /// 读取某帧**已执行**的某玩家输入（追帧补发用）。追帧沿用帧（<see cref="PrepareNext"/> 的
        /// baseInputs 续行）与真实输入帧同读——多逻辑帧渲染帧里，未上行的沿用帧会让服务器按空输入
        /// 兜底执行，产生"本地在动、权威已停"的分叉（《状态同步专项设计》两端同帧同值前提），
        /// 调用方（BattleContext）须把这些帧同样 SendInput。
        ///
        /// **按实体 Id 查找**（不是数组下标）：历史数组由 SimStep **就地按 EntityId 排序**（规范形），
        /// 而远端槽位的输入常携带零身份（客户端填不出远端 Id）——零身份排在数组最前，会把
        /// "本地玩家在数组里的下标"挤位，按下标读会取到别人的行（旧口径的错）。
        /// <paramref name="entityId"/> = 目标实体 Id（0 = 无目标，返回 false）。frame 必须 ≤ 当前已执行帧
        /// （未来帧不在史里）；超出史窗/未记录/未找到 = false。
        /// </summary>
        public bool TryGetExecutedInput(int frame, long entityId, out SimInputFrame input)
        {
            input = default;
            if (frame <= 0 || frame > _state.Frame) return false;   // 只读已执行帧（0 = 初始锚定，未执行）
            if (entityId == 0) return false;
            if (!_history.TryGet(frame, out var stored, out var _)) return false;
            for (int i = 0; i < _playerCount; i++)
            {
                if (stored[i].EntityId != entityId) continue;   // lint-allow R3（64 位整型 Id 判等，非浮点精度比较）
                input = stored[i];
                return true;
            }
            return false;
        }

        /// <summary>预测推进。停预测（越界退化）期间不推进——§5.4 强制等待。</summary>
        public void Tick(float realDelta)
        {
            if (_halted) return;

            PrepareNext(_state.Frame + 1);   // 每渲染帧预备下一帧输入（幂等：历史未变则结果不变）
            _rollbacksThisFrame = 0;         // 渲染帧边界（单帧回滚上限的计数窗口）
            _driver.Tick(realDelta, _state, _map, _tickInputs, _values, _weapons, OnLogicalFrame, _rules, _resolveCombat);
        }

        /// <summary>
        /// 真实输入到达（网络层喂入）：入史 → 判定（帧已模拟 且 已用预测输入 且 逐位不符
        /// → Restore(F-1) → 重放 F..last，§5.4）。早到帧（frame &gt; 已执行帧号）仅入史供模拟时取用，
        /// 并解锁停预测（确认流越过不可恢复窗口即续跑）。
        /// </summary>
        public void OnRealInput(int frame, SimInputFrame[] realInputs)
        {
            if (frame > _state.Frame)
            {
                _history.Overwrite(frame, realInputs);
                if (_halted) _halted = false;
                return;
            }

            // 覆盖前读取判定材料（Overwrite 会就地覆写内部数组）
            bool anyPredicted = _history.IsAnyPredicted(frame);
            bool differs = anyPredicted && _history.Differs(frame, realInputs);
            _history.Overwrite(frame, realInputs);

            if (!differs) return;                            // 预测正确：零回滚（§5.4 自检；重复确认幂等）
            if (_halted) return;                              // 停预测期不回滚（真实值已入史）
            if (_rollbacksThisFrame >= SimConfig.MaxRollbacksPerFrame)
            {
                _deferredCount++;                            // 丢弃（权威快照覆盖兜底）
                return;
            }

            ExecuteRollback(frame);
        }

        private void OnLogicalFrame(SimWorldState s)
        {
            _ring.Capture(s.Frame, s);                        // Step 后捕获
            _history.Record(s.Frame, _tickInputs, _tickPredicted);
            PrepareNext(s.Frame + 1);                         // 下一逻辑帧输入（多逻辑帧各自决议）

            // 帧事件交付**必须最后做**：FrameDriver 在回调返回后清空事件缓冲，
            // 这里是消费方读事件的最后时机（SimView 静默门在此取件）。
            OnFrameEvents?.Invoke(s);
        }

        /// <summary>下一逻辑帧输入决议：历史早到真实输入优先（逐玩家）；否则沿用上一帧（Buttons=0——开火不预测）。</summary>
        private void PrepareNext(int nextFrame)
        {
            bool hasReal = _history.TryGet(nextFrame, out var stored, out var pred);
            bool hasBase = _history.TryGet(nextFrame - 1, out var baseInputs, out var _);
            if (!hasBase) baseInputs = _tickInputs;           // 首帧无前驱 → 零输入预测

            for (int i = 0; i < _playerCount; i++)
            {
                if (hasReal && !pred[i])
                {
                    _tickInputs[i] = stored[i];               // 已确认真实输入
                    _tickPredicted[i] = false;
                }
                else
                {
                    _tickInputs[i] = baseInputs[i];           // 沿用移动/朝向（§5.3）
                    // 只保留**连续意图位**（见 SimInputFrame.PredictedButtons）：开火与离散意图不预测
                    // （离散事件猜错代价极大）；连续位跟着沿用，否则"举枪/松开"会在缺真实输入的帧闪断。
                    _tickInputs[i].Buttons &= SimInputFrame.PredictedButtons;
                    _tickPredicted[i] = true;
                }
            }
        }

        private void ExecuteRollback(int frame)
        {
            int target = frame - 1;
            int last = _state.Frame;                          // 回滚前已执行到的帧号（先取——Restore 会改写 Frame）
            if (!_ring.TryRestore(target, _state))
            {
                _halted = true;                               // 超出深度停预测（强制等待）
                _haltCount++;
                return;
            }

            for (int f = frame; f <= last; f++)                // §5.4：重放第 F..last 步（Step 内 Frame 递增，上界固定）
            {
                if (!_history.TryGet(f, out var inputs, out var _))
                {
                    _halted = true;                           // 防御（环窗口 ⊆ 历史窗口，理论不达）
                    _haltCount++;
                    return;
                }

                SimStep.Step(_state, _map, inputs, _values, _weapons, rules: _rules, resolveCombat: _resolveCombat);
                _ring.Capture(_state.Frame, _state);          // 重放段快照同步更新（后续回滚的基点）
                _state.Events.Clear();                        // 重放期事件不消费即清
            }

            PrepareNext(_state.Frame + 1);                     // 回滚后下一帧输入预备
            _rollbacksThisFrame++;
            _rollbackCount++;
            if (OnRollback != null) OnRollback(_state.Frame); // View.Realign 接缝
        }

        /// <summary>
        /// **全量口径**和解入口（保留）：比对本地预测@frame 的 <see cref="SimChecksum.ComputePublicChecksum"/>
        /// 与权威值。全量帧/重连/单测直构形态用它；增量帧走
        /// <see cref="OnAuthoritativeSnapshot(int, SimWorldState, in AuthoritativeFrameInfo)"/>（部分和解）。
        /// </summary>
        public bool OnAuthoritativeSnapshot(int frame, SimWorldState authoritative, uint authoritativeChecksum)
            => Reconcile(frame, authoritative, isFull: true, authoritativeChecksum, 0u, 0u, null, null, 0);

        /// <summary>
        /// **增量感知**和解入口（P2-A）：全量帧走全图口径；增量帧只按**消息携带的槽位集合**做部分和解——
        /// 视野外实体的变化不再导致每快照失配和解（全图 checksum 不再为 AOI 增量当裁判）。
        /// 语义见 <see cref="AuthoritativeFrameInfo"/>。
        /// </summary>
        public bool OnAuthoritativeSnapshot(int frame, SimWorldState authoritative, in AuthoritativeFrameInfo info)
            => Reconcile(frame, authoritative, info.IsFull, info.FullChecksum, info.VisibleChecksum,
                info.MatchChecksum, info.Slots, info.SlotChecksums, info.SlotCount);

        /// <summary>
        /// 权威快照和解核心（《状态同步实施方案》§5 章头："回退源=权威快照、重放范围=本地输入"）。
        ///
        /// - **快照超前/帧太老**（环窗口外）：权威态直接覆盖续跑（快照覆盖兜底语义）。
        /// - **全量帧**（<paramref name="isFull"/>）：本地预测@frame 公共口径 checksum 与全图权威比对——
        ///   一致 = 零和解；不符 = 权威整帧覆盖 + 重放。
        /// - **增量帧**：本地预测的**携带槽位集合**公共口径 + Match 与权威比对——一致 = 零和解；
        ///   不符 = 逐槽位比对定位差异槽，**只把差异槽位的权威面写回**（视野外保留本地预测，
        ///   不整帧复制镜像——避免"误回退"），再重放本地历史。
        /// - 重放终点一律截到**预测领先上限**（<see cref="SimConfig.MaxPredictionLead"/>）：领先随卡顿累积、
        ///   越过输入冗余覆盖范围后服务器再也找不到要的帧号，输入整包判"越界"、回滚锁死（现场实测形态）。
        ///   被截帧留在历史里，本地随后按原输入自然重演（状态等价，仅帧号回退到上限）。
        /// - 私有面（他人弹药/技能 CD/背包/资源、RngState、状态明细）客户端永远无法重建，不进比对口径。
        /// </summary>
        private bool Reconcile(int frame, SimWorldState authoritative, bool isFull, uint fullChecksum,
            uint visibleChecksum, uint matchChecksum, int[] slots, uint[] slotChecksums, int slotCount)
        {
            if (frame < 0 || authoritative == null) return false;

            if (frame > _state.Frame || !_ring.ContainsFrame(frame))
            {
                // 超前/太老：权威态直接覆盖（快照覆盖兜底），预测从新基线继续
                authoritative.CopyTo(_state);
                PrepareNext(_state.Frame + 1);                          // 与 replayed 路径同款：新基线确立后刷新下帧输入
                _reconcileCount++;
                if (OnReconcile != null) OnReconcile(frame);
                return true;
            }

            // 环内：本地预测@frame 的探针态（比对材料）
            _probe = _probe ?? new SimWorldState();
            _ring.TryRestore(frame, _probe);

            int last = _state.Frame;
            int replayEnd = last < frame + SimConfig.MaxPredictionLead ? last : frame + SimConfig.MaxPredictionLead;

            if (isFull)
            {
                uint localChecksum = SimChecksum.ComputePublicChecksum(_probe);
                if (localChecksum == fullChecksum) return false;   // 预测正确——零和解（lint-allow R3：uint 位级判等）

                // 不符：权威**整帧**覆盖 + 重放本地历史（全量帧 = 全图权威，缺席判死语义成立）
                authoritative.CopyTo(_state);
                ReplayHistory(frame, replayEnd);
                return FinishReconcile(frame);
            }

            // 增量：可见摘要 + Match 摘要短路（一致 ⇒ 零和解）；不等 ⇒ 逐槽位定位差异槽
            uint localVisible = SimChecksum.ComputeVisibleChecksum(_probe, slots, slotCount);
            uint localMatch = SimChecksum.ComputeMatchChecksum(in _probe.Match);
            if (localVisible == visibleChecksum && localMatch == matchChecksum) return false;   // lint-allow R3

            // 部分和解：只把**差异槽位**的权威面写回探针；视野外槽位保留本地预测（不整帧复制镜像——
            // 否则会把视野外本地预测拉回旧镜像，问题从"误和解"变成"误回退"）。槽位集在增量下不变
            // （活体变化必转全量），故不动活体位图。Match 非预测量——以权威为准。
            for (int k = 0; k < slotCount; k++)
            {
                int slot = slots[k];
                uint localSlot = SimChecksum.ComputeEntityPublicChecksum(
                    in _probe.Entities[slot], in _probe.Actions[slot * SimConfig.ActionSlotsPerEntity]);
                if (localSlot == slotChecksums[k]) continue;   // 该槽一致，不动（lint-allow R3）
                _probe.Entities[slot] = authoritative.Entities[slot];
                _probe.Actions[slot * SimConfig.ActionSlotsPerEntity] =
                    authoritative.Actions[slot * SimConfig.ActionSlotsPerEntity];
            }
            _probe.Match = authoritative.Match;

            _probe.CopyTo(_state);
            ReplayHistory(frame, replayEnd);
            return FinishReconcile(frame);
        }

        /// <summary>重放本地历史 frame+1..<paramref name="replayEnd"/>（不消费事件——重放期事件清掉）。</summary>
        private void ReplayHistory(int frame, int replayEnd)
        {
            for (int f = frame + 1; f <= replayEnd; f++)
            {
                if (!_history.TryGet(f, out var inputs, out var _)) break;   // 历史窗口外（不应达——32 > 深度）
                SimStep.Step(_state, _map, inputs, _values, _weapons, rules: _rules, resolveCombat: _resolveCombat);
                _state.Events.Clear();
            }
        }

        /// <summary>和解收口：刷新下帧输入基线 + 计数 + 上报（与 <see cref="ExecuteRollback"/> 对称）。</summary>
        private bool FinishReconcile(int frame)
        {
            PrepareNext(_state.Frame + 1);
            _reconcileCount++;
            if (OnReconcile != null) OnReconcile(frame);
            return true;
        }
    }
}
