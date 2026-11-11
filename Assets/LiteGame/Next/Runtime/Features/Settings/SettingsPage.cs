using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Platform.Ui;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LiteGameNext.Features.Settings
{
    /// <summary>设置页：音量滑条 + 画质开关 + 恢复默认/关闭；控件直接写设置定义。</summary>
    public sealed class SettingsPage : PageBase
    {
        private const string Title = "设置";

        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Settings/Content/SettingsPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.Lru);

        private GameSettings _settings;
        private Slider _volume;
        private Toggle _quality;
        private UnityAction<float> _volumeChanged;
        private UnityAction<bool> _qualityChanged;

        public void Bind(GameSettings settings) => _settings = settings;

        protected override void OnShow()
        {
            if (_settings == null)
            {
                Log.Warning("SettingsPage 未绑定 GameSettings");
                return;
            }

            SetText("Txt_Title", Title);
            _volume = Bind<Slider>("Sld_Volume");
            _volume.value = _settings.MasterVolume.Value;
            _volumeChanged = value => _settings.MasterVolume.Set(value);
            _volume.onValueChanged.AddListener(_volumeChanged);

            _quality = Bind<Toggle>("Tgl_Quality");
            _quality.isOn = _settings.Quality.Value >= 2;
            _qualityChanged = on => _settings.Quality.Set(on ? 2 : 0);
            _quality.onValueChanged.AddListener(_qualityChanged);

            OnClick("Btn_Reset", ResetDefaults);
            OnClick("Btn_Close", Close);
            Refresh();
        }

        protected override void OnHide()
        {
            if (_volume != null && _volumeChanged != null)
                _volume.onValueChanged.RemoveListener(_volumeChanged);
            if (_quality != null && _qualityChanged != null)
                _quality.onValueChanged.RemoveListener(_qualityChanged);
            _volume = null;
            _quality = null;
            _volumeChanged = null;
            _qualityChanged = null;
        }

        private void ResetDefaults()
        {
            _settings?.ResetDefaults();
            if (_volume != null) _volume.SetValueWithoutNotify(_settings.MasterVolume.Value);
            if (_quality != null) _quality.SetIsOnWithoutNotify(_settings.Quality.Value >= 2);
            Refresh();
        }

        private void Close()
        {
            if (Handle == null) return;
            Handle.CloseAsync().Forget(ex => Log.Error("关闭设置页失败：" + ex));
        }

        private void Refresh()
        {
            if (_settings == null) return;
            SetText("Txt_Status", "音量 " + (int)(_settings.MasterVolume.Value * 100f)
                + "% · 画质 " + (_settings.Quality.Value >= 2 ? "高" : "低"));
        }
    }
}
