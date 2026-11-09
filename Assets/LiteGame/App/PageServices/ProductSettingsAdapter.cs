using System;
using LiteGame.Modules.Settings;
using LiteGame.Modules.UI;

namespace LiteGame
{
    /// <summary><see cref="ISettingsPagePort"/> 的生产实现：包设置门面，页面不认 <c>ProductSettings</c>。</summary>
    public sealed class ProductSettingsAdapter : ISettingsPagePort
    {
        private readonly ProductSettings _settings;

        public ProductSettingsAdapter(ProductSettings settings)
            => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public int LastTab => _settings.LastTab;
        public int FrameRateOptionCount => _settings.FrameRateOptionCount;
        public int FrameRateOption(int index) => _settings.FrameRateOption(index);
        public int DisplayMode => _settings.DisplayMode;
        public int ResolutionCount() => _settings.ResolutionCount();
        public string ResolutionLabel(int index) => _settings.ResolutionLabel(index);
        public int ResolutionIndex => _settings.ResolutionIndex;
        public float LookSensitivity => _settings.LookSensitivity;
        public float AdsSensitivity => _settings.AdsSensitivity;
        public float Volume => _settings.Volume;
        public bool SoundEnabled => _settings.SoundEnabled;
        public int Quality => _settings.Quality;
        public int FrameRate => _settings.FrameRate;
        public int VSync => _settings.VSync;
        public bool InvertLookY => _settings.InvertLookY;

        public void SetLastTab(int index) => _settings.SetLastTab(index);
        public void SetVolume(float value01) => _settings.SetVolume(value01);
        public void SetSoundEnabled(bool on) => _settings.SetSoundEnabled(on);
        public void SetQuality(int tier) => _settings.SetQuality(tier);
        public void SetDisplayMode(int mode) => _settings.SetDisplayMode(mode);
        public void SetResolutionIndex(int index) => _settings.SetResolutionIndex(index);
        public void SetFrameRate(int cap) => _settings.SetFrameRate(cap);
        public void SetVSync(int value) => _settings.SetVSync(value);
        public void SetLookSensitivity(float multiplier) => _settings.SetLookSensitivity(multiplier);
        public void SetAdsSensitivity(float multiplier) => _settings.SetAdsSensitivity(multiplier);
        public void SetInvertLookY(bool on) => _settings.SetInvertLookY(on);
        public void ResetDefaults() => _settings.ResetDefaults();
        public void Save() => _settings.Save();
    }
}
