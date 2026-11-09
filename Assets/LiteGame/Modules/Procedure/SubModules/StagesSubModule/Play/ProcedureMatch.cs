using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteNet.Proto;

using LiteSync.Client;
namespace LiteGame
{
    /// <summary>
    /// 进房阶段：消费房间操作句柄 → 建会话/JoinAck → 移交 Battle；本地测试直入复用同一寿命句柄。
    /// </summary>
    public sealed class ProcedureMatch : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>本地测试模式的占位端点（进程内 LocalServerTransport 不建 Socket；GM 本地联测页复用端口常量）。</summary>
        public const string TestHost = "127.0.0.1";
        public const int TestPort = 17777;
        /// <summary>本地测试模式房间号（进程内 Room-Test；全房免死规则见 <see cref="LiteGame.ILocalTestHooks"/>）。</summary>
        public const string TestRoomId = "Room-Test";

        /// <summary>JoinAck 等待预算（毫秒）——超时进 Error（确定失败态，不静默重试）。</summary>
        public const int JoinTimeoutMs = 10_000;

        private readonly IMatchSessionFactory _accounts;
        private readonly RoomFlowLifetime _lifetime;
        private readonly FeedbackService _feedback;

        /// <summary>本阶段在途的进房连接（<see cref="OnUpdate"/> 据它驱动传输泵；未连接时为 null）。</summary>
        private IMatchJoin _pending;

