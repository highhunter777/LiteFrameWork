using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame.Modules.Config
{
    /// <summary>⑩ 配置：表投影数据源（页面目录生成物/注册表填充依赖）。字节通道经 ④内容服务租约
    /// （代次/引用统一——提取 bytes 即释放源资产，热更专项 §9）；候选校验→原子发布见 <see cref="ConfigService"/>。
    ///
    /// **适配器由组合根注入**：`ConfigService` 属 `LiteGame.Adapters.Luban`，而该程序集反向引用本域
    /// （`Adapters.Luban → Modules.Config`）——段内直接 `new` 会成环，故接工厂委托，
    /// 装配形态（用哪个实现）留在 <see cref="AppComposition"/>。</summary>
    public sealed class ConfigModule : IClientModule, IClientModuleGraph
    {
        private readonly Func<IContentService, IConfigService> _createConfig;

        public ConfigModule(Func<IContentService, IConfigService> createConfig)
            => _createConfig = createConfig ?? throw new ArgumentNullException(nameof(createConfig));

        public string Name => "Config";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "Content" };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            var content = context.Require<IContentService>();   // 依赖②——注册序即依赖序
            context.Put<IConfigService>(_createConfig(content));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 配置重发布/事务在热更链
    }
}
