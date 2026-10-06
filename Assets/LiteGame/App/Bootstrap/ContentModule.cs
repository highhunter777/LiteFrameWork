using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;

namespace LiteGame
{
    /// <summary>② 内容服务：IContentService 注入装配（YooAsset 适配 + 共享加载协调器 + 租约）——
    /// 配置/Lua 字节通道与租约消费者依赖它（注册序即依赖序：先于 Config/Presentation）；
    /// 资源包初始化仍由 ProcedurePreload 经本服务触发。</summary>
    internal sealed class ContentModule : IClientModule
    {
        private YooAssetContentService _content;

        public string Name => "Content";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            _content = new YooAssetContentService();
            context.Put<IContentService>(_content);
            context.Put<IGenerationSink>(_content);                                      // 代次推进（§9：确认后切换，失败回退）
            context.Put(new ActivationTransactionStore(new FileActivationRecordIO()));   // 启动恢复决策（Patch 流程消费）

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
            //   入口清单见 ContentSampleAssets（样例①真实资产包段：标记素材只在候选包，能加载即证据）。
            ContentCdnConfig deploy = ContentDeployConfig.Load();
            string candidateRoot = ReleaseLayout.CandidateRootRelative;
            ICandidateFetcher fetcher;
            ICandidateProvider provider;
            Func<ReleaseManifest, DownloadPlan> planFactory;
            if (deploy.HasCdn)
            {
                var source = new DownloadSource("cdn", deploy.BaseUrl);
                fetcher = new HttpCandidateFetcher(candidateRoot, deploy.TimeoutSeconds);
                provider = new HttpCandidateProvider(deploy.BaseUrl, deploy.OfferPath, deploy.TimeoutSeconds);
                planFactory = manifest => new DownloadPlan(manifest, new[] { source });
                Log.Info($"内容通道：CDN {deploy.BaseUrl}（信封 {deploy.OfferPath}）", "Content");
            }
            else
            {
                fetcher = new LocalDirectoryCandidateFetcher(candidateRoot);
                provider = new FileSystemCandidateProvider();
                planFactory = null;                       // PatchRunner 默认计划（本地通道只清点落盘、不消费来源）
                Log.Info("内容通道：本地信封（未配置 CDN）", "Content");
            }

            var trustedKeys = new TrustedKeyStore();
            ContentTrustAnchors.ApplyTo(trustedKeys);                                // 锚点已 provisioning
            var candidateFiles = new FileSysCandidateFileSource(candidateRoot);
            var coordinator = new PatchCoordinator(
                context.Require<ActivationTransactionStore>(),
                candidateFiles,
                // 空间预检探**内容将写入的卷**——候选根即写入目标（FileSys 相对路径，
                // 卷归属与 RootPath 同卷：RootPath = Application.persistentDataPath）。
                new WriteProbeDiskSpaceProbe(candidateRoot),
                fetcher,
                new CompositeHealthCheck(
                    new CandidateConfigHealthProbe(candidateRoot, ReleaseLayout.ConfigPaths),
                    new LuaScriptsHealthProbe(candidateRoot, ReleaseLayout.LuaScripts)),
                new ContentActivator(_content));
            context.Put(new PatchRunner(
                provider,
                BuildPlayerCapabilities(),
                context.Require<ActivationTransactionStore>(),
                coordinator,
                trustedKeys.AsResolver(),                                                // keyId → 验签器；未登记/已撤销 → null → 拒（§6）
                budget: null,
                planFactory: planFactory));
            context.Put(coordinator);

            // 入口资产探针（初始化后执行；时序说明见上）——入口清单来自 ContentSampleAssets（单源）。
            context.Put(new AssetsHealthProbe(_content, ContentSampleAssets.EntryLocations));
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 运行时可接受的能力下限（§5 版本元组"当前 Player 实际具备"的一侧）。
        /// 由编译期常量/生成物填充——**不接受运行时可变来源**（否则兼容判定可被内容影响）。
        ///
        /// 本形态下多数维度取 0 = 不限；`AppVersion` 留空 = 不要求精确匹配。
        /// 真实能力声明须来自编译期生成物，不能只上报 Player 内旧常量（§5）。
        /// </summary>
        private static PlayerCapabilities BuildPlayerCapabilities() => new PlayerCapabilities
        {
            AppVersion = "",
            Platform = UnityEngine.Application.platform.ToString(),
            Channel = "",
            BridgeApiVersion = 0,
            ProtocolVersion = 0,
            SimVersion = 0,
            ConfigSchemaVersion = 0,
            SaveSchemaVersion = 0,
        };

        public UniTask ShutdownAsync(CancellationToken ct)
            => _content?.ShutdownAsync(ct) ?? UniTask.CompletedTask;   // 释放面：剩余租约/句柄归零
    }
}
