namespace LiteFramework
{
    /// <summary>
    /// 音频设置生效器：主音量 → 四组分组音量；音效开关 → Effect 组门。
    ///
    /// **语义边界**（既有口径，不变）：音效关只静音 Effect 组，Bgm/Voice/Ui 仍受主音量；
    /// 不引入"独立 BGM 音量"——那是另一层产品需求，不在当前设置模型里。
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
            _audio.SetGroupVolume(AudioService.Group.Ui, master);
            _audio.SetGroupVolume(AudioService.Group.Effect, sfx ? master : 0f);   // 音效开关 = Effect 组门
        }
    }
}
