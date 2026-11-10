namespace LiteGame.Modules.Settings
{
    /// <summary>
    /// 设置页面向页面的原语读写端口（页面只认本口，不认 ProductSettings 实现类）。
    /// 写成员即改即生效（无 Apply）；由 ProductSettings 实现。
    /// </summary>
    public interface ISettingsPagePort
    {
        // ---- 读（15）----

        int LastTab { get; }
        int FrameRateOptionCount { get; }
        int FrameRateOption(int index);
        int DisplayMode { get; }
        int ResolutionCount();
        string ResolutionLabel(int index);
        int ResolutionIndex { get; }
        float LookSensitivity { get; }
        float AdsSensitivity { get; }
        float Volume { get; }
        bool SoundEnabled { get; }
        int Quality { get; }
        int FrameRate { get; }
        int VSync { get; }
        bool InvertLookY { get; }

        // ---- 写/动作（13）----

        void SetLastTab(int index);
        void SetVolume(float value01);
        void SetSoundEnabled(bool on);
        void SetQuality(int tier);
        void SetDisplayMode(int mode);
        void SetResolutionIndex(int index);
        void SetFrameRate(int cap);
        void SetVSync(int value);
        void SetLookSensitivity(float multiplier);
        void SetAdsSensitivity(float multiplier);
        void SetInvertLookY(bool on);
        void ResetDefaults();
        void Save();
    }
}
