using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using UnityEngine;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Config;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;
using LiteGame.Modules.Settings;
using LiteGame.Modules.UI;
using LiteGame.Modules.UI.Pages;
using LiteSim;

using LiteSync.Client;
using LiteGame.Modules.Meta.Abstractions;
using LiteGame.Modules.Meta.Abstractions.Account;
using LiteGame.Modules.Meta.Client.Account;
using LiteGame.Modules.Meta.Client.Session;
namespace LiteGame
{
    /// <summary>
    /// **应用组合根**（两副职责，都只在这一个文件里）： ① 适配器的**唯一创建点**——new 只出现在本文件（相机/输入/配置服务的工厂委托，GameEntry 注入给各段）。
    /// </summary>
    internal sealed class AppComposition
    {
        private IDisposable _devDecoration;      // 开发装配装饰句柄（GM/弱网/调试内核；null = 发布形态）
        private RoomBridge _roomSession;          // 房间会话门面（流程端口与页面读口 IRoomView 同实例）
        private PageServices _pageServices;      // 页面装配面（UI 段经工厂延迟取件）
        private ISettingsPagePort _settingsPort; // 设置页端口（ProductSettings 同实例）

        /// <summary>判定参数覆写实例（判定几何 13 列的调参面）：装配期创建一次，调参面板与编辑器导出器
        /// 共用同一实例（容器登记；编辑器 GM 经 <c>GMEditorResolve</c> 取件）。
        /// **这里只登记不判定**——"本局是否合并"由进房单点每局求值一次（<c>SimParamsActivation</c>：
        /// 装配期测试模式尚未开启，在这里求门恒假）。</summary>
        private readonly SimParamsOverrides _simParamsOverrides = new SimParamsOverrides();

        // ---- ① 适配器工厂（new 唯一出现处；GameEntry 注入给各段）----。

        /// <summary>配置服务（表投影）：字节通道经内容租约（代次/引用统一，提取即释放源资产）。
        /// 装载期不接覆写合并口（传 null）：覆写合并已下移到进房时点，
        /// 客户端发布面与本地服房间数值必须由同一求值结果派生（见 <see cref="Modules.Config.SimParamsActivation"/>）。</summary>
        internal Func<IContentService, IFileStore, ActivationTransactionStore, IConfigService> ConfigServiceFactory()
            => (content, files, activation) => new ConfigService(
                (location, token) => LoadBytesViaContentLease(content, location, token),
                candidateConfig: new CandidateConfigSource(files, () => activation.ActiveGeneration));

        /// <summary>意图设备源（New Input System 适配器）：本局规则经读口注入（进房时才有值）。</summary>
        internal Func<IDevSession, IIntentSource> IntentSourceFactory()
            => dev => new NewInputIntentSource(matchRules: () => dev.MatchRules);

        /// <summary>相机服务（Cinemachine 适配器）：vcam 是场景对象，服务按需解析。</summary>
        internal Func<ICameraService> CameraServiceFactory()
            => () => new CinemachineCameraService();

        /// <summary>内容服务（YooAsset 适配器）：创建点只在本文件，ContentSegment 只认 IContentService。</summary>
        internal Func<IFileStore, IContentService> ContentServiceFactory()
            => files => new YooAssetContentService(files);

        /// <summary>场景服务（YooAsset 场景句柄适配器）：只经 ISceneService 被流程消费；包句柄由内容服务持有
        /// （组合根是唯一把端口还原为具体实现的地方）。</summary>
        internal Func<IContentService, ISceneService> SceneServiceFactory()
            => content => new SceneService(content as YooAssetContentService
                ?? throw new ArgumentException("SceneService 需要 YooAsset 内容服务", nameof(content)));

        /// <summary>热更程序集健康探针（code/ 段）：只经 IHealthProbe 注入内容编排。</summary>
        internal Func<string, IFileStore, IHealthProbe> CodeHealthProbeFactory()
            => (candidateRoot, files) => new CodeAssembliesHealthProbe(candidateRoot, ReleaseLayout.CodeAssemblies, files);

        /// <summary>候选配置语义探针（H4-2）：全量 config/ → cfg.Tables → ConfigTableValidator；
        /// 只经 IHealthProbe 注入内容编排，Luban 细节不穿透到 Composition。</summary>
        internal Func<string, IFileStore, IHealthProbe> ConfigHealthProbeFactory()
            => (candidateRoot, files) => new ConfigCandidateHealthProbe(files, candidateRoot);

        /// <summary>房间会话装订口工厂（流程段端口 <c>IRoomSessionBridge</c>）：适配器类型只出现在这里，
        /// 与页面端口 <c>IRoomView</c> 共用同一实例（同源同帧）。</summary>
        internal Func<IRoomSessionBridge> RoomBridgeFactory()
            => () =>
            {
                var room = new RoomBridge();
                _roomSession = room;
                return room;
            };

        // ---- ② 组合根（ClientHost.ComposeRoot 驱动）----。

        /// <summary>跨域装订：模块全部成功后执行一次；登记资源进本窗口作用域（关闭时先于模块释放）。
        /// 末步是装配面密封——此后运行期不再接受新增登记。</summary>
        internal async UniTask ComposeAfter(SegmentContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Scope.Register(new DelegatedDisposable(DisposeComposition));

            BindProductSettings(context);
            BindPageServices(context);
            BindIdentity(context);
            BindModals(context);
            BindInputGate(context);
            BindSimParams(context);
            BindDevDecoration(context);
            FrameSchedule frames = context.Require<FrameSchedule>();
            DeclareFrameOrder(frames);
            frames.Validate();                         // 密封前校验帧计划覆盖（失败保留未密封，可诊断）
            context.Services.Seal();                   // 装配面密封（最后一步；职责与旧 Container 末段一致）

            await UniTask.CompletedTask;               // 无异步步骤：签名保留"装配窗口内执行"的语义
        }

