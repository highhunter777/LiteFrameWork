using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteNet.Proto;
using LiteSim;
using LiteGame.Modules.Battle;
using LiteGame.Modules.Battle.Shared;
using LiteGame.Modules.Input;

using LiteSync.Client;
using LiteGame.Modules.Battle.Abstractions.Run;
using LiteGame.Modules.Battle.Abstractions.View;
using LiteGame.Modules.Battle.Abstractions.Session;
namespace LiteGame
{
    /// <summary>
    /// 对局聚合（路线图 M0 接缝 4 / M2）：Match 域的唯一持有点——持有 BattleDomain （服务域 + BattleContext）。
    /// </summary>
    internal sealed class BattleSession : IBattleRun
    {
        private readonly IDevSession _dev;
        private readonly IBattleDevDiagnostics _diagnostics;
        private readonly IBattleClient _battle;
        private IRoomSessionFace _roomSession;
        private bool _disposed;

        public BattleDomain Match { get; }

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

        /// <summary>房间会话门面（与客户端端口同生共死；页面经 IRoomView 读房主事实）。</summary>
        public IRoomSessionFace RoomSession => _roomSession ??= new RoomSessionFace(_battle);

        public event Action<BattleRunOutcome> Ended;

        public BattleSession(BattleDomain match, IDevSession dev = null,
            IBattleDevDiagnostics diagnostics = null)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            _battle = Match.BattleClient;
            _dev = dev;
            _diagnostics = diagnostics;
            Context.RulesBound += PublishRules;
            Context.Ended += OnContextEnded;
            if (Rules != null) _dev?.AttachMatchRules(Rules);   // 进房即有种子规则（StartGame 前投影也可读）
            _diagnostics?.Attach(Context);               // 诊断读口登记（开发装配实现；发布无实现 = 不登记）
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
            (view as IBattleViewAttachment)?.Attach(Context, openBattleMenu);   // 挂载属实现件接缝（契约包不引 BattleContext）
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
            _diagnostics?.Detach(Context);               // 诊断读口注销（只注销本局实例——防误注下一局）
            DetachView();
            Match.Dispose();
        }
    }
}
