using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteClient;
using LiteClient.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 行为族 PlayMode 用例（工具链设计 §6.2「行为」——执行面＝PlayMode 真实生命周期）：
    /// ① **复用视觉残留**：池化复用前 PrepareForShow 复位面（CanvasGroup alpha/interactable/blocksRaycasts +
    /// anchoredPosition 回基线）——脏化后关再开必须全复位；
    /// ② **语言切换**：OnLocaleChanged 只重写静态文案（LTextKeyRef 绑定），不重跑 OnShow（《UI 逻辑 API 参考》
    /// 「语言切换只重写 key 绑定，不重跑 OnShow、不重发业务请求」）；
    /// ③ **模态射线穿透**：顶层登记模态打开期间，视觉下方页面 blocksRaycasts=false（UIModalStack「模态有真实
    /// 射线遮罩」），模态关闭恢复——射线打不到底层。
    /// 其余行为例（列表重绑/取消/转场输入锁）已有夹具（List/LuaPage/Navigation PlayModeTests），本组只补缺口。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class UiBehaviorPlayModeTests
    {
        private const int PageLogin = 11;
        private const int Dialog = 201;

        private sealed class StaticCatalog : IPageCatalog
        {
            // 非空 LuaPath：走 resolver 注入（空 LuaPath 会被 ResolveLogic 短路成 NullUIFormLogic——
            // OnShow 计数观测面就没了；UiStatePlayModeTests 同款约定）
            private const string InjectedLogicPath = "Tests.CountingLogic";

            private readonly Dictionary<int, PageDefinition> _rows = new Dictionary<int, PageDefinition>();
            public void Add(int id, int layer, bool fullScreen, string location)
                => _rows[id] = new PageDefinition
                { Id = id, Layer = layer, FullScreen = fullScreen, LuaPath = InjectedLogicPath, Location = location };
            public PageDefinition Get(int id)
            {
                if (_rows.TryGetValue(id, out var r)) return r;
                throw new KeyNotFoundException($"StaticCatalog 无行:{id}");
            }
        }

        private sealed class PlainLease : IUIPrefabLease
        {
            private readonly GameObject _p;
            public PlainLease(GameObject p) => _p = p;
            public GameObject Prefab => _p;
            public void Release() { }
        }

        /// <summary>计数逻辑（断言 OnShow 是否重跑的观测面）。</summary>
        private sealed class CountingLogic : IUIFormLogic
        {
            public int Shows;
            public void OnInit(UIForm form, IUIData data) { }
            public void OnShow(IUIData data) => Shows++;
            public void OnUpdate(float dt) { }
            public void OnPause() { }
            public void OnCover() { }
            public void OnReveal() { }
            public void OnHide() { }
        }

        /// <summary>可切语言的本地化服务（Raw/Get 同值；SetLocale 触发 OnLocaleChanged）。</summary>
        private sealed class SwitchableLocale : ILocalizationService
        {
            private readonly Dictionary<string, string> _zh;
            private readonly Dictionary<string, string> _en;

            public SwitchableLocale(Dictionary<string, string> zh, Dictionary<string, string> en)
            {
                _zh = zh;
                _en = en;
            }

            public string Locale { get; private set; } = "zh-CN";
            public event Action<string> OnLocaleChanged;
            public int MissingKeyCount => 0;

            public void SetLocale(string locale)
            {
                if (string.Equals(Locale, locale, StringComparison.Ordinal)) return;
                Locale = locale;
                OnLocaleChanged?.Invoke(locale);
            }

            public string Raw(string key) => Table().TryGetValue(key, out var v) ? v : key;
            public string Get(string key) => Raw(key);
            public string Format(string key, params object[] args) => Raw(key);
            public string FormatPlural(string key, long count, params object[] args) => Raw(key);

            private Dictionary<string, string> Table()
                => string.Equals(Locale, "en", StringComparison.Ordinal) ? _en : _zh;
        }

        private PlayModeTestScope _scope;
        private UIService _ui;
        private SwitchableLocale _locale;
        private CountingLogic _loginLogic;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(UiBehaviorPlayModeTests));
            if (!_assetsReady)
            {
                var init = AssetService.InitAsync();
                yield return Wait(init, 60f);
                Assert.AreNotEqual(UniTaskStatus.Pending, init.Status, "资源包初始化超时（60s）");
                _assetsReady = true;
            }

            _locale = new SwitchableLocale(
                new Dictionary<string, string> { ["UI.Login.Title"] = "中文标题样例" },
                new Dictionary<string, string> { ["UI.Login.Title"] = "English Title" });

            var catalog = new StaticCatalog();
            catalog.Add(PageLogin, layer: 0, fullScreen: true, "Assets/ShareResource/UI/Screens/Page_Login.prefab");
            catalog.Add(Dialog, layer: 3, fullScreen: false, "Assets/ShareResource/UI/Screens/Tmp_Dialog.prefab");

            _loginLogic = new CountingLogic();
            _ui = new UIService(catalog,
                logicResolver: info => info.Id == PageLogin ? _loginLogic : new CountingLogic(),
                loadPrefab: LoadLeaseAsync,
                transitionStrategy: new FadeSlideTransition(),
                localeProvider: () => _locale);

            var host = _scope.CreateGameObject("Host");
            PlayModeTicker.Attach(host, _ui.Tick);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_ui != null)
            {
                var shutdown = _ui.ShutdownAsync();
                yield return Wait(shutdown, 20f);
                _ui = null;
            }
            yield return _scope.DisposeAsync();
        }

        // ---- 复用视觉残留 ----

        [UnityTest]
        public IEnumerator 复用复位_脏化CanvasGroup与位置_重开后全复位()
        {
            var open = _ui.ShowAsync(PageLogin);
            yield return Wait(open, 20f);
            UIForm form = open.GetAwaiter().GetResult();
            Assert.IsNotNull(form, "首次打开须成功");

            // 脏化框架复位面（模拟展示期被表现/异常改脏）
            form.CanvasGroup.alpha = 0.3f;
            form.CanvasGroup.interactable = false;
            form.CanvasGroup.blocksRaycasts = false;
            Vector2 dirtyPos = new Vector2(123f, -45f);
            ((RectTransform)form.Root.transform).anchoredPosition = dirtyPos;
            int showsBefore = _loginLogic.Shows;

            var close = _ui.CloseAsync(PageLogin);
            yield return Wait(close, 20f);
            yield return Frames(2);                       // 关闭收尾（退订/落池）

            var reopen = _ui.ShowAsync(PageLogin);
            yield return Wait(reopen, 20f);
            UIForm reused = reopen.GetAwaiter().GetResult();
            Assert.IsNotNull(reused, "复用打开须成功");

            Assert.AreEqual(1f, reused.CanvasGroup.alpha, 0.0001f, "复用复位：alpha 回 1");
            Assert.IsTrue(reused.CanvasGroup.interactable, "复用复位：interactable 回 true");
            Assert.IsTrue(reused.CanvasGroup.blocksRaycasts, "复用复位：blocksRaycasts 回 true");
            Vector2 pos = ((RectTransform)reused.Root.transform).anchoredPosition;
            Assert.AreNotEqual(dirtyPos, pos, "复用复位：anchoredPosition 回基线（非脏化值）");
            Assert.AreEqual(showsBefore + 1, _loginLogic.Shows, "复用重跑 OnShow（生命週期口径）");
        }

        // ---- 语言切换 ----

        [UnityTest]
        public IEnumerator 语言切换_静态文案重写且OnShow不重跑()
        {
            var open = _ui.ShowAsync(PageLogin);
            yield return Wait(open, 20f);
            UIForm form = open.GetAwaiter().GetResult();
            Assert.IsNotNull(form);

            TMP_Text title = FindText(form.Root, "Txt_Title");
            Assert.IsNotNull(title, "Page_Login 的静态文案节点须在（LTextKeyRef 试点）");
            string zhText = title.text;
            int showsBefore = _loginLogic.Shows;

            _locale.SetLocale("en");                      // 触发 OnLocaleChanged——只重写绑定，不重跑 OnShow
            yield return Frames(2);

            Assert.AreNotEqual(zhText, title.text, "静态文案须换语言值");
            StringAssert.Contains("English Title", title.text, "写入英文值（服务单源）");
            Assert.AreEqual(showsBefore, _loginLogic.Shows, "语言切换不得重跑 OnShow（不重发业务请求）");

            _locale.SetLocale("zh-CN");
            yield return Frames(2);
            Assert.AreEqual(zhText, title.text, "切回中文恢复源语言值");
        }

        // ---- 模态射线穿透 ----

        [UnityTest]
        public IEnumerator 模态遮挡_顶模态打开期间底层blocksRaycasts关闭_关闭恢复()
        {
            var openBase = _ui.ShowAsync(PageLogin);
            yield return Wait(openBase, 20f);
            UIForm baseForm = openBase.GetAwaiter().GetResult();
            Assert.IsNotNull(baseForm);
            Assert.IsTrue(baseForm.CanvasGroup.blocksRaycasts, "无模态时底层页面可命中");

            _ui.RegisterModal(Dialog);
            var openDialog = _ui.ShowAsync(Dialog);
            yield return Wait(openDialog, 20f);
            UIForm dialogForm = openDialog.GetAwaiter().GetResult();
            Assert.IsNotNull(dialogForm, "模态打开须成功");
            yield return Frames(2);                       // 遮蔽重算随打开管线落地

            Assert.IsFalse(baseForm.CanvasGroup.blocksRaycasts,
                "顶层模态打开期间，视觉下方页面 blocksRaycasts=false（射线打不到底层）");
            Assert.IsTrue(dialogForm.CanvasGroup.blocksRaycasts, "顶模态自身保持可命中");

            var closeDialog = _ui.CloseAsync(Dialog);
            yield return Wait(closeDialog, 20f);
            yield return Frames(2);
            Assert.IsTrue(baseForm.CanvasGroup.blocksRaycasts, "模态关闭后底层页面命中恢复");
        }

        // ---- 辅助 ----

        private static TMP_Text FindText(GameObject root, string nodeName)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == nodeName) return text;
            return null;
        }

        private static async UniTask<IUIPrefabLease> LoadLeaseAsync(string location, CancellationToken ct)
        {
            var go = await AssetService.LoadAssetAsync<GameObject>(location, ct);
            if (go == null) throw new InvalidOperationException($"真资源加载失败:{location}");
            return new PlainLease(go);
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static IEnumerator Wait(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
