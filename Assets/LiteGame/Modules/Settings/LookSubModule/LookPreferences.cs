using System;
using LiteFramework;

namespace LiteGame.Modules.Settings
{
    /// <summary>
    /// ILookPreferences 的实现：把 GameSettings 的三项读成**缓存值**。
    /// </summary>
    public sealed class LookPreferences : ILookPreferences
    {
        private readonly GameSettings _settings;

        public LookPreferences(GameSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Refresh();
        }

        public float SensitivityMultiplier { get; private set; }
        public float AdsScaleMultiplier { get; private set; }
        public bool InvertY { get; private set; }

        /// <summary>重新读取设置（改值/恢复默认后调用；对局中即时生效，无需重开）。</summary>
        public void Refresh()
        {
            SensitivityMultiplier = _settings.LookSensitivity.Get();
            AdsScaleMultiplier = _settings.AdsSensitivity.Get();
            InvertY = _settings.InvertLookY.Get();
        }
    }
}
