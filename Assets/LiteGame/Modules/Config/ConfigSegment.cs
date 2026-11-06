using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// ⑩ 配置：表投影数据源（页面目录生成物/注册表填充依赖）。
    /// </summary>
    public sealed class ConfigSegment : IClientSegment, IClientSegmentGraph
    {
        private readonly Func<IContentService, IFileStore, ActivationTransactionStore, IConfigService> _createConfig;

        public ConfigSegment(
            Func<IContentService, IFileStore, ActivationTransactionStore, IConfigService> createConfig)
            => _createConfig = createConfig ?? throw new ArgumentNullException(nameof(createConfig));

        public string Name => "Config";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "Content", "PlatformInfrastructure" };

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            var content = context.Require<IContentService>();   // 依赖②——注册序即依赖序
            var files = context.Require<IFileStore>();
            var activation = context.Require<ActivationTransactionStore>();
            context.Put<IConfigService>(_createConfig(content, files, activation));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 配置重发布/事务在热更链
    }
}
