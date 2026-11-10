using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Settings;

namespace LiteGame.Modules.UI
{
    /// <summary>设置页：五页签（音频/图形/显示/控制/常规），即改即生效、关闭落盘、对局中锁显示类。</summary>
    public sealed class Page_SettingsLogic : PageLogicBase
    {
        private static readonly string[] Locales = { "zh-CN", "en" };
        private static readonly string[] LocaleKeys = { "UI.Settings.Language.Zh", "UI.Settings.Language.En" };
        private static readonly string[] QualityKeys =
            { "UI.Settings.Quality.Low", "UI.Settings.Quality.Medium", "UI.Settings.Quality.High" };
        private static readonly string[] TabTextKeys =
        {
            "UI.Settings.Tab.Audio", "UI.Settings.Tab.Graphics", "UI.Settings.Tab.Display",
            "UI.Settings.Tab.Control", "UI.Settings.Tab.General",
        };
        private static readonly string[] TabTextNodes =
            { "Txt_TabAudio", "Txt_TabGraphics", "Txt_TabDisplay", "Txt_TabControl", "Txt_TabGeneral" };
        private static readonly string[] Panels =
        {
            "Cot_AudioPanel", "Cot_GraphicsPanel", "Cot_DisplayPanel", "Cot_ControlPanel", "Cot_GeneralPanel",
        };
        private static readonly int[] DisplayModeValues = { 0, 1, 2 };
        private static readonly string[] DisplayModeKeys =
        {
            "UI.Settings.DisplayMode.Fullscreen", "UI.Settings.DisplayMode.Borderless", "UI.Settings.DisplayMode.Windowed",
        };
        private static readonly int[] VSyncValues = { -1, 1, 0 };
        private static readonly string[] VSyncKeys = { "UI.Settings.FollowPreset", "Common.On", "Common.Off" };
        private static readonly string[] SettingsControls =
        {
            "Sld_Volume", "Tgl_Sound", "Ddl_Quality", "Ddl_DisplayMode", "Ddl_Resolution", "Ddl_FrameRate",
            "Ddl_VSync", "Sld_LookSensitivity", "Sld_AdsSensitivity", "Tgl_InvertLookY", "Ddl_Language",
            "Btn_ResetDefaults",
        };

        private const int TabCount = 5;
        private const int WindowedMode = 2;

        private bool _exitable;
        private bool _leaving;
        private int _tab = 1;

        protected override void OnShow(IUIData data)
        {
            if (Services == null)
            {
                ApplyNoServicesState();
                return;
            }

            PushStaticTexts();
            var pageData = data as SettingsPageData;
            _exitable = pageData != null && pageData.BattleExit;
            _leaving = false;
            SetVisible("Btn_ExitMatch", _exitable);
            if (_exitable)
            {
                SetTextKey("Txt_ExitMatch", string.IsNullOrEmpty(pageData.ExitKey) ? "UI.Settings.Exit" : pageData.ExitKey);
                OnButtonClick("Btn_ExitMatch", ConfirmExit);
            }

            PushReadOnlyFacts();
            ISettingsPagePort settings = Settings;
            _tab = settings.LastTab + 1;
            if (_tab < 1 || _tab > TabCount) _tab = 1;
            ApplyTab(_tab);
            Index.SetTab("Tabs_Settings", _tab - 1);

            PushAllOptions();
            RefreshControls();

            Index.OnTabChanged("Tabs_Settings", index => OnTab(index + 1));
            Index.OnSliderChanged("Sld_Volume", value => settings.SetVolume(value));
            Index.OnToggleChanged("Tgl_Sound", on => settings.SetSoundEnabled(on));
            Index.OnDropdownChanged("Ddl_Quality", index => settings.SetQuality(index));
            Index.OnDropdownChanged("Ddl_DisplayMode", index =>
            {
                settings.SetDisplayMode(DisplayModeValues[index]);
                RefreshResolutionRow();
            });
            Index.OnDropdownChanged("Ddl_Resolution", index => settings.SetResolutionIndex(index));
            Index.OnDropdownChanged("Ddl_FrameRate", index => settings.SetFrameRate(settings.FrameRateOption(index)));
            Index.OnDropdownChanged("Ddl_VSync", index => settings.SetVSync(VSyncValues[index]));
            Index.OnSliderChanged("Sld_LookSensitivity", value =>
            {
                settings.SetLookSensitivity(value);
                PushSensitivityText();
            });
            Index.OnSliderChanged("Sld_AdsSensitivity", value =>
            {
                settings.SetAdsSensitivity(value);
                PushSensitivityText();
            });
            Index.OnToggleChanged("Tgl_InvertLookY", on => settings.SetInvertLookY(on));
            Index.OnDropdownChanged("Ddl_Language", index =>
            {
                Locale?.SetLocale(Locales[index]);
                PushAllOptions();                          // 新语言下重建全部下拉文本
            });
            OnButtonClick("Btn_ResetDefaults", ConfirmReset);
            OnButtonClick("Btn_Close", CloseWithSave);

            bool locked = _exitable;
            SetInteractable("Ddl_DisplayMode", !locked);
            SetInteractable("Ddl_VSync", !locked);
            if (locked) SetInteractable("Ddl_Resolution", false);
        }

