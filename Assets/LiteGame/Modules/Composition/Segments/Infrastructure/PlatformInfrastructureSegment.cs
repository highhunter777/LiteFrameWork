using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame.Modules.Composition
{
    /// <summary>
    /// ① 平台基础设施：本地持久化 + 日志宿主（先于一切 IO）。
    /// </summary>
    public sealed class PlatformInfrastructureSegment : IClientSegment, IClientSegmentGraph
    {
        private FrameworkInfrastructure _infra;

        public string Name => "PlatformInfrastructure";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => System.Array.Empty<string>();

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            // 平台基础设施实例（Root 域）：文件/JSON/日志/派发/命令总线。
            _infra = FrameworkInfrastructure.Create(FrameworkInfrastructureOptions.FromDevBuild());
            context.Put<FrameworkInfrastructure>(_infra);   // 装配入口本身留可读（工具链取命令总线）
            context.Scope.Register(_infra);                 // 随本模块关闭释放

            // 文件端口（进程级）：消费者经 IFileStore 注入（静态门面已删除）。
            context.Put<IFileStore>(_infra.Files);

            // 网络契约 JSON 端口（严格类别读取 + 字面量拼装）：第三方实现只在框架装配入口被 new，
            // 业务（MetaJson/UnityMetaClient）只认 IJsonCodec——不直接引用序列化库。
            context.Put<IJsonCodec>(_infra.Json);

            // 日志实例（进程级）：静态 Log 只是门面；新代码/适配器经 ILog 注入。
            context.Put<ILog>(_infra.Logger);

            // 主线程派发 + 命令总线（框架基础设施件；命令总线只依赖池）。
            context.Put<IMainThreadDispatcher>(_infra.Dispatcher);
            context.Put<CommandBus>(_infra.Commands);
            context.Put<ICommandBus>(_infra.Commands);

            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 日志落盘/崩溃上报由平台层负责
    }
}
