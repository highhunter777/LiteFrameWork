using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.View;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Config;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;
using LiteGame.Modules.Ui;

namespace LiteGame
{
    /// <summary>⑱ 应用壳：流程机（阶段表）＋ 页面流 ＋ 错误恢复 ＋ 账号重入 ＋ 产品桥装订口。
    /// **只编排**：各阶段的业务实现住各自域（战斗/会话/联机入口），本段不认业务内部。
    ///
    /// 为什么住 App：流程装配依赖 Lua 桥的注册表填充件（<c>RegistryFiller</c>/<c>UiLuaRegistry</c> 等，
    /// 属 <c>Adapters.LuaBridge</c>）——业务模块程序集不得引适配器；待注册表件归位后再迁 <c>Modules/Procedure/</c>。</summary>
    public sealed class ProcedureComposition : IClientModule, IClientModuleGraph
    {
        private readonly Func<(IRoomSessionBridge room, ILuaProductBridge product)> _createBridges;

        /// <param name="createBridges">Lua 桥装订口工厂（房间会话门面与产品桥**共用同一实例**：Battle 阶段装订的
        /// 会话须被 env 重建后的门面读到）。</param>
        public ProcedureComposition(Func<(IRoomSessionBridge room, ILuaProductBridge product)> createBridges)
            => _createBridges = createBridges ?? throw new ArgumentNullException(nameof(createBridges));

        public string Name => "Procedure";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[]
        {
            "PlatformInfrastructure", "Camera", "Content", "Config", "LuaHost", "UiService",
            "Input", "Meta", "Battle", "Session", "Map",
        };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            IConfigService config = context.Require<IConfigService>();
            IContentService content = context.Require<IContentService>();
            LuaComponent lua = context.Require<LuaComponent>();
            UIService ui = context.Require<UIService>();
            UINavigationController nav = context.Require<UINavigationController>();
            IAccountSessionControl accountControl = context.Require<IAccountSessionControl>();

            // 页面流（入口/开局关房/结算三处流程×页面接缝）
            var pageFlow = new PageFlow(nav, ui, lua);
            // 账号重入单点（登录失效 → 清登录态 → 在途对局回 Main → 登录页）；Bridge.meta 是唯一调用方
            context.Put<IAccountReentry>(new AccountReentry(accountControl, pageFlow));

            // Lua 桥装订口：房间会话门面与产品桥共用同一实例（实现住 Adapters.LuaBridge，本段只经接口）
            var (roomBridge, productBridge) = _createBridges();
            context.Put<IRoomSessionBridge>(roomBridge);
            context.Put<ILuaProductBridge>(productBridge);

            // 恢复执行器（Root：启动/内容/会话失败共用）；清缓存实现面归内容段
            var recovery = new ErrorRecoveryService(
                new AppRecoveryActions(cache: context.Require<IContentCacheMaintenance>()));
            context.Put(recovery);

            var machine = CreateMachine(context, pageFlow, context.Require<IMetaConfigFacts>(), recovery, productBridge, roomBridge);
            context.Put(machine);
            // 联机入口只认流程端口（不回看阶段机/阶段参数类型）
            context.Put<IAppFlow>(new ProcedureFlow(machine));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>阶段表（注册序之外的第二道显式拼装面）：每阶段只吃端口/阶段实例。</summary>
        private static StageMachine<ProcedureId, ProcedureArgs> CreateMachine(ModuleContext context,
            PageFlow pageFlow, IMetaConfigFacts metaFacts, ErrorRecoveryService recovery,
            ILuaProductBridge productBridge, IRoomSessionBridge roomBridge)
        {
            IContentService content = context.Require<IContentService>();
            IConfigService config = context.Require<IConfigService>();
            var filler = new RegistryFiller(config, UiPageCatalog.All, context.Require<LuaComponent>(),
                context.Require<UiLuaRegistry>(), context.Require<ContentLuaRegistry>(),
                context.Require<StrategyLuaRegistry>());
            var rootToken = context.RootScope.Token;

            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(rootToken)),
                (ProcedureId.Patch, new ProcedurePatch(content, context.Require<ActivationTransactionStore>(),
                    context.Require<PatchRunner>(), rootToken, context.Require<AssetsHealthProbe>(), recovery)),
                (ProcedureId.Preload, new ProcedurePreload(content, config, context.Require<LuaComponent>(), filler,
                    productBridge, context.Require<EventBus>(), context.Require<UIService>(),
                    () => ListLuaAssetPaths(content), rootToken, context.Require<FeedbackService>())),
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
