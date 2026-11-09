using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Config;
using LiteGame.Modules.Shared;

namespace LiteGame.Modules.Composition
{
    /// <summary>④ 内容服务：IContentService 注入装配（YooAsset 适配 + 共享加载协调器 + 租约）——
    /// 配置/程序集字节通道与租约消费者依赖它（注册序即依赖序：先于 Config/Presentation）；
    /// 资源包初始化仍由 ProcedurePreload 经本服务触发。</summary>
    public sealed class ContentSegment : IClientSegment, IClientSegmentGraph
    {
        private readonly DeployBake _deployBake;   // 包内烙入（命令行 > 烙入 > 默认；实例只读一次）
        private readonly Func<IFileStore, IContentService> _contentFactory;
        private readonly Func<IContentService, ISceneService> _sceneFactory;
        private readonly Func<string, IFileStore, IHealthProbe> _configHealthProbeFactory;
        private readonly Func<string, IFileStore, IHealthProbe> _codeHealthProbeFactory;
        private IContentService _content;
        private ISceneService _scenes;      // 场景句柄服务（流程/工具体消费；租约面归内容域）

        /// <param name="contentFactory">内容服务实现工厂（YooAsset 适配器在组合根构造——本段只认端口）。</param>
        /// <param name="sceneFactory">场景服务实现工厂（同上；包句柄由内容服务持有）。</param>
        public ContentSegment(DeployBake deployBake,
            Func<IFileStore, IContentService> contentFactory,
            Func<IContentService, ISceneService> sceneFactory,
            Func<string, IFileStore, IHealthProbe> configHealthProbeFactory,
            Func<string, IFileStore, IHealthProbe> codeHealthProbeFactory = null)
        {
            _deployBake = deployBake ?? throw new ArgumentNullException(nameof(deployBake));
            _contentFactory = contentFactory ?? throw new ArgumentNullException(nameof(contentFactory));
            _sceneFactory = sceneFactory ?? throw new ArgumentNullException(nameof(sceneFactory));
            _configHealthProbeFactory = configHealthProbeFactory ?? throw new ArgumentNullException(nameof(configHealthProbeFactory));
            _codeHealthProbeFactory = codeHealthProbeFactory;
        }

        public string Name => "Content";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "PlatformInfrastructure", "Identity" };
        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            IFileStore files = context.Require<IFileStore>();
            _content = _contentFactory(files);
            context.Put<IContentService>(_content);
            if (_content is not IGenerationSink generationSink)
                throw new InvalidOperationException("内容服务实现必须同时实现 IGenerationSink（代次推进端口）");
            context.Put<IGenerationSink>(generationSink);                                // 代次推进（§9：确认后切换，失败回退）
            var activation = new ActivationTransactionStore(new FileActivationRecordIO(files: files));
            context.Put(activation);   // 启动恢复决策（Patch 流程消费）
            RegisterSceneAndCacheServices(context, files);

            // 热更链生产装配。
            ContentCdnConfig deploy = ContentDeployConfig.Load(
                Environment.GetCommandLineArgs(), _deployBake.ReadArgs());
            string candidateRoot = ReleaseLayout.CandidateRootRelative;
            var json = context.Require<IJsonCodec>();
            ICandidateFetcher fetcher;
            ICandidateProvider provider;
            Func<ReleaseManifest, DownloadPlan> planFactory;
            if (deploy.HasCdn)
            {
                var source = new DownloadSource("cdn", deploy.BaseUrl);
                fetcher = new HttpCandidateFetcher(candidateRoot, deploy.TimeoutSeconds, files);
                provider = new HttpCandidateProvider(deploy.BaseUrl, deploy.OfferPath, json, deploy.TimeoutSeconds);
                planFactory = manifest => new DownloadPlan(manifest, new[] { source });
                Log.Info($"内容通道：CDN {deploy.BaseUrl}（信封 {deploy.OfferPath}）", LogTag.Content);
            }
            else
            {
                fetcher = new LocalDirectoryCandidateFetcher(candidateRoot, files);
                provider = new FileSystemCandidateProvider(json, files: files);
                planFactory = null;                       // PatchRunner 默认计划（本地通道只清点落盘、不消费来源）
                Log.Info("内容通道：本地信封（未配置 CDN）", LogTag.Content);
            }

            var trustedKeys = new TrustedKeyStore();
            ContentTrustAnchors.ApplyTo(trustedKeys);                                // 锚点已 provisioning
            var candidateFiles = new FileSystemCandidateFileSource(candidateRoot, files);
            var coordinator = new PatchCoordinator(
                context.Require<ActivationTransactionStore>(),
                candidateFiles,
                // 空间预检探**内容将写入的卷**——候选根即写入目标（FileSystem 相对路径，
                // 卷归属与 RootPath 同卷：RootPath = Application.persistentDataPath）。
                new WriteProbeDiskSpaceProbe(candidateRoot, files: files),
                fetcher,
                new CompositeHealthCheck(
                    new CandidateConfigHealthProbe(candidateRoot, ReleaseLayout.ConfigPaths, json, files),
                    _configHealthProbeFactory(candidateRoot, files),
                    _codeHealthProbeFactory?.Invoke(candidateRoot, files)),
                new ContentActivator(_content),
                context.Require<IGenerationSink>());   // 确认/回退时推进加载代次（同一内容服务实例）
            context.Put(new PatchRunner(
                provider,
                context.Require<IdentityContext>().CreatePlayerCapabilities(),
                context.Require<ActivationTransactionStore>(),
                coordinator,
                trustedKeys.AsResolver(),                                                // keyId → 验签器；未登记/已撤销 → null → 拒（§6）
                budget: null,
                planFactory: planFactory));
            context.Put(coordinator);

            // 入口资产探针（初始化后执行；时序说明见上）——真实实体入口，走与运行时同一条租约通道。
            context.Put(new AssetsHealthProbe(_content, new[] { BattleViewPaths.DefaultEntityPrefab }));
            return UniTask.CompletedTask;
        }

        /// <summary>场景与缓存租约都走内容包：场景服务、清缓存实现面归本段（进程级实例，关闭时归零）。</summary>
        private void RegisterSceneAndCacheServices(SegmentContext context, IFileStore files)
        {
            _scenes = _sceneFactory(_content);
            context.Put<ISceneService>(_scenes);
            context.Put<IContentCacheMaintenance>(
                new ContentCacheMaintenance(_content, files, context.Require<ActivationTransactionStore>()));
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _scenes?.ReleaseAll();                                     // 场景句柄归零（先收场景再收内容）
            _scenes = null;
            return _content?.ShutdownAsync(ct) ?? UniTask.CompletedTask;   // 释放面：剩余租约/句柄归零
        }
    }
}
