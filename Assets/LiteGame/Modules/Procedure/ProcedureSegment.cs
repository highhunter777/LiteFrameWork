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

using LiteSync.Client;
namespace LiteGame
{
    /// <summary>
    /// ⑱ 应用壳：流程机（阶段表）＋ 页面流 ＋ 错误恢复 ＋ 账号重入 ＋ 产品桥装订口。
    /// </summary>
    public sealed class ProcedureSegment : IClientSegment, IClientSegmentGraph
    {
        private readonly Func<(IRoomSessionBridge room, ILuaProductBridge product)> _createBridges;
        private readonly Func<ILuaHost, IPageDirectory, ILuaRegistryFiller> _createRegistryFiller;

        /// <param name="createBridges">Lua 桥装订口工厂（房间会话门面与产品桥**共用同一实例**：Battle 阶段装订的
        /// 会话须被 env 重建后的门面读到）。</param>
        /// <param name="createRegistryFiller">注册表填充器工厂（读页面目录 → 报告；实现归 Lua 桥适配器——
        /// 流程段只认 <see cref="ILuaRegistryFiller"/> 端口，不认注册表与页面的具体类型）。</param>
        public ProcedureSegment(Func<(IRoomSessionBridge room, ILuaProductBridge product)> createBridges,
            Func<ILuaHost, IPageDirectory, ILuaRegistryFiller> createRegistryFiller)
        {
            _createBridges = createBridges ?? throw new ArgumentNullException(nameof(createBridges));
            _createRegistryFiller = createRegistryFiller ?? throw new ArgumentNullException(nameof(createRegistryFiller));
        }

        public string Name => "Procedure";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public IReadOnlyList<string> DependsOn => new[]
        {
            "PlatformInfrastructure", "Camera", "DevSession", "Content", "Events", "LuaHost",
            "Config", "Map", "UI", "Input", "Meta", "Battle", "Session",
        };

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            IConfigService config = context.Require<IConfigService>();
            IContentService content = context.Require<IContentService>();
            ILuaHost lua = context.Require<ILuaHost>();
            UIService ui = context.Require<UIService>();
            UINavigationController nav = context.Require<UINavigationController>();
            IAccountSessionControl accountControl = context.Require<IAccountSessionControl>();
            IPageDirectory pages = context.Require<IPageDirectory>();          // 页面目录端口（UI 段产出）
            IPageDataFactory pageData = context.Require<IPageDataFactory>();   // 数据面端口（UI 段产出）
            ISettlementSummary settlement = context.Require<ISettlementSummary>();   // 结算摘要端口（Battle 段产出）

            // 页面流（入口/开局关房/结算三处流程×页面接缝）——只吃目录/数据两个端口，不认生成物与 Lua 表。
            var pageFlow = new PageFlow(nav, ui, pages, pageData, settlement);
            // 账号重入单点（登录失效 → 清登录态 → 在途对局回 Main → 登录页）；Bridge.meta 是唯一调用方。
            context.Put<IAccountReentry>(new AccountReentry(accountControl, pageFlow));

            // Lua 桥装订口：房间会话门面与产品桥共用同一实例（实现住 Adapters.LuaBridge，本段只经接口）。
            var (roomBridge, productBridge) = _createBridges();
            context.Put<IRoomSessionBridge>(roomBridge);
            context.Put<ILuaProductBridge>(productBridge);

            // 恢复执行器（Root：启动/内容/会话失败共用）；清缓存实现面归内容段。
            var recovery = new ErrorRecoveryService(
                new AppRecoveryActions(cache: context.Require<IContentCacheMaintenance>()));
            context.Put(recovery);

            var machine = CreateMachine(context, pageFlow, context.Require<IMetaConfigFacts>(), recovery,
                productBridge, roomBridge, pages);
            context.Put(machine);
            // 联机入口只认流程端口（不回看阶段机/阶段参数类型）。
            context.Put<IAppFlow>(new ProcedureFlow(machine));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>阶段表（注册序之外的第二道显式拼装面）：每阶段只吃端口/阶段实例。</summary>
        private StageMachine<ProcedureId, ProcedureArgs> CreateMachine(SegmentContext context,
            PageFlow pageFlow, IMetaConfigFacts metaFacts, ErrorRecoveryService recovery,
            ILuaProductBridge productBridge, IRoomSessionBridge roomBridge, IPageDirectory pages)
        {
            IContentService content = context.Require<IContentService>();
            IConfigService config = context.Require<IConfigService>();
            ILuaHost lua = context.Require<ILuaHost>();
            // 注册表填充器经端口注入：注册表与页面目录的具体形态归适配器/UI 域。
            ILuaRegistryFiller filler = _createRegistryFiller(lua, pages);
            var rootToken = context.RootScope.Token;

            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(rootToken)),
                (ProcedureId.Patch, new ProcedurePatch(content, context.Require<ActivationTransactionStore>(),
                    context.Require<PatchRunner>(), rootToken, context.Require<AssetsHealthProbe>(), recovery)),
                (ProcedureId.Preload, new ProcedurePreload(content, config, lua, filler, pages,
                    productBridge, context.Require<EventBus>(), context.Require<UIService>(),
                    () => ListLuaAssetPaths(content), rootToken, context.Require<FeedbackService>(),
                    context.Require<ICandidateLuaSource>())),
                (ProcedureId.Main, new ProcedureMain(rootToken, pageFlow, metaFacts,
                    dev: context.Require<IDevSession>())),
                (ProcedureId.Match, new ProcedureMatch(context.Require<IAccountSessionFactory>(),
                    context.Require<FeedbackService>(), rootToken)),
                (ProcedureId.Battle, new ProcedureBattle(context.Require<IInputService>(),
                    context.Require<ICameraService>(), context.Require<IBattleCommands>(),
                    context.Require<IBattleViewGroupFactory>(), roomBridge,
                    rootToken, requireCamera: true,
                    runs: context.Require<IBattleRunFactory>(),
                    pages: pageFlow,
                    scenes: context.Require<ISceneService>(),          // 开局入场加载 / 离场卸载对局场景组
                    maps: context.Require<IMapProvider>(),              // 多图批：场景路径查表行 scene 列 / 按表枚举
                    feedback: context.Require<FeedbackService>(),      // 开局入场加载遮罩
                    devSession: context.Require<IDevSession>())),      // dev 投影读本局规则（实例端口）
                (ProcedureId.Error, new ProcedureError(rootToken, recovery)));
        }

        /// <summary>Lua 资源清单由内容适配器按发布约定查询，装配点绑定预载委托。</summary>
        private static string[] ListLuaAssetPaths(IContentService content)
        {
            IReadOnlyList<string> paths = content.ListAssetPathsByTag("lua");
            if (paths == null || paths.Count == 0) return Array.Empty<string>();
            var result = new string[paths.Count];
            for (int i = 0; i < paths.Count; i++) result[i] = paths[i];
            return result;
        }
    }
}
