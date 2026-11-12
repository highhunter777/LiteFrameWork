using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Settings
{
    /// <summary>设置功能：持设置定义并打开设置页；页面直接绑定设置，无 ISettingsPagePort 双门面。</summary>
    public sealed class SettingsFeature : IDisposable
    {
        private readonly UiRuntime _ui;

        public SettingsFeature(UiRuntime ui, GameSettings settings = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Settings = settings ?? new GameSettings();
        }

        public GameSettings Settings { get; }

        /// <summary>打开设置页并注入设置定义；页面关闭后设置值保留。</summary>
        public UniTask<PageHandle> OpenAsync(CancellationToken cancellationToken)
            => _ui.OpenAsync<SettingsPage>(SettingsPage.Spec, page => page.Bind(Settings), cancellationToken);

        public void Dispose()
        {
        }
    }
}
