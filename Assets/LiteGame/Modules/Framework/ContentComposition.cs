using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>④ 内容服务：IContentService 注入装配（YooAsset 适配 + 共享加载协调器 + 租约）——
    /// 配置/Lua 字节通道与租约消费者依赖它（注册序即依赖序：先于 Config/Presentation）；
    /// 资源包初始化仍由 ProcedurePreload 经本服务触发。</summary>
    public sealed class ContentModule : IClientModule, IClientModuleGraph
    {
        private readonly DeployBake _deployBake;   // 包内烙入（命令行 > 烙入 > 默认；实例只读一次）
        private YooAssetContentService _content;
        private SceneService _scenes;      // 场景句柄服务（流程/工具体消费；租约面归内容域）

        public ContentModule(DeployBake deployBake)
            => _deployBake = deployBake ?? throw new ArgumentNullException(nameof(deployBake));

        public string Name => "Content";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "PlatformInfrastructure", "Identity" };
        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            IFileStore files = context.Require<IFileStore>();
            _content = new YooAssetContentService(files);
            context.Put<IContentService>(_content);
            context.Put<IGenerationSink>(_content);                                      // 代次推进（§9：确认后切换，失败回退）
            context.Put(new ActivationTransactionStore(new FileActivationRecordIO(files: files)));   // 启动恢复决策（Patch 流程消费）
            RegisterSceneAndCacheServices(context, files);

            // 热更链生产装配。装配形态决定能力边界：
            // - **信任锚**：内置锚表（ContentTrustAnchors——根信任编入应用本体，不放可写存储）。
            //   锚点已 provisioning（RSA-2048）——候选验签链可用；轮换/撤销经下一版内置表 + Revoke
            //   体现（私钥绝不入库/入包）。
            // - **候选通道 = 部署配置二选一**（§7 部署配置契约；基址非信任边界——信封必过验签、
            //   文件必过摘要复算，被污染的基址只能造成失败/拒绝）：
            //   配了 CDN（ContentDeployConfig，命令行注入）→ 信封与文件都走 HTTP（HttpCandidateProvider +
            //   HttpCandidateFetcher，来源经 DownloadPlan 下发）；未配 → 本地信封文件通道（现行为，
            //   无网络依赖，离线开发用）。
            // - **磁盘余量 = DriveInfo → 写探针回退**：优先 DriveInfo 精确值；不可用（实测 IL2CPP
            //   Windows Player 的 DriveInfo 拿不到卷余量）→ 写探针取下界（只在确有候选、预检时机写入，
            //   属更新期一次性成本）；两者均不可知 → -1 → 空间预检按不足处理（fail-closed）。
            // - **健康探针族**：
            //   候选配置（config/ 前缀 = Luban 表字节）+ 候选 Lua（lua/ 前缀 → 模块名 →
            //   受控沙箱执行）——两者基于候选根文件，先于资源包初始化可运行。
            //   入口资源可加载性探针（AssetsHealthProbe）查当前代次入口，须在资源包初始化后才有意义
            //   （Patch 先于初始化，时序不可能）——本模块只**登记**探针，由 ProcedurePatch 在初始化后执行；
            //   入口取真实实体 prefab（Characters 收集组），不再依赖已退役的样例①标记素材。
            // 部署配置三源（命令行 > 包内 deploy.args 烙入 > 默认——分发包零参数启动；基址非信任边界不变）
            ContentCdnConfig deploy = ContentDeployConfig.Load(
                Environment.GetCommandLineArgs(), _deployBake.ReadArgs());
            string candidateRoot = ReleaseLayout.CandidateRootRelative;
            ICandidateFetcher fetcher;
            ICandidateProvider provider;
            Func<ReleaseManifest, DownloadPlan> planFactory;
            if (deploy.HasCdn)
            {
                var source = new DownloadSource("cdn", deploy.BaseUrl);
                fetcher = new HttpCandidateFetcher(candidateRoot, deploy.TimeoutSeconds, files);
                provider = new HttpCandidateProvider(deploy.BaseUrl, deploy.OfferPath, deploy.TimeoutSeconds);
                planFactory = manifest => new DownloadPlan(manifest, new[] { source });
                Log.Info($"内容通道：CDN {deploy.BaseUrl}（信封 {deploy.OfferPath}）", LogTag.Content);
            }
            else
            {
                fetcher = new LocalDirectoryCandidateFetcher(candidateRoot, files);
                provider = new FileSystemCandidateProvider(files: files);
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
                    new CandidateConfigHealthProbe(candidateRoot, ReleaseLayout.ConfigPaths, files),
                    new LuaScriptsHealthProbe(candidateRoot, ReleaseLayout.LuaScripts, files)),
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
            context.Put(new AssetsHealthProbe(_content, new[] { LiteGame.View.SimView.DefaultEntityPrefab }));
            return UniTask.CompletedTask;
        }

        /// <summary>场景与缓存租约都走内容包：场景服务、清缓存实现面归本段（进程级实例，关闭时归零）。</summary>
        private void RegisterSceneAndCacheServices(ModuleContext context, IFileStore files)
        {
            _scenes = new SceneService();
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
