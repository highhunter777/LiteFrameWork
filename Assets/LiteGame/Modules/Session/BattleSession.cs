using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteNet.Proto;
using LiteSim;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Input;

namespace LiteGame
{
    /// <summary>
    /// 对局聚合（路线图 M0 接缝 4 / M2）：Match 域的唯一持有点——持有 <see cref="MatchSession"/>
    /// （服务域 + <see cref="BattleContext"/>），并把本局规则实例绑定到开发会话端口
    /// （<see cref="IDevSession.MatchRules"/>）供调试投影读取，离场解绑。
    ///
    /// 为什么是聚合：M0 之前"当前局"是隐式全局（Seeder + BattleContext 两处静态写）；聚合后
    /// 规则实例随会话显式传递、经端口暴露给 dev 投影，Sim 侧不再有静态"当前局"。
    ///
    /// **流程端口**：本类实现 <see cref="IBattleRun"/>——流程段只认 StartGame 等待/
    /// 终局事实/每帧驱动/离场，不再直引 <c>BattleClient</c>/<c>BattleContext</c>/<c>RoomSessionFace</c>。
    ///
    /// 生命周期：构造即接线，<see cref="Dispose"/> 先摘订阅/解绑再释放视图域与 Match 域（不留下局可读状态）。
    ///
    /// **诊断所有权（M2）**：对局诊断读口（<c>BattleDiagnostics</c>，开发三宏内）的登记/注销归本会话——
    /// 与会话同生共死，视图域（<see cref="IBattleViewGroup"/>）只做装配/拆除，不再登记静态读口。
    /// </summary>
    internal sealed class BattleSession : IBattleRun
    {
        private readonly IDevSession _dev;
        private readonly BattleClient _battle;
        private IRoomSessionFace _roomSession;
        private bool _disposed;

        public MatchSession Match { get; }

        /// <summary>对局上下文（网络 + Sim + 表现端口）。</summary>
        public BattleContext Context => Match.Context;

        /// <summary>表现视图域（Battle 阶段创建后挂入；null = 尚未建立/已拆除）。</summary>
        public IBattleViewGroup View { get; private set; }

        /// <summary>本局规则实例（null = 常规房无测试规则）。</summary>
        public MatchTestRules Rules => Context.MatchRules;

        /// <summary>对局结束原因（未结束 = null）；流程只从会话读终局事实。</summary>
        public BattleRunOutcome? Outcome => Map(Context.EndedReason);

        /// <summary>冻结的终局结果（结算页数据源；未结束 = null）。</summary>
        public MatchEnded MatchResult => Context.MatchResult;

        /// <summary>本地席位（-1 = 未分配）。</summary>
        public int PlayerId => Context.PlayerId;

        /// <summary>和解次数（诊断/日志）。</summary>
        public long ReconcileCount => Context.ReconcileCount;

        /// <summary>本局地图行键（JoinAck 权威事实）。</summary>
        public int MapId => _battle.Client.MapId;

        /// <summary>StartGame 是否已到达（重入/迟到等待用）。</summary>
        public bool HasStarted => _battle.Client.HasStartGame && _battle.Client.StartGame != null;

        /// <summary>解读世界是否已建立。</summary>
        public bool HasSimulation => Context.Sim != null;

        /// <summary>房间会话门面（与 BattleClient 同生共死；页面经 Bridge.room 读房主事实）。</summary>
        public IRoomSessionFace RoomSession => _roomSession ??= new RoomSessionFace(_battle);

        public event Action<BattleRunOutcome> Ended;

        public BattleSession(MatchSession match, IDevSession dev = null)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            _battle = Match.BattleClient;
            _dev = dev;
            Context.RulesBound += PublishRules;
            Context.Ended += OnContextEnded;
            if (Rules != null) _dev?.AttachMatchRules(Rules);   // 进房即有种子规则（StartGame 前投影也可读）
            DevHooks.Diagnostics?.Attach(Context);               // 诊断读口登记（开发装配实现；发布无实现 = 不登记）
        }