        internal ProcedureMatch(IMatchSessionFactory accounts, RoomFlowLifetime lifetime,
            FeedbackService feedback = null, CancellationToken rootToken = default) : base(rootToken)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _feedback = feedback;
        }

        /// <summary>驱动传输泵（KCP 握手/收发只能轮询推进，本阶段必需）。</summary>
        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
            IMatchJoin pending = _pending;
            if (pending == null) return;
            pending.TickIncoming();
            pending.TickOutgoing();
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
        {
            // 已取票排队的合法票据：先原子验活并消费队列，拒绝时零连接、不回写流程。
            RoomFlowOperation operation = req.RoomOperation;
            if (operation != null)
            {
                if (!_lifetime.TryConsumeMatch(operation, out MetaJoin join))
                {
                    if (!RootToken.IsCancellationRequested) m.Request(ProcedureId.Main);
                    return;
                }
                RunAsyncCore(m, ct, operation, join.Host, join.Port, join.RoomId, join.Ticket,
                    testRoom: false).ForgetLogged("ProcedureMatch");
                return;
            }

            // 唯一免票据入口：开发装配的本地测试直入，用同一寿命句柄走正常消费与收尾。
            if (req.TestRoom && DevBuildFacts.IsDevelopment)
            {
                if (_lifetime.IsBusy)
                {
                    var busy = new InvalidOperationException("房间操作进行中，拒绝本地测试直入");
                    Fail(m, busy, nameof(RunAsync));
                    m.Request(ProcedureId.Error, new ProcedureArgs(busy));
                    return;
                }

                RoomFlowOperation local;
                try
                {
                    local = _lifetime.Begin();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                local.MarkQueuedMatch(new MetaJoin(TestHost, TestPort, TestRoomId, string.Empty));
                if (!_lifetime.TryConsumeMatch(local, out MetaJoin localJoin))
                {
                    if (!RootToken.IsCancellationRequested) m.Request(ProcedureId.Main);
                    return;
                }
                RunAsyncCore(m, ct, local, localJoin.Host, localJoin.Port, localJoin.RoomId,
                    localJoin.Ticket, testRoom: true).ForgetLogged("ProcedureMatch");
                return;
            }

            // 正式包拒绝测试身份；无票据一律显式失败，不退化成 fake。
            var ex = new InvalidOperationException(req.TestRoom
                ? "正式构建拒绝以测试身份进房——真实进房走 Meta 票据（框架先行 §6；上云批C）"
                : "无 Meta 票据：本地测试请用 request enter（Room-Test）；真 KCP 请走本地全链或云端房间");
            Fail(m, ex, nameof(RunAsync));
            m.Request(ProcedureId.Error, new ProcedureArgs(ex));
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct,
            RoomFlowOperation operation, string host, int port, string roomId, string token, bool testRoom)
        {
            IMatchSession account = null;
            bool handedToFlow = false;
            // 进房等待场在票据验活后才开场；JoinAck/失败/取消三路都收口。
            LaunchLoading.Begin(_feedback, "正在进入房间…");
            CancellationToken loadingToken = _feedback?.LoadingToken ?? CancellationToken.None;
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct, loadingToken, operation.Token);
            try
            {
                linkedCts.Token.ThrowIfCancellationRequested();   // Create 前再验一次
                account = _accounts.Create(host, port, roomId, token, testRoom);
                _pending = account;                       // 交给 OnUpdate 驱动泵（握手/收发全靠它推进）

                await WaitJoined(account, linkedCts.Token);
                Log.Info("[Battle] joined (JoinAck)", LogTag.Battle);

                _pending = null;                          // 泵交棒给对局运行端口（不双泵）
                if (!_lifetime.TryQueueBattle(operation, account))
                    throw new OperationCanceledException(operation.Token);
                handedToFlow = true;
                account = null;                           // 会话已由句柄托管，等待 Battle 接管
                LaunchLoading.End();                      // 移交后遮罩不能挡房间页/对局
                if (!m.Request(ProcedureId.Battle, new ProcedureArgs().WithRoomOperation(operation)))
                    throw new InvalidOperationException("Battle 阶段未接受入场请求");
            }
            catch (OperationCanceledException)
            {
                if (handedToFlow)
                    _lifetime.InvalidateCurrent(releaseRequesting: false);   // 未接管会话由句柄回收
                // 阶段/句柄令牌未取消 = 本次取消来自加载面 Back 或 Join 自身的超时/取消：
                // 房间操作已取票，安全回收后回 Main（外部离场由发起方流转，不在此抢写）。
                if (!ct.IsCancellationRequested && !operation.Token.IsCancellationRequested)
                {
                    if (loadingToken.IsCancellationRequested)
                        Log.Info("[Match] 进入房间已取消（加载面 Back）", LogTag.Procedure);
                    else
                    {
                        operation.MarkRoomFailure("进入房间失败", "Join 未完成（超时或会话取消）");
                        Log.Warning("[Match] Join 未完成，清理后回 Main", LogTag.Procedure);
                    }
                    m.Request(ProcedureId.Main);
                }
            }
            catch (Exception ex)
            {
                if (handedToFlow)
                    _lifetime.InvalidateCurrent(releaseRequesting: false);
                if (!linkedCts.IsCancellationRequested)   // 已取消不得再抢转 Error
                {
                    if (operation != null)
                    {
                        // 房间操作（已取票/已排队）的建连或 Join 失败是局内可恢复失败：清理后回 Main，
                        // 由唯一观察者走 RecoveryStep（错误反馈→大厅），不能把整机钉死在 ProcedureError。
                        operation.MarkRoomFailure("进入房间失败", ex.Message);
                        Log.Warning($"[Match] 进入房间失败，清理后回 Main:{ex.Message}", LogTag.Procedure);
                        m.Request(ProcedureId.Main);
                    }
                    else
                    {
                        // 启动/装配路径无操作句柄：仍是不可恢复错误。
                        Fail(m, ex, nameof(RunAsyncCore));
                        m.Request(ProcedureId.Error, new ProcedureArgs(ex));
                    }
                }
            }
            finally
            {
                _pending = null;
                LaunchLoading.End();
                if (!handedToFlow)
                {
                    account?.Dispose();                   // 未移交：本次会话恰一次释放
                    _lifetime.NotifyCleaned(operation);
                }
            }
        }

        /// <summary>等 JoinAck（RoomClient 相位 Idle → Connected；超时 = 确定失败态）。</summary>
        private static UniTask WaitJoined(IMatchJoin join, CancellationToken ct)
            => StageSignal.Wait<JoinAck>(
                h => join.Joined += h,
                h => join.Joined -= h,                   // 临时订阅必须退——持久订阅归对局运行端口
                alreadyDone: false,
                ct,
                timeoutMs: JoinTimeoutMs);
    }
}
