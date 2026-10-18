using System;
using LiteClient;
using LiteFramework;
using UnityEngine;
using XLua; // lint-allow R12：设置门面本体住 App（须引用 GameSettings/AudioService/SettingService——与 MetaBridge 同判据），只做值域收敛与生效面

namespace LiteGame
{
    /// <summary>
    /// 设置门面（`Bridge.settings`，批C-⑥）：音量/音效/画质的**读写 + 立即生效**。
    /// 存储走 <see cref="GameSettings"/>（设置项清单在其单源；业务禁裸用 SettingService），
    /// 生效面 = <see cref="AudioService"/> 分组音量 + UnityEngine 画质档位。
    /// **语言不在此**（走 `Bridge.text.SetLocale`）。
    ///
    /// 值域：音量 0~1；音效 = 开关（关 = Effect 组静音；Ui/Voice/Bgm 仍受主音量）；画质 = UI 三档
    /// （低/中/高，与 `#ltext` 的 `UI.Settings.Quality.{Low,Medium,High}` 同序）——平台档位按名匹配，
    /// 缺同名档位按序号钳位（不发明画质等级）。
    /// 构造即应用（**启动生效点 = 装配根**）；改值即应用并标脏，落盘时机归 SettingService 三处纪律
    /// （设置页 OnHide 调 Save；退出经 SettingsModule.ShutdownAsync）。
    /// </summary>
    public sealed class ProductSettings
    {
        /// <summary>UI 画质档数（低/中/高）；GameSettings.Quality 值域 0..3，UI 只暴露 0..2。</summary>
        public const int UiQualityTiers = 3;

        private static readonly string[] TierNames = { "Low", "Medium", "High" };

        private static ProductSettings s_instance;          // 装订面（Lua 全局表；与 MetaBridge.s_meta 同范式）

        private readonly GameSettings _settings;
        private readonly SettingService _store;
        private readonly AudioService _audio;

        public ProductSettings(GameSettings settings, SettingService store, AudioService audio)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            ApplyVolume();
            ApplyQuality();
        }

        /// <summary>装配期装订（ContainerModule——Lua 门面后端）。</summary>
        public static void Bind(ProductSettings settings)
            => s_instance = settings ?? throw new ArgumentNullException(nameof(settings));

        /// <summary>装订 `Bridge.settings` 进全局 Bridge 表（ProductBridges 调用——在框架门面与 meta 之后）。
        /// **未装订也照装**（与 meta 门面同口径）：读取点 <see cref="Instance"/> 在建用错配时 fail-fast——
        /// 让"只测 meta 门面"的用例可以共用 ProductBridges 装订口而不被迫造设置后端。</summary>
        public static void BindGlobals(LuaEnv env)
        {
            var settings = env.NewTable();
            settings.Set("Volume", new Func<float>(() => Instance().Volume));
            settings.Set("SetVolume", new Action<float>(v => Instance().SetVolume(v)));
            settings.Set("SoundEnabled", new Func<bool>(() => Instance().SoundEnabled));
            settings.Set("SetSoundEnabled", new Action<bool>(on => Instance().SetSoundEnabled(on)));
            settings.Set("Quality", new Func<int>(() => Instance().Quality));
            settings.Set("SetQuality", new Action<int>(tier => Instance().SetQuality(tier)));
            settings.Set("Save", new Action(() => Instance().Save()));
            env.Global.Get<LuaTable>("Bridge").Set("settings", settings);
        }

        private static ProductSettings Instance()
            => s_instance ?? throw new InvalidOperationException("Bridge.settings 未装订（装配点未 Bind 设置门面）");

        public float Volume => _settings.MasterVolume.Get();

        public void SetVolume(float value01)
        {
            _settings.MasterVolume.Set(value01);            // 值域 clamp 在 Setting<float> 内（0..1）
            ApplyVolume();
        }

        public bool SoundEnabled => _settings.SoundEnabled.Get();

        public void SetSoundEnabled(bool on)
        {
            _settings.SoundEnabled.Set(on);
            ApplyVolume();
        }

        /// <summary>UI 画质档（0=低 / 1=中 / 2=高）。</summary>
        public int Quality => Mathf.Clamp(_settings.Quality.Get(), 0, UiQualityTiers - 1);

        public void SetQuality(int tier)
        {
            _settings.Quality.Set(Mathf.Clamp(tier, 0, UiQualityTiers - 1));
            ApplyQuality();
        }

        /// <summary>落盘（SettingService 脏标记协议——未改则零写入）。设置页 OnHide / 退出路径调用。</summary>
        public void Save() => _store.SaveIfDirty();

        private void ApplyVolume()
        {
            float master = Volume;
            bool sfx = SoundEnabled;
            _audio.SetGroupVolume(AudioService.Group.Bgm, master);
            _audio.SetGroupVolume(AudioService.Group.Voice, master);
            _audio.SetGroupVolume(AudioService.Group.Ui, master);
            _audio.SetGroupVolume(AudioService.Group.Effect, sfx ? master : 0f);   // 音效开关 = Effect 组门
        }

        private void ApplyQuality()
        {
            int tier = Quality;
            int target = tier;
            string[] names = QualitySettings.names;
            if (names != null && names.Length > 0)
            {
                int found = Array.FindIndex(names, n => string.Equals(n, TierNames[tier], StringComparison.OrdinalIgnoreCase));
                target = found >= 0 ? found : Mathf.Clamp(tier, 0, names.Length - 1);   // 无同名档位：按序号钳位
            }
            QualitySettings.SetQualityLevel(target, applyExpensiveChanges: true);
        }
    }
}
