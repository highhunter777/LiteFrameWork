using LiteFramework;
namespace LiteGame.Modules.Settings
{
    /// <summary>
    /// 音频设置生效器：主音量 → 四组分组音量。
    /// </summary>
    public sealed class AudioSettingsApplier
    {
        private readonly AudioService _audio;

        public AudioSettingsApplier(AudioService audio)
            => _audio = audio ?? throw new System.ArgumentNullException(nameof(audio));

        /// <summary>幂等：同值重复应用不产生额外副作用（AudioService 侧设定值即覆盖）。</summary>
        public void Apply(GameSettings settings)
        {
            if (settings == null) throw new System.ArgumentNullException(nameof(settings));
            float master = settings.MasterVolume.Get();
            bool sfx = settings.SoundEnabled.Get();
            _audio.SetGroupVolume(AudioService.Group.Bgm, master);
            _audio.SetGroupVolume(AudioService.Group.Voice, master);
            _audio.SetGroupVolume(AudioService.Group.UI, master);
            _audio.SetGroupVolume(AudioService.Group.Effect, sfx ? master : 0f);   // 音效开关 = Effect 组门
        }
    }
}
