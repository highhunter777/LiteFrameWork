using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework;
using UnityEngine;

namespace LiteGame.Modules.Settings
{
    /// <summary>③ 设置：加载玩家偏好（先于容器——UI/音频服务注册时就要读）；退出前保存（《客户端总设计》§6.1 刷新钩子语义）。
    /// 平台差异（帧率档默认值）在此用运行时查询传入 <see cref="GameSettings"/>，数据层不做平台分支（R15）。</summary>
    public sealed class SettingsModule : IClientModule, IClientModuleGraph
    {
        private SettingService _settings;
        private LookPreferences _look;      // 视角偏好单实例（ProductSettings 刷新 + 战斗域只读）

        public string Name => "Settings";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "PlatformInfrastructure" };
        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            _settings = new SettingService(context.Require<IFileStore>());
            _settings.Load();
            context.Put(_settings);
            var gameSettings = new GameSettings(_settings, Application.isMobilePlatform);
            context.Put(gameSettings);
            // 视角偏好**单实例**：具体键供设置门面 Refresh（改灵敏度即生效），端口键供战斗域只读——
            // 两处拿到同一实例是"设置页改值 → 对局同步"的唯一保证。
            _look = new LookPreferences(gameSettings);
            context.Put(_look);
            context.Put<ILookPreferences>(_look);
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _settings?.Save();
            return UniTask.CompletedTask;
        }
    }
}
