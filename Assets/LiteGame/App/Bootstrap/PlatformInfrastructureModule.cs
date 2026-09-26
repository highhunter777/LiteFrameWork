using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// ① 平台基础设施：本地持久化 + 日志宿主（先于一切 IO；Log 未注入静默丢弃）。
    ///
    /// 装配清单与依赖序见 <see cref="GameEntry"/>；模块契约见 <see cref="IClientModule"/>。
    /// </summary>
    internal sealed class PlatformInfrastructureModule : IClientModule
    {
        public string Name => "PlatformInfrastructure";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            FileSys.Init(new UnityPathProvider(), new NewtonsoftJsonSerializer());
            Log.SetHelper(new UnityLogHelper());
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 日志落盘/崩溃上报归 C3
    }
}
