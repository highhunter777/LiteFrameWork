using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// ① 平台基础设施：本地持久化 + 日志宿主（先于一切 IO；Log 未注入静默丢弃）。
    ///
    /// 构造顺序（池 → 文件 → JSON → 日志安装）由框架 <see cref="FrameworkInfrastructure.Create"/> 统一保证，
    /// 本段只决定"开不开/多吵"并登记产物。
    ///
    /// 装配清单与依赖序见 <see cref="GameEntry"/>；模块契约见 <see cref="IClientModule"/>。
    /// 结构化日志文件（dev 三宏内）：`persistentDataPath/logs/*.jsonl`，检索见 `scripts/logs/query.py`。
    /// </summary>
    public sealed class PlatformInfrastructureModule : IClientModule, IClientModuleGraph
    {
        private FrameworkInfrastructure _infra;

        public string Name => "PlatformInfrastructure";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => System.Array.Empty<string>();

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 引用池实例（Root 域）：生产消费者经 IReferencePool 注入（静态门面已删除）。
            _infra = FrameworkInfrastructure.Create(FrameworkInfrastructureOptions.FromDevBuild());
            context.Put<FrameworkInfrastructure>(_infra);   // 装配入口本身留可读（Events/工具链取池与命令中心）
            context.Put<IReferencePool>(_infra.References);
            context.Scope.Register(_infra);                 // 随本模块关闭释放池（清空全部空闲件）

            // 文件端口（进程级）：消费者经 IFileStore 注入（静态门面已删除）。
            // 注：IJsonSerializer 刻意**不进容器**——它唯一的消费者是 FileSystem 的构造参数，
            // 注册进容器却没有容器内消费者会违反"注册必须有生产消费者"；业务侧的 JSON 需求
            // 走 IJsonCodec（语义不同：见 IJsonCodec 注释）。
            context.Put<IFileStore>(_infra.Files);

            // 网络契约 JSON 端口（严格类别读取 + 字面量拼装）：第三方实现只在框架装配入口被 new，
            // 业务（MetaJson/UnityMetaClient）只认 IJsonCodec——不直接引用序列化库。
            context.Put<IJsonCodec>(_infra.Json);

            // 日志实例（进程级）：静态 Log 只是门面；新代码/适配器经 ILog 注入。
            context.Put<ILog>(_infra.Logger);

            // 主线程派发 + 命令中心（框架基础设施件；命令中心只依赖池）。
            context.Put<IMainThreadDispatcher>(_infra.Dispatcher);
            context.Put<CommandCenter>(_infra.Commands);
            context.Put<ICommandCenter>(_infra.Commands);

            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 日志落盘/崩溃上报由平台层负责
    }
}
