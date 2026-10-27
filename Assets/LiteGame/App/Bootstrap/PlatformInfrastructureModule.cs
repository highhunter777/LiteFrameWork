using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient;
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
            FileSys.Init(new UnityPathProvider(), new NewtonsoftJsonSerializer());
            Log.SetHelper(new UnityLogHelper());
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            StructuredLogInstaller.Install();
#endif
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 日志落盘/崩溃上报由平台层负责
    }
}
