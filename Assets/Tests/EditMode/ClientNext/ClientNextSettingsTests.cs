using System;
using System.Threading;
using LiteGameNext.Features.Settings;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>设置定义与页面：默认/钳制/滑条/画质开关/恢复默认；真实 prefab 装载。</summary>
    public sealed class ClientNextSettingsTests
    {
        private float _volume;
        private int _quality;

        [SetUp]
        public void SetUp()
        {
            _volume = AudioListener.volume;
            _quality = QualitySettings.GetQualityLevel();
        }

        [TearDown]
        public void TearDown()
        {
            AudioListener.volume = _volume;
            QualitySettings.SetQualityLevel(_quality, true);
        }

        [Test]
        public void 设置值_钳制到值域()
        {
            var settings = new GameSettings();
            settings.MasterVolume.Set(2f);
            settings.Quality.Set(9);

            Assert.AreEqual(1f, settings.MasterVolume.Value);
            Assert.AreEqual(3, settings.Quality.Value);   // 与旧客户端同值域：quality 钳制 0..3
        }

        [Test]
        public void 设置页_滑条与开关写回定义()
        {
            var ui = new UiRuntime(ClientNextTestPages.PageFactory());
            ui.Initialize();
            var feature = new SettingsFeature(ui);
            try
            {
                feature.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
                var page = (SettingsPage)ui.Top.Page;
                Slider slider = Find<Slider>(page, "Sld_Volume");
                Toggle toggle = Find<Toggle>(page, "Tgl_Quality");

                slider.value = 0.25f;
                toggle.isOn = false;

                Assert.AreEqual(0.25f, feature.Settings.MasterVolume.Value, 0.001f);
                Assert.AreEqual(0, feature.Settings.Quality.Value);
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        [Test]
        public void 恢复默认_写回默认值()
        {
            var settings = new GameSettings();
            settings.MasterVolume.Set(0.1f);
            settings.Quality.Set(0);

            settings.ResetDefaults();

            Assert.AreEqual(0.8f, settings.MasterVolume.Value, 0.001f);
            Assert.AreEqual(2, settings.Quality.Value);
        }

        private static T Find<T>(PageBase page, string bindName) where T : Component
        {
            T[] components = page.GetComponentsInChildren<T>(true);
            for (int index = 0; index < components.Length; index++)
            {
                BindName bind = components[index].GetComponent<BindName>();
                if (bind != null && bind.Name == bindName) return components[index];
            }

            throw new InvalidOperationException("找不到控件：" + bindName);
        }
    }
}
