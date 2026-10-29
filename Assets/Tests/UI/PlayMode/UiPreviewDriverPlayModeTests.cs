#if UNITY_EDITOR
using System.Collections;
using LiteFramework.UI;
using LiteFramework;
using LiteGame;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 预览驱动 apply/restore 链（P2-3 B3 / AC2；总设计 §7.1 布局档）：安全区策略注入→SafeAreaReceiver
    /// anchors 变化、撤销→回归；字体缩放应用/恢复；语言绑定→LTextKeyRef 刷新、复位→解绑无残留。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiPreviewDriverPlayModeTests
    {
        private sealed class FakeLocale : ILocalizationService
        {
            public string Locale => "zh-CN";
            public event System.Action<string> OnLocaleChanged;
            public int MissingKeyCount => 0;
            public void SetLocale(string locale) => OnLocaleChanged?.Invoke(locale);
            public string Raw(string key) => key == "UI.Test.Key" ? "中文文案" : "[" + key + "]";
            public string Get(string key) => Raw(key);
            public string Format(string key, params object[] args) => Raw(key);
            public string FormatPlural(string key, long count, params object[] args) => Raw(key);
        }

        private static (GameObject canvas, SafeAreaReceiver receiver) BuildSafeAreaPage()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var content = new GameObject("Cot_SafeArea", typeof(RectTransform));
            content.transform.SetParent(canvas.transform, false);
            content.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            content.GetComponent<RectTransform>().anchorMax = Vector2.one;
            return (canvas, content.AddComponent<SafeAreaReceiver>());
        }

        private static UiPreviewDriver CreateDriver(out GameObject driverObject)
        {
            driverObject = new GameObject("driver");
            return driverObject.AddComponent<UiPreviewDriver>();
        }

        [UnityTest]
        public IEnumerator 安全区_注入后anchors收缩_撤销后回归全屏()
        {
            string originalTarget = EditorPrefs.GetString(UiPreviewDriver.TargetPrefabKey, string.Empty);
            EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, string.Empty);
            (GameObject canvas, SafeAreaReceiver receiver) = BuildSafeAreaPage();
            GameObject driverObject = null;
            try
            {
                yield return null;                       // OnEnable 首次 Apply
                Rect before = Screen.safeArea;
                var driver = CreateDriver(out driverObject);

                // 设备档与当前屏幕同尺寸、左右 inset 10%——anchors 应收缩
                driver.ApplySafeArea(Screen.width, Screen.height, Screen.width * 0.1f, 0f, Screen.width * 0.1f, 0f);
                yield return null;                       // 下一帧 Apply（SetStrategy 立即重算，保守等帧）
                RectTransform rt = (RectTransform)receiver.transform;
                Assert.Greater(rt.anchorMin.x, 0.05f, "左 anchor 收缩");
                Assert.Less(rt.anchorMax.x, 0.95f, "右 anchor 收缩");

                driver.ResetPreviewOptions(canvas);
                yield return null;
                Assert.AreEqual(before.xMin / Screen.width, rt.anchorMin.x, 0.01f, "撤销后回归 Screen.safeArea 左边界");
                Assert.AreEqual(before.xMax / Screen.width, rt.anchorMax.x, 0.01f, "撤销后回归 Screen.safeArea 右边界");
            }
            finally
            {
                if (driverObject != null) Object.Destroy(driverObject);
                Object.Destroy(canvas);
                EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, originalTarget);
            }
        }

        [UnityTest]
        public IEnumerator 字体缩放_应用后放大_复位回原值()
        {
            string originalTarget = EditorPrefs.GetString(UiPreviewDriver.TargetPrefabKey, string.Empty);
            EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, string.Empty);
            var page = new GameObject("Page", typeof(Canvas));
            var text = new GameObject("Txt", typeof(TextMeshProUGUI));
            text.transform.SetParent(page.transform, false);
            text.GetComponent<TextMeshProUGUI>().fontSize = 20f;
            GameObject driverObject = null;
            try
            {
                var driver = CreateDriver(out driverObject);
                driver.ApplyFontScale(page, 1.15f);
                Assert.AreEqual(23f, text.GetComponent<TextMeshProUGUI>().fontSize, 0.01f, "1.15 档");

                driver.ResetPreviewOptions(page);
                Assert.AreEqual(20f, text.GetComponent<TextMeshProUGUI>().fontSize, 0.01f, "复位回原值");
                yield return null;
            }
            finally
            {
                if (driverObject != null) Object.Destroy(driverObject);
                Object.Destroy(page);
                EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, originalTarget);
            }
        }

        [UnityTest]
        public IEnumerator 语言_绑定后静态文案刷新_复位解绑无残留()
        {
            string originalTarget = EditorPrefs.GetString(UiPreviewDriver.TargetPrefabKey, string.Empty);
            EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, string.Empty);
            var page = new GameObject("Page", typeof(Canvas));
            var text = new GameObject("Txt_Title", typeof(TextMeshProUGUI));
            text.transform.SetParent(page.transform, false);
            var tmp = text.GetComponent<TextMeshProUGUI>();
            tmp.text = "作者标题";
            var keyRef = text.AddComponent<LTextKeyRef>();
            keyRef.Key = "UI.Test.Key";
            GameObject driverObject = null;
            var locale = new FakeLocale();
            try
            {
                var driver = CreateDriver(out driverObject);
                driver.ApplyLocale(page, locale);
                yield return null;
                Assert.AreEqual("中文文案", tmp.text, "绑定后静态文案按服务取值");

                driver.ApplyLocale(page, null);
                yield return null;
                Assert.AreEqual("作者标题", tmp.text, "切回作者文本还原原串");

                driver.ApplyLocale(page, locale);
                yield return null;
                Assert.AreEqual("中文文案", tmp.text, "再次绑定译文");

                driver.ResetPreviewOptions(page);
                yield return null;
                Assert.AreEqual("作者标题", tmp.text, "复位还原作者原串");
                locale.SetLocale("en");                  // 已解绑——服务变化不得再改文案
                yield return null;
                Assert.AreEqual("作者标题", tmp.text, "复位解绑后无残留订阅");
            }
            finally
            {
                if (driverObject != null) Object.Destroy(driverObject);
                Object.Destroy(page);
                EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, originalTarget);
            }
        }
    }
}
#endif
