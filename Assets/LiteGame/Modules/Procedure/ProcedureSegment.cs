using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Config;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;
using LiteGame.Modules.UI;
using LiteGame.Modules.UI.Pages;

using LiteSync.Client;
using LiteGame.Modules.Meta.Abstractions;
using LiteGame.Modules.Meta.Abstractions.Account;
namespace LiteGame
{
    /// <summary>
    /// ⑱ 应用壳：流程机（阶段表）＋ 页面流 ＋ 错误恢复 ＋ 账号重入 ＋ 产品桥装订口。
    /// </summary>
    public sealed class ProcedureSegment : IClientSegment, IClientSegmentGraph
    {
        private readonly Func<IRoomSessionBridge> _createBridges;
        private PageFlow _pageFlow;                          // 宿主退出时使在途恢复代次失效（不触发导航）

        /// <param name="createBridges">房间会话装订口工厂（Battle 阶段装订的会话由页面端口 <c>IRoomView</c> 同实例消费）。</param>
        public ProcedureSegment(Func<IRoomSessionBridge> createBridges)
            => _createBridges = createBridges ?? throw new ArgumentNullException(nameof(createBridges));

        public string Name => "Procedure";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public IReadOnlyList<string> DependsOn => new[]
        {
            "PlatformInfrastructure", "Camera", "DevSession", "Content", "Events",
            "Config", "Map", "UI", "Input", "Meta", "Battle", "Session",
        };

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            CancellationToken rootToken = context.RootScope.Token;
            UIService ui = context.Require<UIService>();
            UINavigationController nav = context.Require<UINavigationController>();
            IPageDirectory pages = context.Require<IPageDirectory>();          // 页面目录端口（UI 段产出）
            ISettlementSummary settlement = context.Require<ISettlementSummary>();   // 结算摘要端口（Battle 段产出）
            IMetaConfigFacts metaFacts = context.Require<IMetaConfigFacts>();
            IRoomTicketSource tickets = context.Require<IRoomTicketSource>();

            // 页面流（入口/开局关房/结算三处流程×页面接缝）——只吃导航/UI/结算三个端口，不认生成物与脚本表。
            var pageFlow = new PageFlow(nav, ui, settlement);
            _pageFlow = pageFlow;
            FeedbackService feedback = context.Require<FeedbackService>();

            // 房间会话装订口（实现住 App/PageServices 的 RoomBridge，本段只经接口）。
            IRoomSessionBridge roomSession = _createBridges();
            context.Put<IRoomSessionBridge>(roomSession);

            // 恢复执行器（Root：启动/内容/会话失败共用）；清缓存实现面归内容段。
            var recovery = new ErrorRecoveryService(
                new AppRecoveryActions(cache: context.Require<IContentCacheMaintenance>()));
            context.Put(recovery);

            // 阶段机与流程共用同一操作寿命；flow 是 lifetime 的唯一 Dispose owner。
            var lifetime = new RoomFlowLifetime(rootToken);
            // 恢复单写者仍是 ProcedureFlow（CleanupTask 观察者）；这里只把页面落点注入寿命，导航不写进 Lifetime。
            lifetime.RecoveryStep = operation => RecoverRoomAsync(operation, pageFlow, feedback, rootToken);
            ProcedureFlow flow = null;
            try
            {
                var machine = CreateMachine(context, pageFlow, metaFacts, recovery,
                    roomSession, pages, lifetime);
                context.Put(machine);
                // 联机入口只认流程端口（不回看阶段机/阶段参数类型）；票据源与操作寿命由本段装订。
                flow = new ProcedureFlow(machine, tickets, lifetime, rootToken);
                context.Scope.Register(flow);
            }
            catch
            {
                if (flow != null) flow.Dispose();
                else lifetime.Dispose();                     // 尚未交给 flow：由本段回收
                throw;
            }

            context.Put<IRoomFlow>(flow);
            context.Put<IAccountLoginControl>(flow);
            // 账号重入单点（登录失效 → 清登录态 → 等离场终态 → 关账户域 → 登录页）；Meta 门面是唯一调用方。
            context.Put<IAccountReentry>(new AccountReentry(flow, flow, context.Require<IAccountDomain>(), pageFlow));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _pageFlow?.InvalidateRecovery();                  // 宿主退出：旧房间清理完成不得再导航
            return UniTask.CompletedTask;
        }