        /// <summary>挂入视图域（Battle 阶段 StartGame 后建立；生命周期随本会话，先于 Match 域释放）。</summary>
        public void AttachView(IBattleViewGroup view)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BattleSession));
            View = view;
        }

        /// <summary>建视图并接线（对局场景组就位之后；<paramref name="openBattleMenu"/> = 对局菜单入口）。</summary>
        public void ActivateView(Action openBattleMenu = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BattleSession));
            IBattleViewGroup view = View
                ?? throw new InvalidOperationException("视图域未挂入：ActivateView 须在 AttachView 之后调用");
            view.Attach(Context, openBattleMenu);
        }

        /// <summary>拆视图域（幂等）：消费者/视图/租约作用域由视图组自身逆序完成。</summary>
        public void DetachView()
        {
            IBattleViewGroup view = View;
            View = null;
            view?.Dispose();
        }

        /// <summary>等 StartGame 或对局终止（先到先得；true = 已开局）。</summary>
        public async UniTask<bool> WaitStartedOrEndedAsync(CancellationToken ct)
        {
            if (HasStarted) return true;
            if (Outcome.HasValue) return false;

            var ready = new UniTaskCompletionSource<bool>();
            void OnStart(StartGame _) => ready.TrySetResult(true);
            void OnEnd(BattleRunOutcome _) => ready.TrySetResult(false);
            _battle.Client.OnStartGame += OnStart;
            Ended += OnEnd;
            try
            {
                // 订阅后复检：事件可能在首次检查与订阅之间到达（与 StageSignal 的迟到订阅者同口径）。
                if (HasStarted) return true;
                if (Outcome.HasValue) return false;
                return await ready.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                _battle.Client.OnStartGame -= OnStart;   // 胜者败者一并退——一次性等待不留悬挂订阅
                Ended -= OnEnd;
            }
        }

        /// <summary>每帧驱动：瞄准世界注入 → 输入采样 → 对局上下文推进（唯一驱动入口）。</summary>
        public void Tick(float deltaSeconds, IInputService input)
        {
            if (_disposed) return;
            if (input != null && Context.Sim != null)
            {
                // 解析失败一律给 -1（不可沿用 TryResolve 的 out 值：0 是合法槽位）。
                int localSlot = Context.LocalEntityId != 0
                    && Context.Sim.State.TryResolve(Context.LocalEntityId, out int resolved) ? resolved : -1;
                input.SetAimWorld(Context.Sim.State, localSlot);
                input.SampleOnRenderFrame(Context.LocalPosition);
            }
            Context.Tick(deltaSeconds);
        }

        /// <summary>主动离场（幂等）——经 <see cref="Ended"/> 收尾。</summary>
        public void Leave() => Context.Leave();

        /// <summary>规则实例变化（StartGame/权威 GM 位）→ 绑定到 dev 端口（投影读同一实例）。</summary>
        private void PublishRules(MatchTestRules rules) => _dev?.AttachMatchRules(rules);

        private void OnContextEnded(BattleContext.EndReason reason) => Ended?.Invoke(Map(reason));

        private static BattleRunOutcome Map(BattleContext.EndReason reason)
        {
            switch (reason)
            {
                case BattleContext.EndReason.Leave: return BattleRunOutcome.Leave;
                case BattleContext.EndReason.Completed: return BattleRunOutcome.Completed;
                case BattleContext.EndReason.Aborted: return BattleRunOutcome.Aborted;
                default: return BattleRunOutcome.SessionFailed;
            }
        }

        private static BattleRunOutcome? Map(BattleContext.EndReason? reason)
            => reason.HasValue ? Map(reason.Value) : (BattleRunOutcome?)null;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Context.RulesBound -= PublishRules;
            Context.Ended -= OnContextEnded;
            MatchTestRules rules = Rules;
            if (rules != null) _dev?.DetachMatchRules(rules);   // 离场解绑（只解绑本会话的实例）
            DevHooks.Diagnostics?.Detach(Context);               // 诊断读口注销（只注销本局实例——防误注下一局）
            DetachView();
            Match.Dispose();
        }
    }
}