        /// <summary>无装配面（页面上下文未注入）：设置控件全部禁用、页签仍可切换，关闭键保留——不抛。</summary>
        private void ApplyNoServicesState()
        {
            _tab = 1;
            ApplyTab(_tab);
            foreach (string node in SettingsControls)
                if (HasControl(node)) SetInteractable(node, false);
            if (HasControl("Tabs_Settings"))
            {
                Index.SetTab("Tabs_Settings", 0);
                Index.OnTabChanged("Tabs_Settings", index => OnTab(index + 1));
            }
            OnButtonClick("Btn_Close", CloseWithSave);
        }

        private void PushStaticTexts()
        {
            SetTextKey("Txt_Title", "Common.Settings");
            SetTextKey("Txt_Close", "Common.Close");
            for (int i = 0; i < TabCount; i++) SetTextKey(TabTextNodes[i], TabTextKeys[i]);
            SetTextKey("Txt_Volume", "UI.Settings.Volume");
            SetTextKey("Txt_Sound", "UI.Settings.Sound");
            SetTextKey("Txt_Quality", "UI.Settings.Quality");
            SetTextKey("Txt_DisplayMode", "UI.Settings.DisplayMode");
            SetTextKey("Txt_Resolution", "UI.Settings.Resolution");
            SetTextKey("Txt_FrameRate", "UI.Settings.FrameRate");
            SetTextKey("Txt_VSync", "UI.Settings.VSync");
            SetTextKey("Txt_LookSensitivity", "UI.Settings.LookSensitivity");
            SetTextKey("Txt_AdsSensitivity", "UI.Settings.AdsSensitivity");
            SetTextKey("Txt_InvertLookY", "UI.Settings.InvertLookY");
            SetTextKey("Txt_Language", "UI.Settings.Language");
            SetTextKey("Txt_ResetDefaults", "UI.Settings.ResetDefaults");
            SetTextKey("Txt_Version", "Common.Version", Services != null ? Meta.Version : string.Empty);
        }

        private void PushReadOnlyFacts()
        {
            string account = Meta.AccountId;
            SetVisible("Txt_Account", !string.IsNullOrEmpty(account));
            if (!string.IsNullOrEmpty(account)) SetTextKey("Txt_Account", "UI.Settings.Account", account);

            string server = Meta.ServerUrl;
            SetVisible("Txt_ServerAddress", !string.IsNullOrEmpty(server));
            if (!string.IsNullOrEmpty(server)) SetTextKey("Txt_ServerAddress", "UI.Settings.Server", server);
        }

        private void OnTab(int index)
        {
            _tab = index;
            ApplyTab(index);
            Settings.SetLastTab(index - 1);
        }

        private void ApplyTab(int index)
        {
            for (int i = 0; i < TabCount; i++) SetVisible(Panels[i], i + 1 == index);
        }

        /// <summary>下拉项文本：语言切换后整批重建（下拉是引擎组件，不随 LText 自动刷新）。</summary>
        private void PushAllOptions()
        {
            var quality = new string[QualityKeys.Length];
            for (int i = 0; i < QualityKeys.Length; i++) quality[i] = Text(QualityKeys[i]);
            Index.SetDropdownOptions("Ddl_Quality", quality);

            var modes = new string[DisplayModeKeys.Length];
            for (int i = 0; i < DisplayModeKeys.Length; i++) modes[i] = Text(DisplayModeKeys[i]);
            Index.SetDropdownOptions("Ddl_DisplayMode", modes);

            var vsync = new string[VSyncKeys.Length];
            for (int i = 0; i < VSyncKeys.Length; i++) vsync[i] = Text(VSyncKeys[i]);
            Index.SetDropdownOptions("Ddl_VSync", vsync);

            PushFrameRateOptions();
            RefreshResolutionRow();

            var locales = new string[LocaleKeys.Length];
            for (int i = 0; i < LocaleKeys.Length; i++) locales[i] = Text(LocaleKeys[i]);
            Index.SetDropdownOptions("Ddl_Language", locales);
            Index.SetDropdownValue("Ddl_Language", IndexOf(Locales, Locale != null ? Locale.Locale : Locales[0], 0));
        }