        /// <summary>房间操作清理完成后的唯一页面恢复：失败先反馈再大厅；正常终态按 completed 进结算/大厅。</summary>
        private static async UniTask RecoverRoomAsync(RoomFlowOperation operation, PageFlow pages,
            FeedbackService feedback, CancellationToken rootToken)
        {
            if (rootToken.IsCancellationRequested || operation == null || operation.RecoverySuppressed) return;
            if (pages == null || pages.IsLoginOpen
                || !pages.IsRecoveryGenerationCurrent(operation.RecoveryGeneration)) return;

            if (operation.RecoveryFailed)
            {
                if (feedback != null)
                {
                    // 反馈等待期间账号重入会取消操作令牌：旧弹窗随之收口，代次/登录页守卫再兜底。
                    using CancellationTokenSource feedbackCts =
                        CancellationTokenSource.CreateLinkedTokenSource(rootToken, operation.Token);
                    try
                    {
                        await feedback.ShowErrorAsync(operation.RecoveryErrorTitle, operation.RecoveryErrorMessage,
                            retryable: false, mergeKey: "room.recovery", ct: feedbackCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Procedure] 房间失败反馈未完成（继续回大厅）:{ex.Message}", LogTag.Procedure);
                    }
                }
                else
                {
                    Log.Warning($"[Procedure] 房间失败反馈未接线（FeedbackService 缺失）："
                        + $"{operation.RecoveryErrorTitle}/{operation.RecoveryErrorMessage}", LogTag.Procedure);
                }
            }

            if (rootToken.IsCancellationRequested || operation.RecoverySuppressed || pages.IsLoginOpen) return;
            if (!pages.IsRecoveryGenerationCurrent(operation.RecoveryGeneration)) return;
            pages.OnMatchEnded(operation.RecoveryCompleted, operation.RecoveryResult,
                operation.RecoveryPlayerId, operation.RecoveryGeneration);
        }

        /// <summary>阶段表（注册序之外的第二道显式拼装面）：每阶段只吃端口/阶段实例。</summary>
        private StageMachine<ProcedureId, ProcedureArgs> CreateMachine(SegmentContext context,
            PageFlow pageFlow, IMetaConfigFacts metaFacts, ErrorRecoveryService recovery,
            IRoomSessionBridge roomSession, IPageDirectory pages, RoomFlowLifetime lifetime)
        {
            IContentService content = context.Require<IContentService>();
            IConfigService config = context.Require<IConfigService>();
            var rootToken = context.RootScope.Token;

            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(rootToken)),
                (ProcedureId.Patch, new ProcedurePatch(content, context.Require<ActivationTransactionStore>(),
                    context.Require<PatchRunner>(), rootToken, context.Require<AssetsHealthProbe>(), recovery)),
                (ProcedureId.Preload, new ProcedurePreload(config, context.Require<UIService>(),
                    rootToken, context.Require<FeedbackService>())),
                (ProcedureId.Main, new ProcedureMain(rootToken, pageFlow, metaFacts,
                    dev: context.Require<IDevSession>())),
                (ProcedureId.Match, new ProcedureMatch(context.Require<IMatchSessionFactory>(),
                    lifetime, context.Require<FeedbackService>(), rootToken)),
                (ProcedureId.Battle, new ProcedureBattle(context.Require<IInputService>(),
                    context.Require<ICameraService>(), context.Require<IBattleCommands>(),
                    context.Require<IBattleViewGroupFactory>(), roomSession,
                    lifetime, rootToken, requireCamera: true,
                    runs: context.Require<IBattleRunFactory>(),
                    pages: pageFlow,
                    scenes: context.Require<ISceneService>(),          // 开局入场加载 / 离场卸载对局场景组
                    maps: context.Require<IMapProvider>(),              // 多图批：场景路径查表行 scene 列 / 按表枚举
                    feedback: context.Require<FeedbackService>(),      // 开局入场加载遮罩
                    devSession: context.Require<IDevSession>())),      // dev 投影读本局规则（实例端口）
                (ProcedureId.Error, new ProcedureError(rootToken, recovery)));
        }
    }
}
