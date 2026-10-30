using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.UI;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// **应用组合根**（两副职责，都只在这一个文件里）：
    /// ① 适配器的**唯一创建点**——new 只出现在本文件（相机/输入/配置服务的工厂委托，GameEntry 注入给各段）；
    /// ② <see cref="ComposeAfter"/>——**全部模块成功后、装配面密封前**的跨域装订：Lua 门面/模态/身份/输入拦截/
    /// 注册表回填/开发装饰/帧驱动序/密封。
    ///
    /// 为什么必须有它：适配器程序集反向引用域程序集（Adapters.Unity → Modules.Input/Modules.Camera、
    /// Adapters.Luban → Modules.Config），段一旦自己 new 就成环；跨域连线放进任何一段都会让"段式装配"
    /// 退化成大段。**本类是装配点不是业务段**——段内仍不许跨域连线。
    ///
    /// 方法体预算：类 ≤400 行、方法 ≤80 行（CompositionBudgetTests）——装订按域拆成小方法。
    /// </summary>
    internal sealed class AppComposition
    {
        private IDisposable _devDecoration;      // 开发装配装饰句柄（GM/弱网/调试内核；null = 发布形态）

        // ---- ① 适配器工厂（new 唯一出现处；GameEntry 注入给各段）----

        /// <summary>配置服务（表投影）：字节通道经内容租约（代次/引用统一，提取即释放源资产）。</summary>
        internal Func<IContentService, IConfigService> ConfigServiceFactory()
            => content => new ConfigService((location, token) => LoadBytesViaContentLease(content, location, token));

        /// <summary>意图设备源（New Input System 适配器）：本局规则经读口注入（进房时才有值）。</summary>
        internal Func<IDevSession, IIntentSource> IntentSourceFactory()
            => dev => new NewInputIntentSource(matchRules: () => dev.MatchRules);

        /// <summary>相机服务（Cinemachine 适配器）：vcam 是场景对象，服务按需解析。</summary>
        internal Func<ICameraService> CameraServiceFactory()
            => () => new CinemachineCameraService();

        // ---- ② 组合根（ClientHost.ComposeRoot 驱动）----

        /// <summary>跨域装订：模块全部成功后执行一次；登记资源进本窗口作用域（关闭时先于模块释放）。
        /// 末步是装配面密封——此后运行期不再接受新增登记。</summary>
        internal async UniTask ComposeAfter(ModuleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Scope.Register(new DelegatedDisposable(DisposeComposition));

            BindLuaFacades(context);
            BindIdentity(context);
            BindModals(context);
            BindInputGate(context);
            BindRegistryRefill(context);
            BindMetaEntry(context);
            BindDevDecoration(context);
            DeclareTickOrder(context.Services);
            context.Services.Seal();                   // 装配面密封（最后一步；职责与旧 Container 末段一致）

            await UniTask.CompletedTask;               // 无异步步骤：签名保留"装配窗口内执行"的语义
        }

        /// <summary>Lua 门面后端（Bridge.*）：表源/注册表/UI 服务/导航——页面与脚本消费的唯一绑定面。</summary>
        private static void BindLuaFacades(ModuleContext context)
        {
            IConfigService config = context.Require<IConfigService>();
            Bridge.Bind(() => config.Tables);
            Bridge.BindRegistries(context.Require<UiLuaRegistry>(), context.Require<ContentLuaRegistry>());
            Bridge.BindUIService(context.Require<UIService>());
            Bridge.BindNavigation(context.Require<UINavigationController>());
        }

        /// <summary>内容身份绑定（只此一次）：内容/配置装配完成后，身份上下文用同一份 Activation/Config 事实回答。</summary>
        private static void BindIdentity(ModuleContext context)
        {
            IdentityContext identity = context.Require<IdentityContext>();
            ActivationTransactionStore activation = context.Require<ActivationTransactionStore>();
            IConfigService config = context.Require<IConfigService>();
            identity.BindContent(() => new ContentIdentity(
                activation.ActiveGeneration.ReleaseId,
                activation.ActiveGeneration.Value,
                activation.Current.ConfirmedRevision,
                config.GameplayDigestBase,
                LiteSim.RootMotionCatalog.ContentDigest));
        }

        /// <summary>模态登记（生产装配——与测试同口径）：错误/确认两个弹窗。设置页（表单 15）不在此登记：
        /// 它须经生成页面目录反查（单源），而本装订点早于 ProcedurePreload 的表装载——登记随放行闸口就位。</summary>
        private static void BindModals(ModuleContext context)
        {
            UIService ui = context.Require<UIService>();
            FeedbackService feedback = context.Require<FeedbackService>();
            ui.RegisterModal(feedback.ErrorFormId);
            ui.RegisterModal(feedback.ConfirmFormId);
        }

        /// <summary>输入拦截源（ui.modal）：模态 UI 打开时拦游戏意图。拦截键登记一次、跨对局常驻。</summary>
        private static void BindInputGate(ModuleContext context)
        {
            UIService ui = context.Require<UIService>();
            context.Require<IInputService>().RegisterBlocker(
                new IntentGate.BlockerKey("ui.modal", "模态 UI 打开——游戏意图被拦截"), () => ui.IsModalOpen);
        }

        /// <summary>注册表回填服务（env 重建后按表补注册）：页面/内容/策略三张表 + Lua 宿主。</summary>
        private static void BindRegistryRefill(ModuleContext context)
        {
            context.Services.RegisterInstance(new LuaRegistryRefillService(
                context.Require<LuaComponent>(), context.Require<UIService>(), context.Require<IConfigService>(),
                UiPageCatalog.All, context.Require<UiLuaRegistry>(), context.Require<ContentLuaRegistry>(),
                context.Require<StrategyLuaRegistry>()));
        }

        /// <summary>联机入口接流程机：进房 = Request(Match, MetaJoin)——走既有 Join 管道（不另开路径）。</summary>
        private static void BindMetaEntry(ModuleContext context)
        {
            context.Require<MetaEntryService>().BindMachine(
                context.Require<StageMachine<ProcedureId, ProcedureArgs>>());
        }

        /// <summary>开发装配（GM 命令/弱网/调试内核）整段归 LiteGame.Dev，经钩子装饰；发布形态无 Dev 程序集 = 不装饰。
        /// 句柄登记进组合根作用域——关闭时先于模块释放（命令注销 + Dev 静态读口收口）。</summary>
        private void BindDevDecoration(ModuleContext context)
        {
            _devDecoration = DevHooks.Current?.Decorate(new DevServices
            {
                Context = context,
                Commands = context.Require<CommandCenter>(),
                Services = context.Services,
                Session = context.Require<IDevSession>(),
                Scenes = context.Require<ISceneService>(),
                Input = context.Require<IInputService>(),
            });
        }

        /// <summary>帧驱动序单点声明（13 项）：段只 Put 实例、不各自声明相对序——分段落声明会按段注册序
        /// 重排全局相对序（行为变更）。CommandCenter 置流程机前：排队/延迟命令到期派发先于机器 Tick。</summary>
        private static void DeclareTickOrder(ServiceContainer services)
        {
            services.SetTickOrder(
                typeof(IMainThreadDispatcher), typeof(IWorldClock), typeof(IUIClock),
                typeof(DotweenUiClockDriver), typeof(UIService),   // EventBus 无队列、不再实现 ITickable（M3-7 7b）
                typeof(ILogicScheduler), typeof(IUIScheduler), typeof(ITimelineRunner),
                typeof(IVFXService), typeof(DialogService), typeof(ToastTicker),
                typeof(ICommandCenter),
                typeof(StageMachine<ProcedureId, ProcedureArgs>));
        }

        /// <summary>组合根释放（组合根作用域关闭时调用）：开发装配句柄收口（注销 + 静态读口），幂等。</summary>
        private void DisposeComposition()
        {
            _devDecoration?.Dispose();
            _devDecoration = null;
        }

        /// <summary>经内容租约读表字节（TextAsset 提取 bytes 后即释放——复制数据不留源引用）。</summary>
        private static async UniTask<byte[]> LoadBytesViaContentLease(IContentService content,
            string location, System.Threading.CancellationToken ct)
        {
            var lease = await content.AcquireAsync<TextAsset>(location, ct: ct);
            try { return lease.Asset.bytes; }
            finally { lease.Dispose(); }
        }
    }
}
