using LiteFramework;
using LiteGame;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 预览语言面（P2-3 B3 / AC2）：tbltext 文件直载构建服务（中/英）＋伪本地化装饰器（长文本）＋
    /// 安全区模拟策略换算（当前屏幕像素空间）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiPreviewLocaleEditModeTests
    {
        private const string SampleKey = "UI.Login.Title";      // 试点页真实 key（契约 text[3] 之一）

        [Test]
        public void 直载_服务非空且中文可用()
        {
            ILocalizationService service = UiPreviewLocale.BuildService();
            Assert.IsNotNull(service, "tbltext 直载应可用（AnimationProfileTableSource 模式）");
            string zh = service.Raw(SampleKey);
            Assert.AreNotEqual(SampleKey, zh, "已知 key 不应回显 key 本身（缺 key 语义）");
            Assert.IsFalse(zh.StartsWith("["), "不应走 [key] 降级");
        }

        [Test]
        public void 直载_表缺失返回空并降级()
        {
            Assert.IsNull(UiPreviewLocale.BuildService("Assets/NoSuchDir/"),
                "表目录缺失应返回 null（调用方走降级并报告）");
        }

        [Test]
        public void 状态词_off_ok_unavailable()
        {
            Assert.AreEqual("off", UiPreviewLocale.StatusFor(string.Empty, false));
            Assert.AreEqual("unavailable", UiPreviewLocale.StatusFor("en", false));
            Assert.AreEqual("ok", UiPreviewLocale.StatusFor("en", true));
        }

        [Test]
        public void 直载_英文切换可用()
        {
            ILocalizationService service = UiPreviewLocale.BuildService();
            Assert.IsNotNull(service, "tbltext 直载应可用（绿测硬门槛）");
            string zh = service.Raw(SampleKey);
            service.SetLocale("en");
            string en = service.Raw(SampleKey);
            Assert.AreNotEqual(zh, en, "中英取值应不同（英文列非空）");
        }

        [Test]
        public void 伪本地化_加长且区分音标()
        {
            ILocalizationService service = UiPreviewLocale.BuildService();
            Assert.IsNotNull(service, "tbltext 直载应可用（绿测硬门槛）");
            string original = service.Raw(SampleKey);
            var pseudo = new PseudoLocale(service);
            string transformed = pseudo.Raw(SampleKey);
            Assert.GreaterOrEqual(transformed.Length, Mathf.CeilToInt(original.Length * 1.3f),
                "伪本地化至少加长 30%（暴露截断/换行）");
            Assert.AreNotEqual(original, transformed, "内容应变化（音标/填充）");
        }

        [Test]
        public void 伪本地化_事件与缺key透传()
        {
            var fake = new RelayLocale();
            var pseudo = new PseudoLocale(fake);
            string fired = null;
            pseudo.OnLocaleChanged += locale => fired = locale;
            fake.Fire("en");
            Assert.AreEqual("en", fired, "OnLocaleChanged 应透传");
            Assert.AreEqual(fake.MissingKeyCount, pseudo.MissingKeyCount, "缺 key 计数透传");
        }

        [Test]
        public void 安全区策略_当前屏幕像素空间换算()
        {
            // 设备档 2436×1125（横屏），左右 notch inset 132px；屏幕任意（含降级未切分辨率）都应按比例缩放
            var strategy = new PreviewSafeAreaStrategy(2436, 1125, 132f, 0f, 132f, 0f);
            Rect safe = strategy.Resolve();
            float scaleX = Screen.width / 2436f;
            float scaleY = Screen.height / 1125f;
            Assert.AreEqual(132f * scaleX, safe.xMin, 0.5f, "左 inset 按屏幕/设备宽缩放");
            Assert.AreEqual(Screen.width - 132f * scaleX, safe.xMax, 0.5f, "右 inset 同法");
            Assert.AreEqual(0f, safe.yMin, 0.01f, "上下无 inset");
            Assert.AreEqual(Screen.height, safe.yMax, 0.5f);
        }

        /// <summary>事件透传用最小假服务。</summary>
        private sealed class RelayLocale : ILocalizationService
        {
            public string Locale => "zh-CN";
            public event System.Action<string> OnLocaleChanged;
            public int MissingKeyCount => 7;
            public void SetLocale(string locale) => Fire(locale);
            public string Raw(string key) => key;
            public string Get(string key) => key;
            public string Format(string key, params object[] args) => key;
            public string FormatPlural(string key, long count, params object[] args) => key;
            internal void Fire(string locale) => OnLocaleChanged?.Invoke(locale);
        }
    }
}