        private void PushFrameRateOptions()
        {
            ISettingsPagePort settings = Settings;
            int count = settings.FrameRateOptionCount;
            var options = new string[count];
            for (int i = 0; i < count; i++)
            {
                int cap = settings.FrameRateOption(i);
                options[i] = cap == 0 ? Text("UI.Settings.FrameRate.Unlimited")
                                      : cap.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            Index.SetDropdownOptions("Ddl_FrameRate", options);
        }

        /// <summary>分辨率行：仅窗口模式可改；全屏/无边框显示"跟随显示器"并置灰。</summary>
        private void RefreshResolutionRow()
        {
            ISettingsPagePort settings = Settings;
            if (settings.DisplayMode != WindowedMode)
            {
                Index.SetDropdownOptions("Ddl_Resolution", new[] { Text("UI.Settings.Resolution.FollowDisplay") });
                Index.SetDropdownValue("Ddl_Resolution", 0);
                SetInteractable("Ddl_Resolution", false);
                return;
            }

            int count = settings.ResolutionCount();
            var options = new string[count];
            for (int i = 0; i < count; i++) options[i] = settings.ResolutionLabel(i);
            Index.SetDropdownOptions("Ddl_Resolution", options);
            int index = settings.ResolutionIndex;
            Index.SetDropdownValue("Ddl_Resolution", index >= 0 ? index : 0);
            SetInteractable("Ddl_Resolution", !_exitable);
        }

        private void PushSensitivityText()
        {
            ISettingsPagePort settings = Settings;
            SetText("Txt_LookSensitivityValue", Percent(settings.LookSensitivity));
            SetText("Txt_AdsSensitivityValue", Percent(settings.AdsSensitivity));
        }

        private static string Percent(float value)
            => ((int)(value * 100f + 0.5f)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        private void RefreshControls()
        {
            ISettingsPagePort settings = Settings;
            Index.SetSlider("Sld_Volume", settings.Volume);
            Index.SetToggle("Tgl_Sound", settings.SoundEnabled);
            Index.SetDropdownValue("Ddl_Quality", settings.Quality);
            Index.SetDropdownValue("Ddl_DisplayMode", IndexOf(DisplayModeValues, settings.DisplayMode, 1));
            RefreshResolutionRow();
            Index.SetDropdownValue("Ddl_FrameRate", FrameRateIndex(settings));
            Index.SetDropdownValue("Ddl_VSync", IndexOf(VSyncValues, settings.VSync, 0));
            Index.SetSlider("Sld_LookSensitivity", settings.LookSensitivity);
            Index.SetSlider("Sld_AdsSensitivity", settings.AdsSensitivity);
            Index.SetToggle("Tgl_InvertLookY", settings.InvertLookY);
            PushSensitivityText();
        }

        private int FrameRateIndex(ISettingsPagePort s)
        {
            int current = s.FrameRate;
            int count = s.FrameRateOptionCount;
            for (int i = 0; i < count; i++) if (s.FrameRateOption(i) == current) return i;
            return 0;
        }

        private static int IndexOf(int[] list, int value, int fallback)
        {
            for (int i = 0; i < list.Length; i++) if (list[i] == value) return i;
            return fallback;
        }

        private static int IndexOf(string[] list, string value, int fallback)
        {
            for (int i = 0; i < list.Length; i++) if (string.Equals(list[i], value, StringComparison.Ordinal)) return i;
            return fallback;
        }

        private string Text(string key) => Locale != null ? Locale.Get(key) : key;

        private void ConfirmReset() => ConfirmResetAsync().ForgetLogged("Page_Settings.reset");

        private async UniTask ConfirmResetAsync()
        {
            IConfirmDialog confirm = Confirm;
            int generation = Form.DisplayGeneration;
            bool ok = await confirm.ShowAsync(Text("UI.Settings.ResetConfirmTitle"),
                Text("UI.Settings.ResetConfirmMessage"), Text("Common.Confirm"), Text("Common.Cancel"));
            if (!ok || !Form.IsDisplayCurrent(generation)) return;
            Settings.ResetDefaults();
            PushAllOptions();
            RefreshControls();
        }

        private void ConfirmExit() => ConfirmExitAsync().ForgetLogged("Page_Settings.exit");

        /// <summary>确认离房：只表达离房意图，页面保留到统一恢复清理完成后由 PageFlow 回大厅。</summary>
        private async UniTask ConfirmExitAsync()
        {
            if (_leaving) return;
            _leaving = true;
            SetInteractable("Btn_ExitMatch", false);
            IConfirmDialog confirm = Confirm;
            int generation = Form.DisplayGeneration;
            bool ok = await confirm.ShowAsync(Text("UI.Settings.ExitConfirmTitle"),
                Text("UI.Settings.ExitConfirmMessage"), Text("Common.Confirm"), Text("Common.Cancel"));
            if (!Form.IsDisplayCurrent(generation)) return;
            _leaving = false;
            SetInteractable("Btn_ExitMatch", true);
            if (!ok) return;
            Meta.LeaveMatch();
        }

        private void CloseWithSave()
        {
            if (Services != null) Settings.Save();
            CloseSelf();
        }

        protected override void OnPageHide()
        {
            if (Services != null) Settings.Save();         // Back/ESC 关闭同径落盘
            _leaving = false;
        }
    }
}
