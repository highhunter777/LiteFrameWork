using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Platform.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LiteGameNext.Features.Settings
{
    /// <summary>设置页：14 项可操作设置（音量/画质/渲染/抗锯齿/阴影/显示/帧率/同步/灵敏度/音效/反转Y/语言）。</summary>
    public sealed class SettingsPage : PageBase
    {
        private const string Title = "设置";

        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Settings/Content/SettingsPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.Lru);

        private GameSettings _settings;
        private UnityAction<float> _volumeChanged;
        private UnityAction<bool> _qualityChanged;
        private UnityAction<bool> _sfxChanged;
        private UnityAction<bool> _invertChanged;

        public void Bind(GameSettings settings) => _settings = settings;

        protected override void OnShow()
        {
            if (_settings == null)
            {
                Log.Warning("SettingsPage 未绑定 GameSettings");
                return;
            }

            SetText("Txt_Title", Title);
            BindSliders();
            BindToggles();
            OnClick("Btn_Language", CycleLanguage);
            OnClick("Btn_Reset", ResetDefaults);
            OnClick("Btn_Close", Close);
            Refresh();
        }

        private void BindSliders()
        {
            GameSettings s = _settings;
            _volumeChanged = v => s.MasterVolume.Set(v);
            LinkSlider("Sld_Volume", s.MasterVolume.Value, 0f, 1f, _volumeChanged);

            LinkSlider("Sld_RenderScale", s.RenderScale.Value, -1f, 3f,
                v => s.RenderScale.Set((int)System.Math.Round(v)));
            LinkSlider("Sld_AA", s.AntiAliasing.Value, -1f, 3f,
                v => s.AntiAliasing.Set((int)System.Math.Round(v)));
            LinkSlider("Sld_Shadow", s.ShadowQuality.Value, -1f, 3f,
                v => s.ShadowQuality.Set((int)System.Math.Round(v)));
            LinkSlider("Sld_DisplayMode", s.DisplayMode.Value, 0f, 2f,
                v => s.DisplayMode.Set((int)System.Math.Round(v)));
            LinkSlider("Sld_FrameRate", FrameRateIndex(), 0f, 5f,
                v => s.FrameRate.Set(FrameRateFromIndex((int)System.Math.Round(v))));
            LinkSlider("Sld_VSync", s.VSync.Value, -1f, 1f,
                v => s.VSync.Set((int)System.Math.Round(v)));
            LinkSlider("Sld_LookSens", s.LookSensitivity.Value, 0.2f, 3f,
                v => s.LookSensitivity.Set(v));
            LinkSlider("Sld_AdsSens", s.AdsSensitivity.Value, 0.5f, 1.5f,
                v => s.AdsSensitivity.Set(v));
        }

        private void LinkSlider(string bind, float value, float min, float max, UnityAction<float> handler)
        {
            Slider slider = Bind<Slider>(bind);
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(handler);
        }

        private void BindToggles()
        {
            GameSettings s = _settings;
            _qualityChanged = on => s.Quality.Set(on ? 3 : 0);
            LinkToggle("Tgl_Quality", s.Quality.Value >= 2, _qualityChanged);

            _sfxChanged = on => s.SoundEnabled.Set(on);
            LinkToggle("Tgl_Sfx", s.SoundEnabled.Value, _sfxChanged);

            _invertChanged = on => s.InvertLookY.Set(on);
            LinkToggle("Tgl_InvertY", s.InvertLookY.Value, _invertChanged);
        }

        private void LinkToggle(string bind, bool isOn, UnityAction<bool> handler)
        {
            Toggle toggle = Bind<Toggle>(bind);
            if (toggle == null) return;
            toggle.SetIsOnWithoutNotify(isOn);
            toggle.onValueChanged.AddListener(handler);
        }

        protected override void OnHide()
        {
            Unlink("Sld_Volume", _volumeChanged);
            Unlink("Tgl_Quality", _qualityChanged);
            Unlink("Tgl_Sfx", _sfxChanged);
            Unlink("Tgl_InvertY", _invertChanged);
            _volumeChanged = null;
            _qualityChanged = null;
            _sfxChanged = null;
            _invertChanged = null;
        }

        private void Unlink(string bind, UnityAction<float> handler)
        {
            if (handler == null) return;
            Slider slider = Bind<Slider>(bind);
            if (slider != null) slider.onValueChanged.RemoveListener(handler);
        }

        private void Unlink(string bind, UnityAction<bool> handler)
        {
            if (handler == null) return;
            Toggle toggle = Bind<Toggle>(bind);
            if (toggle != null) toggle.onValueChanged.RemoveListener(handler);
        }

        private void CycleLanguage()
        {
            string current = _settings.Language.Value;
            _settings.Language.Set(current == "zh-CN" ? "en" : "zh-CN");
            Refresh();
        }

        private void ResetDefaults()
        {
            _settings?.ResetAll();
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
            GameSettings s = _settings;
            SetLabel("Sld_Volume", (int)(s.MasterVolume.Value * 100f) + "%");
            SetLabel("Sld_FrameRate", s.FrameRate.Value == 0 ? "不限" : s.FrameRate.Value + " fps");
            SetLabel("Sld_LookSens", s.LookSensitivity.Value.ToString("F2"));
            SetLabel("Sld_AdsSens", s.AdsSensitivity.Value.ToString("F2"));
            SetLabel("Btn_Language", "语言：" + (s.Language.Value == "zh-CN" ? "中文" : "English"));
            SetLabel("Txt_Resolution", "分辨率：" + (string.IsNullOrEmpty(s.Resolution.Value) ? "默认" : s.Resolution.Value));
            SetLabel("Txt_Bindings", "按键：默认");
            SetText("Txt_Status", "设置 " + CountNonDefault(s) + " 项非默认");
        }

        /// <summary>改控件子树上的文本（滑块/按钮的当前值显示）；控件类型不限。</summary>
        private void SetLabel(string bind, string text)
        {
            Component node = Bind<Component>(bind);
            if (node == null) return;
            TextMeshProUGUI label = node.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = text;
        }

        private static int CountNonDefault(GameSettings s)
        {
            int count = 0;
            foreach (ISetting setting in s.All)
                if (!Equals(setting.DefaultValue, setting.CurrentValue)) count++;
            return count;
        }

        private int FrameRateIndex()
        {
            System.Collections.Generic.IReadOnlyList<int> options =
                FrameRateOptions.For(Application.isMobilePlatform);
            for (int i = 0; i < options.Count; i++)
                if (options[i] == _settings.FrameRate.Value) return i;
            return 0;
        }

        private static int FrameRateFromIndex(int index)
        {
            System.Collections.Generic.IReadOnlyList<int> options =
                FrameRateOptions.For(Application.isMobilePlatform);
            if (options.Count == 0) return 0;
            return options[System.Math.Clamp(index, 0, options.Count - 1)];
        }
    }
}
