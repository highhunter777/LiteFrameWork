using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>② 内容服务：IContentService 注入装配（YooAsset 适配 + 共享加载协调器 + 租约）——
    /// 配置/Lua 字节通道与 U1/C2 租约消费者依赖它（注册序即依赖序：先于 Config/Presentation）；
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
            context.Put(new ActivationTransactionStore(new FileActivationRecordIO()));   // C1-⑩：启动恢复决策（Patch 流程消费）

            // 热更链生产装配（《客户端总设计》§6/§8/§12 端到端装配批）。装配形态决定能力边界：
            // - **信任锚**：内置锚表（ContentTrustAnchors——根信任编入应用本体，不放可写存储）。
            //   首版锚点已 provisioning（2026-09-26，release-key-2026-09-26 / RSA-2048）——
            //   候选验签链可用；轮换/撤销经下一版内置表 + Revoke 体现（私钥绝不入库/入包）。
            // - **候选来源 = 本地信封文件**（`content/candidate.json`）：无 CDN 条件下走完整链路；
            //   CDN 通道（HttpCandidateFetcher）随部署配置接入——未配 CDN 不空挂。
            // - **磁盘余量 = DriveInfo**：桌面返回真实可用空间；移动端 -1 不可知 → 空间预检按不足处理。
            // - **健康探针族**：候选配置（config/ 前缀 = Luban 表字节）+ 候选 Lua（lua/ 前缀 → 模块名 →
            //   受控沙箱执行）——两者基于候选根文件，先于资源包初始化可运行。
            //   入口资源可加载性探针（AssetsHealthProbe）查当前代次入口，须在资源包初始化后才有意义
            //   （Patch 先于初始化，时序不可能）——归 Preload 后接缝（见待办总览 §2.1 边界）。
            var trustedKeys = new TrustedKeyStore();
            ContentTrustAnchors.ApplyTo(trustedKeys);                                    // 首版锚点已 provisioning（2026-09-26）
            var candidateFiles = new FileSysCandidateFileSource(CandidateRoot);
            var coordinator = new PatchCoordinator(
                context.Require<ActivationTransactionStore>(),
                candidateFiles,
                new DriveInfoSpaceProbe(UnityEngine.Application.persistentDataPath),
                new LocalDirectoryCandidateFetcher(CandidateRoot),
                new CompositeHealthCheck(
                    new CandidateConfigHealthProbe(CandidateRoot, ReleaseLayout.ConfigPaths),
                    new LuaScriptsHealthProbe(CandidateRoot, ReleaseLayout.LuaScripts)),
                new ContentActivator(_content));
            context.Put(new PatchRunner(
                new FileSystemCandidateProvider(),
                BuildPlayerCapabilities(),
                context.Require<ActivationTransactionStore>(),
                coordinator,
                trustedKeys.AsResolver(),                                                // keyId → 验签器；未登记/已撤销 → null → 拒（§6）
                budget: null));
            context.Put(coordinator);
            return UniTask.CompletedTask;
        }

        /// <summary>候选根（FileSys 相对路径）。</summary>
        internal const string CandidateRoot = "content/candidate";

        /// <summary>
        /// 运行时可接受的能力下限（§5 版本元组"当前 Player 实际具备"的一侧）。
        /// 由编译期常量/生成物填充——**不接受运行时可变来源**（否则兼容判定可被内容影响）。
        ///
        /// 本形态下多数维度取 0 = 不限；`AppVersion` 留空 = 不要求精确匹配。
        /// 真实能力声明随生成链接线（§5"不能只上报 Player 内旧常量"）。
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
