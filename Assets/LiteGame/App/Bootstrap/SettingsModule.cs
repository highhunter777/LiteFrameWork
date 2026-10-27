using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;

namespace LiteGame
{
    /// <summary>③ 设置：加载玩家偏好（先于容器——UI/音频服务注册时就要读）；退出前保存（《客户端总设计》§6.1 刷新钩子语义）。</summary>
    internal sealed class SettingsModule : IClientModule, IClientModuleGraph
    {
        private SettingService _settings;

        public string Name => "Settings";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<System.Type> DependsOn => new System.Type[] { typeof(PlatformInfrastructureModule) };
        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            _settings = new SettingService(context.Require<IFileStore>());
            _settings.Load();
            context.Put(_settings);
            context.Put(new GameSettings(_settings));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _settings?.Save();
            return UniTask.CompletedTask;
        }
    }
}