        /// <summary>页面装配面：域服务一律取域实现（组合根只负责装配，不做页面级映射）。</summary>
        private void BindPageServices(SegmentContext context)
        {
            IConfigService config = context.Require<IConfigService>();
            var meta = new MetaSession(context.Require<IAccountDomain>(), context.Require<MetaLoginService>(),
                context.Require<IRoomFlow>(), () => config.Tables);
            _pageServices = new PageServices(
                context.Require<UINavigationController>(), context.Require<UIService>(),
                context.Require<IConfirmDialog>(), meta, context.Require<IMapProvider>(),
                _roomSession, _settingsPort);
        }

        /// <summary>页面装配面工厂（UI 段构造注入；延迟取件——首次页面打开时组合根已装订完毕）。</summary>
        internal Func<object> PageServicesFactory() => () => _pageServices;

        /// <summary>设置门面装订：构造即全量应用持久化值（启动生效点），容器取件面；同一实例即页面端口
        /// （<see cref="ISettingsPagePort"/>）。视角偏好用设置段的**同一实例**（<c>LookPreferences</c>）。</summary>
        private void BindProductSettings(SegmentContext context)
        {
            var settings = new ProductSettings(
                context.Require<GameSettings>(), context.Require<SettingService>(),
                context.Require<AudioService>(), context.Require<LookPreferences>());
            context.Put(settings);
            _settingsPort = settings;                                // 同一实例：设置页端口即设置门面
        }

        /// <summary>内容身份绑定（只此一次）：内容/配置装配完成后，身份上下文用同一份 Activation/Config 事实回答。
        /// 根位移摘要经战斗侧端口 <see cref="IRootMotionContentDigest"/> 取（首个对局装载前为空字符串——
        /// 此时没有对局在跑，不参与准入；身份面不引战斗域类型）。</summary>
        private static void BindIdentity(SegmentContext context)
        {
            IdentityContext identity = context.Require<IdentityContext>();
            ActivationTransactionStore activation = context.Require<ActivationTransactionStore>();
            IConfigService config = context.Require<IConfigService>();
            IRootMotionContentDigest rootMotionDigest = context.Require<IRootMotionContentDigest>();
            identity.BindContent(() => new ContentIdentity(
                activation.ActiveGeneration.ReleaseId,
                activation.ActiveGeneration.Value,
                activation.Current.ConfirmedRevision,
                config.GameplayDigestBase,
                rootMotionDigest.Digest));
        }

        /// <summary>模态登记（生产装配——与测试同口径）：错误/确认两个弹窗。设置页（表单 15）不在此登记：
        /// 它须经生成页面目录反查（单源），而本装订点早于 ProcedurePreload 的表装载——登记随放行闸口就位。</summary>
        private static void BindModals(SegmentContext context)
        {
            UIService ui = context.Require<UIService>();
            FeedbackService feedback = context.Require<FeedbackService>();
            ui.RegisterModal(feedback.ErrorFormId);
            ui.RegisterModal(feedback.ConfirmFormId);
        }

        /// <summary>输入拦截源（ui.modal）：模态 UI 打开时拦游戏意图。拦截键登记一次、跨对局常驻。</summary>
        private static void BindInputGate(SegmentContext context)
        {
            UIService ui = context.Require<UIService>();
            context.Require<IInputService>().RegisterBlocker(
                new IntentGate.BlockerKey("ui.modal", "模态 UI 打开——游戏意图被拦截"), () => ui.IsModalOpen);
        }

        /// <summary>判定参数覆写面装订：实例登记进容器（调参面板/编辑器导出器共用），**此处不求门**——
        /// 装配完成时本地测试模式还没开启（门恒假），求值点是进房单点（每局创建传输前一次，
        /// 见 <c>ProductTransportFactory</c>）；运行期取件一律经容器。</summary>
        private void BindSimParams(SegmentContext context) => context.Put(_simParamsOverrides);

        /// <summary>开发装配（GM 命令/弱网/调试内核）整段归 LiteGame.Dev，经钩子装饰；发布形态无 Dev 程序集 = 不装饰。
        /// 句柄登记进组合根作用域——关闭时先于模块释放（命令注销 + Dev 静态读口收口）。</summary>
        private void BindDevDecoration(SegmentContext context)
        {
            _devDecoration = DevHooks.Current?.Decorate(new DevServices
            {
                Context = context,
                Commands = context.Require<CommandBus>(),
                Services = context.Services,
                Session = context.Require<IDevSession>(),
                Scenes = context.Require<ISceneService>(),
                Input = context.Require<IInputService>(),
            });
        }

        /// <summary>帧驱动序单点声明（12 项）：段只 Put 实例、不各自声明相对序——分段落声明会按段注册序
        /// 重排全局相对序（行为变更）。EventBus 置 CommandBus 前：到期事件先于排队命令/流程机派发。</summary>
        private static void DeclareFrameOrder(FrameSchedule frames)
        {
            frames.SetOrder(
                typeof(IMainThreadDispatcher), typeof(IWorldClock), typeof(IUIClock),
                typeof(DotweenUIClockDriver), typeof(UIService),
                typeof(ITimelineRunner),
                typeof(IVfxService), typeof(DialogService), typeof(ToastTicker),
                typeof(EventBus), typeof(ICommandBus),
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
