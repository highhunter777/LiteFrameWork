using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;

namespace LiteGame
{
    /// <summary>③ 设置：加载玩家偏好（先于容器——UI/声音壳注册时就要读）；退出前保存（《客户端总设计》§6.1 刷新钩子语义）。</summary>
    internal sealed class SettingsModule : IClientModule
    {
        private SettingService _settings;

        public string Name => "Settings";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            _settings = new SettingService();
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
