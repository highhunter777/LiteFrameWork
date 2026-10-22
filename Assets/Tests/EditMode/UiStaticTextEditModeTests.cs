using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient.UI;
using LiteFramework;
using LiteGame.Editor.UiTools.Text;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-1 静态文案：`LTextKeyRef` 是静态文案的唯一声明，运行时在页面打开/语言变更时写入，
    /// 关闭后必须退订——池化复用不得继续被旧语言事件刷新。
    /// </summary>
    public sealed class UiStaticTextEditModeTests : UnityTestBase
    {
        private static LocalizationCatalog Catalog()
        {
            var c = new LocalizationCatalog();
            c.Add("UI.Test.Title", "zh-CN", "标题");
            c.Add("UI.Test.Title", "en", "Title");
            return c;
        }

        private sealed class FakeCatalog : IUIFormCatalog
        {
            private readonly UIFormInfo _row = new UIFormInfo
            {
                Id = 11,
                LuaPath = "UI.Page_Static",
                Location = "test/page_static",
                Layer = 0,
                FullScreen = true,
            };

            public UIFormInfo Get(int id) => id == _row.Id ? _row : throw new System.Collections.Generic.KeyNotFoundException();
        }

        private GameObject BuildPrefab()
        {
            var root = new GameObject("Page_Static", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var child = new GameObject("Txt_Title", typeof(RectTransform), typeof(Text));
            child.transform.SetParent(root.transform, false);
            child.GetComponent<Text>().text = "prefab";
            child.AddComponent<LTextKeyRef>().Key = "UI.Test.Title";
            return root;
        }

        private static T Await<T>(UniTask<T> task, UIService service, int maxTicks = 10)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++) service.Tick(0.05f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            return task.GetAwaiter().GetResult();
        }

        private static void Await(UniTask task, UIService service, int maxTicks = 10)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++) service.Tick(0.05f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void 绑定器_按key写入_语言变更刷新_解绑后不刷新()
        {
            GameObject root = Scope.CreateGameObject("root", typeof(RectTransform));
            GameObject textGo = Scope.CreateGameObject("Txt_Title", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(root.transform, false);
            textGo.AddComponent<LTextKeyRef>().Key = "UI.Test.Title";
            var text = textGo.GetComponent<Text>();
            var locale = new LocalizationService(Catalog(), "zh-CN");

            var binder = new UiStaticTextBinder(root.transform);
            binder.Bind(locale);
            Assert.AreEqual("标题", text.text, "打开时应按 key 写入源语言");

            locale.SetLocale("en");
            Assert.AreEqual("Title", text.text, "语言变更应刷新静态文案");

            binder.Unbind();
            locale.SetLocale("zh-CN");
            Assert.AreEqual("Title", text.text, "解绑后不得再被语言事件刷新");
        }

        [Test]
        public void 页面打开与关闭_静态文案随页面生命周期刷新()
        {
            GameObject prefab = BuildPrefab();
            var locale = new LocalizationService(Catalog(), "zh-CN");
            var ui = new UIService(new FakeCatalog(),
                transitionStrategy: new InstantTransition(),
                logicResolver: _ => NullUIFormLogic.Instance,
                loadPrefab: (_, __) => UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(prefab)),
                localeProvider: () => locale);

            try
            {
                UIForm form = Await(ui.ShowAsync(11), ui);
                var text = form.Root.GetComponentInChildren<Text>(true);
                Assert.AreEqual("标题", text.text, "页面打开时 UIForm 应驱动静态文案绑定");

                locale.SetLocale("en");
                Assert.AreEqual("Title", text.text, "页面在场时语言切换应刷新");

                Await(ui.CloseAsync(11), ui);
                locale.SetLocale("zh-CN");
                Assert.AreEqual("Title", text.text, "关闭后静态文案绑定必须退订");
            }
            finally
            {
                ui.ShutdownAsync().GetAwaiter().GetResult();
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void TMP文本_优先写入_语言变更刷新()
        {
            GameObject root = Scope.CreateGameObject("root", typeof(RectTransform));
            GameObject textGo = Scope.CreateGameObject("Txt_Tmp", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            textGo.transform.SetParent(root.transform, false);
            textGo.AddComponent<LTextKeyRef>().Key = "UI.Test.Title";
            var tmp = textGo.GetComponent<TMPro.TextMeshProUGUI>();
            var locale = new LocalizationService(Catalog(), "zh-CN");

            var binder = new UiStaticTextBinder(root.transform);
            binder.Bind(locale);
            Assert.AreEqual("标题", tmp.text, "TMP_Text 是首选目标（与受控 API 同序）");

            locale.SetLocale("en");
            Assert.AreEqual("Title", tmp.text, "语言变更应刷新 TMP 文本");
        }

        [Test]
        public void 无文本组件_不抛异常且不影响其他节点()
        {
            GameObject root = Scope.CreateGameObject("root", typeof(RectTransform));
            GameObject bad = Scope.CreateGameObject("Bad", typeof(RectTransform));
            bad.transform.SetParent(root.transform, false);
            bad.AddComponent<LTextKeyRef>().Key = "UI.Test.Title";
            GameObject good = Scope.CreateGameObject("Good", typeof(RectTransform), typeof(Text));
            good.transform.SetParent(root.transform, false);
            good.AddComponent<LTextKeyRef>().Key = "UI.Test.Title";
            var text = good.GetComponent<Text>();
            var locale = new LocalizationService(Catalog(), "zh-CN");

            var binder = new UiStaticTextBinder(root.transform);
            Assert.DoesNotThrow(() => binder.Bind(locale));
            Assert.AreEqual("标题", text.text, "坏节点不应阻断同页其他静态文案");
        }

        [Test]
        public void 编辑器文案表_可读取真实key()
        {
            Assert.IsTrue(UiLTextTable.TryGetRows(out var rows, out string error), error);
            Assert.IsTrue(rows.ContainsKey("UI.Login.Title"), "真实 tbltext 应包含登录标题键");
            Assert.IsTrue(UiLTextTable.Keys().Contains("UI.Login.Title"));
        }

        [Test]
        public void 空key_不写入作者文本()
        {
            GameObject root = Scope.CreateGameObject("root", typeof(RectTransform));
            GameObject textGo = Scope.CreateGameObject("Txt_Empty", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(root.transform, false);
            textGo.GetComponent<Text>().text = "authored";
            textGo.AddComponent<LTextKeyRef>().Key = string.Empty;
            var locale = new LocalizationService(Catalog(), "zh-CN");

            new UiStaticTextBinder(root.transform).Bind(locale);
            Assert.AreEqual("authored", textGo.GetComponent<Text>().text, "空 key 不得用空文本覆盖作者文本");
        }
    }
}
