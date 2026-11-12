using System;
using System.Collections.Generic;
using System.IO;
using LiteGameNext.Features.Settings;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>设置持久化与 16 项定义：往返、缺/坏文件降级、键与默认值防漂移、值域钳制。</summary>
    public sealed class ClientNextSettingsStoreTests
    {
        private string _directory;
        private float _audioVolume;
        private int _qualityLevel;
        private int _vSyncCount;
        private int _targetFrameRate;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "litegame-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            // 生效器会改全局引擎状态，用例前后各抓一次还原。
            _audioVolume = AudioListener.volume;
            _qualityLevel = QualitySettings.GetQualityLevel();
            _vSyncCount = QualitySettings.vSyncCount;
            _targetFrameRate = Application.targetFrameRate;
        }

        [TearDown]
        public void TearDown()
        {
            AudioListener.volume = _audioVolume;
            QualitySettings.SetQualityLevel(_qualityLevel, true);
            QualitySettings.vSyncCount = _vSyncCount;
            Application.targetFrameRate = _targetFrameRate;
            try
            {
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响用例结论。
            }
        }

        [Test]
        public void 持久化往返_同目录新store读回一致()
        {
            var store = new SettingsStore(_directory);
            var settings = new GameSettings(store);
            settings.MasterVolume.Set(0.35f);
            settings.SoundEnabled.Set(false);
            settings.Quality.Set(3);
            settings.RenderScale.Set(85);
            settings.AntiAliasing.Set(4);
            settings.ShadowQuality.Set(2);
            settings.DisplayMode.Set(DisplayOptions.ModeWindowed);
            settings.Resolution.Set("1600x900");
            settings.FrameRate.Set(120);
            settings.VSync.Set(1);
            settings.LookSensitivity.Set(2.5f);
            settings.AdsSensitivity.Set(0.75f);
            settings.InvertLookY.Set(true);
            settings.Bindings.Set("{\"bindings\":[]}");
            settings.Language.Set("en-US");
            settings.LastTab.Set(3);
            store.SaveIfDirty();

            var reloaded = new SettingsStore(_directory);
            reloaded.Load();
            var restored = new GameSettings(reloaded);

            Assert.AreEqual(0.35f, restored.MasterVolume.Value, 0.0001f);
            Assert.IsFalse(restored.SoundEnabled.Value);
            Assert.AreEqual(3, restored.Quality.Value);
            Assert.AreEqual(85, restored.RenderScale.Value);
            Assert.AreEqual(4, restored.AntiAliasing.Value);
            Assert.AreEqual(2, restored.ShadowQuality.Value);
            Assert.AreEqual(DisplayOptions.ModeWindowed, restored.DisplayMode.Value);
            Assert.AreEqual("1600x900", restored.Resolution.Value);
            Assert.AreEqual(120, restored.FrameRate.Value);
            Assert.AreEqual(1, restored.VSync.Value);
            Assert.AreEqual(2.5f, restored.LookSensitivity.Value, 0.0001f);
            Assert.AreEqual(0.75f, restored.AdsSensitivity.Value, 0.0001f);
            Assert.IsTrue(restored.InvertLookY.Value);
            Assert.AreEqual("{\"bindings\":[]}", restored.Bindings.Value);
            Assert.AreEqual("en-US", restored.Language.Value);
            Assert.AreEqual(3, restored.LastTab.Value);
        }

        [Test]
        public void 缺文件_不抛且全为默认()
        {
            var store = new SettingsStore(_directory);
            Assert.DoesNotThrow(() => store.Load());
            Assert.IsNull(store.Get("volume"));

            var settings = new GameSettings(store);
            Assert.AreEqual(0.8f, settings.MasterVolume.Value, 0.0001f);
            Assert.AreEqual(2, settings.Quality.Value);
        }

        [Test]
        public void 损坏文件_不抛且退回默认()
        {
            File.WriteAllText(Path.Combine(_directory, "settings.json"), "这不是 JSON {{{ 坏文件");
            var store = new SettingsStore(_directory);
            Assert.DoesNotThrow(() => store.Load());

            var settings = new GameSettings(store);
            Assert.AreEqual(0.8f, settings.MasterVolume.Value, 0.0001f);
            Assert.AreEqual(-1, settings.RenderScale.Value);
            Assert.AreEqual("zh-CN", settings.Language.Value);
        }

        [Test]
        public void 坏值_不抛且退回默认()
        {
            var store = new SettingsStore(_directory);
            store.Load();
            store.Set("volume", "不是数字");
            store.Set("quality", "abc");

            var settings = new GameSettings(store);
            Assert.AreEqual(0.8f, settings.MasterVolume.Value, 0.0001f);
            Assert.AreEqual(2, settings.Quality.Value);
        }

        [Test]
        public void 十六项_键名与默认值对齐旧客户端()
        {
            var settings = new GameSettings();
            // 默认值写字面量（照旧客户端 `Modules/Settings/Model/GameSettings.cs`）：常量被改动时此处即红。
            var expected = new Dictionary<string, object>
            {
                { "volume", 0.8f },
                { "sfx", true },
                { "quality", 2 },
                { "renderScale", -1 },
                { "aa", -1 },
                { "shadow", -1 },
                { "displayMode", 1 },
                { "resolution", string.Empty },
                { "frameRate", 0 },
                { "vsync", -1 },
                { "lookSens", 1f },
                { "adsSens", 1f },
                { "invertY", false },
                { "bindings", string.Empty },
                { "lang", "zh-CN" },
                { "settingsTab", 0 },
            };

            Assert.AreEqual(16, settings.All.Count, "设置项应为 16 项");
            foreach (ISetting setting in settings.All)
            {
                Assert.IsTrue(expected.ContainsKey(setting.Key), "出现未登记的键：" + setting.Key);
                Assert.AreEqual(expected[setting.Key], setting.DefaultValue, "键 " + setting.Key + " 的默认值漂移");
            }

            Assert.AreEqual(16, expected.Count);
            Assert.AreEqual(-1, GraphicsOptions.FollowPreset);
            Assert.AreEqual(1, DisplayOptions.DefaultMode);
            Assert.AreEqual(-1, DisplayOptions.VSyncFollowPreset);
            Assert.AreEqual(0, FrameRateOptions.Unlimited);
            Assert.AreEqual(60, FrameRateOptions.MobileDefault);
            Assert.AreEqual(5, GameSettings.SettingsTabCount);
        }

        [Test]
        public void 移动端_帧率默认六十且档位收敛()
        {
            var mobile = new GameSettings(null, isMobilePlatform: true);
            Assert.AreEqual(60, mobile.FrameRate.DefaultValue);

            var desktop = new GameSettings(null, isMobilePlatform: false);
            Assert.AreEqual(0, desktop.FrameRate.DefaultValue);

            mobile.FrameRate.Set(144);      // 移动端无该档 → 收敛到最近合法档
            Assert.AreEqual(60, mobile.FrameRate.Value);

            desktop.FrameRate.Set(144);
            Assert.AreEqual(144, desktop.FrameRate.Value);
        }

        [Test]
        public void 值域钳制_超界被归一()
        {
            var settings = new GameSettings();

            settings.MasterVolume.Set(2f);
            Assert.AreEqual(1f, settings.MasterVolume.Value);

            settings.Quality.Set(9);
            Assert.AreEqual(3, settings.Quality.Value);

            settings.RenderScale.Set(63);
            Assert.AreEqual(-1, settings.RenderScale.Value);

            settings.AntiAliasing.Set(8);
            Assert.AreEqual(-1, settings.AntiAliasing.Value);

            settings.ShadowQuality.Set(5);
            Assert.AreEqual(-1, settings.ShadowQuality.Value);

            settings.DisplayMode.Set(7);
            Assert.AreEqual(DisplayOptions.DefaultMode, settings.DisplayMode.Value);

            settings.VSync.Set(9);
            Assert.AreEqual(-1, settings.VSync.Value);

            settings.LookSensitivity.Set(99f);
            Assert.AreEqual(3f, settings.LookSensitivity.Value, 0.0001f);

            settings.AdsSensitivity.Set(0.01f);
            Assert.AreEqual(0.5f, settings.AdsSensitivity.Value, 0.0001f);

            settings.LastTab.Set(99);
            Assert.AreEqual(4, settings.LastTab.Value);

            settings.Resolution.Set("1920X1080");
            Assert.AreEqual("1920x1080", settings.Resolution.Value);

            settings.Resolution.Set("不是分辨率");
            Assert.AreEqual(string.Empty, settings.Resolution.Value);
        }

        [Test]
        public void 恢复默认_全部回默认且写回存档()
        {
            var store = new SettingsStore(_directory);
            var settings = new GameSettings(store);
            settings.MasterVolume.Set(0.2f);
            settings.LastTab.Set(4);

            settings.ResetAll();

            Assert.AreEqual(0.8f, settings.MasterVolume.Value, 0.0001f);
            Assert.AreEqual(0, settings.LastTab.Value);
            Assert.AreEqual("0.8", store.Get("volume"));
            Assert.AreEqual("0", store.Get("settingsTab"));
        }

        [Test]
        public void 音效开关_实例可查询()
        {
            var settings = new GameSettings();
            settings.SoundEnabled.Set(false);
            Assert.IsFalse(settings.SfxEnabled);
            settings.SoundEnabled.Set(true);
            Assert.IsTrue(settings.SfxEnabled);
        }
    }
}
