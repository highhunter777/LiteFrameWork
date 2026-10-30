using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// ① 平台基础设施：本地持久化 + 日志宿主（先于一切 IO；Log 未注入静默丢弃）。
    ///
    /// 装配清单与依赖序见 <see cref="GameEntry"/>；模块契约见 <see cref="IClientModule"/>。
    /// 结构化日志文件（dev 三宏内）：`persistentDataPath/logs/*.jsonl`，检索见 `scripts/logs/query.py`。
    /// </summary>
    internal sealed class PlatformInfrastructureModule : IClientModule, IClientModuleGraph
    {
        public string Name => "PlatformInfrastructure";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<System.Type> DependsOn => System.Array.Empty<System.Type>();
        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 引用池实例（Root 域）：生产消费者经 IReferencePool 注入（静态门面已删除）。
            var references = new ReferencePoolService(trackUsage: DevBuildFacts.IsDevelopment);   // 开发形态检测重复归还
            context.Put<IReferencePool>(references);
            context.Put(references);
            context.Scope.Register(references);          // 随本模块关闭 Dispose（清空全部空闲件）

            var files = new FileSystem(new UnityPathProvider(), new NewtonsoftJsonSerializer());

            // 文件端口（进程级）：消费者经 IFileStore 注入（静态门面已删除）。
            // 注：IJsonSerializer 刻意**不进容器**——它唯一的消费者是 FileSystem 的构造参数，
            // 注册进容器却没有容器内消费者会违反"注册必须有生产消费者"；业务侧的 JSON 需求
            // 走下面的 IJsonCodec（语义不同：见 IJsonCodec 注释）。
            context.Put<IFileStore>(files);

            // 网络契约 JSON 端口（严格类别读取 + 字面量拼装）：第三方实现只在本装配层被 new，
            // 业务（MetaJson/UnityMetaClient）只认 IJsonCodec——不直接引用序列化库。
            context.Put<IJsonCodec>(new NewtonsoftJsonCodec());

            // 日志实例（进程级）：静态 Log 只是门面；新代码/适配器经 ILog 注入。
            var logService = new LogService(DevBuildFacts.IsDevelopment ? LogLevel.Info : LogLevel.Warning);
            context.Put<ILog>(logService);
            Log.Install(logService);
            Log.SetHelper(new UnityLogHelper());
            if (DevBuildFacts.IsDevelopment) StructuredLogInstaller.Install();   // 开发形态装结构化日志（发布不落盘）
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 日志落盘/崩溃上报由平台层负责
    }
}
