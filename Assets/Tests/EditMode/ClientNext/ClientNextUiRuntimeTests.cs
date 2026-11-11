using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// Next 页面运行时行为：打开/关闭/栈/Replace/Back、模态与输入阻断、缓存策略与预算、展示代次/取消、
    /// PageBase 绑定与错误契约；页面全部由代码创建，不依赖 prefab。
    /// </summary>
    public sealed class ClientNextUiRuntimeTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private UiRuntime _runtime;
        private Func<PageSpec, TestPage> _buildPage;
        private int _factoryCalls;

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            _buildPage = null;
            _factoryCalls = 0;
            _runtime = new UiRuntime(CreatePageAsync);
            _runtime.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _runtime?.Shutdown();
            for (int index = 0; index < _objects.Count; index++)
                if (_objects[index] != null) UnityEngine.Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [Test]
        public void 打开页面_创建非激活_注入后激活入栈再OnShow()
        {
            TestPage page = null;
            bool inactiveDuringBind = false;
            bool handleNullDuringBind = false;
            int createCountDuringBind = -1;
            bool activeDuringShow = false;
            bool handlePresentDuringShow = false;
            int openCountDuringShow = -1;

            PageHandle handle = _runtime.OpenAsync<TestPage>(Spec("Page_Order"), bound =>
            {
                page = bound;
                inactiveDuringBind = !bound.gameObject.activeSelf;
                handleNullDuringBind = bound.Handle == null;
                createCountDuringBind = bound.CreateCount;
                bound.ShowHook = () =>
                {
                    activeDuringShow = bound.gameObject.activeSelf;
                    handlePresentDuringShow = bound.Handle != null;
                    openCountDuringShow = _runtime.OpenPageCount;
                };
            }).GetAwaiter().GetResult();

            Assert.IsNotNull(page);
            Assert.IsTrue(inactiveDuringBind, "bind 阶段页面必须保持非激活（先构造后激活）");
            Assert.IsTrue(handleNullDuringBind, "句柄属激活阶段注入，bind 阶段不应存在");
            Assert.AreEqual(1, createCountDuringBind, "OnCreate（含 BindName 扫描）必须先于 bind");
            Assert.IsTrue(activeDuringShow, "OnShow 时页面必须已激活");
            Assert.IsTrue(handlePresentDuringShow, "OnShow 时句柄必须已注入");
            Assert.AreEqual(1, openCountDuringShow, "OnShow 时页面必须已入栈");
            Assert.AreEqual(1, page.CreateCount);
            Assert.AreEqual(1, page.ShowCount);
            Assert.AreSame(page, handle.Page);
            Assert.AreEqual("Page_Order", handle.Spec.Address);
            Assert.AreSame(handle, _runtime.Top);
            Assert.AreEqual(1, _runtime.OpenPageCount);
            Assert.AreEqual(1, _factoryCalls);
        }

        [Test]
        public void 关闭页面_触发OnHide并取消令牌_重复关闭为空操作()
        {
            PageHandle handle = Open(Spec("Page_A"));
            var page = (TestPage)handle.Page;
            CancellationToken token = handle.Lifetime.Token;

            _runtime.CloseAsync(handle).GetAwaiter().GetResult();

            Assert.IsTrue(handle.IsClosed);
            Assert.IsTrue(token.IsCancellationRequested, "关闭页面必须取消页面级 Lifetime");
            Assert.AreEqual(1, page.HideCount);
            Assert.AreEqual(0, _runtime.OpenPageCount);
            Assert.IsNull(_runtime.Top);
            Assert.IsFalse(_runtime.IsInputBlocked);
            Assert.IsFalse(page.gameObject.activeSelf, "缓存页关闭后必须隐藏 GameObject（不能只 Hide）");

            _runtime.CloseAsync(handle).GetAwaiter().GetResult();
            handle.CloseAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, page.HideCount, "重复关闭不得重复触发 OnHide");
        }

        [Test]
        public void 重复打开同一地址抛异常()
        {
            Open(Spec("Page_A"));
            Assert.Throws<InvalidOperationException>(() => Open(Spec("Page_A")));
            Assert.AreEqual(1, _runtime.OpenPageCount);
            Assert.AreEqual(1, _factoryCalls);
        }

        [Test]
        public void Back优先关闭系统页再模态页最后普通页()
        {
            PageHandle screen = Open(Spec("Page_Screen", PageLayer.Screen, true));
            Assert.IsFalse(Back(), "仅剩根页面时 Back 不关页");

            PageHandle overlay = Open(Spec("Page_Overlay", PageLayer.Overlay, false));
            Assert.IsTrue(Back());
            Assert.IsTrue(overlay.IsClosed);
            Assert.AreEqual(1, _runtime.OpenPageCount);

            PageHandle modal = Open(Spec("Page_Modal", PageLayer.Modal, false));
            PageHandle screen2 = Open(Spec("Page_Screen2", PageLayer.Screen, true));
            PageHandle system = Open(Spec("Page_System", PageLayer.System, false));

            Assert.IsTrue(Back(), "系统页优先");
            Assert.IsTrue(system.IsClosed);
            Assert.IsFalse(modal.IsClosed);
            Assert.IsFalse(screen2.IsClosed);

            Assert.IsTrue(Back(), "其次模态页");
            Assert.IsTrue(modal.IsClosed);
            Assert.IsFalse(screen2.IsClosed);

            Assert.IsTrue(Back(), "最后关闭栈顶普通页");
            Assert.IsTrue(screen2.IsClosed);
            Assert.AreEqual(1, _runtime.OpenPageCount);
            Assert.IsFalse(screen.IsClosed);

            Assert.IsFalse(Back());
        }

        [Test]
        public void Replace关闭栈顶页面并打开新页()
        {
            PageHandle first = Open(Spec("Page_A"));
            PageHandle second = Open(Spec("Page_B"));

            PageHandle replaced = _runtime.ReplaceAsync<TestPage>(Spec("Page_C")).GetAwaiter().GetResult();

            Assert.IsTrue(second.IsClosed);
            Assert.IsFalse(first.IsClosed);
            Assert.IsFalse(replaced.IsClosed);
            Assert.AreEqual(2, _runtime.OpenPageCount);
            Assert.AreSame(replaced, _runtime.Top);
            Assert.AreEqual(1, ((TestPage)second.Page).HideCount);
            Assert.AreEqual(1, ((TestPage)replaced.Page).ShowCount);
        }

        [Test]
        public void 模态与全屏页计入输入阻断_关闭后回落()
        {
            Assert.IsFalse(_runtime.IsInputBlocked);
            Assert.AreEqual(0, _runtime.InputBlockCount);

            PageHandle screen = Open(Spec("Page_Screen", PageLayer.Screen, true));
            Assert.IsTrue(_runtime.IsInputBlocked);
            Assert.AreEqual(1, _runtime.InputBlockCount);

            PageHandle modal = Open(Spec("Page_Modal", PageLayer.Modal, false));
            Assert.AreEqual(2, _runtime.InputBlockCount);

            PageHandle overlay = Open(Spec("Page_Overlay", PageLayer.Overlay, false));
            Assert.AreEqual(2, _runtime.InputBlockCount, "非全屏浮层不阻断输入");

            Close(overlay);
            Assert.AreEqual(2, _runtime.InputBlockCount);
            Close(modal);
            Assert.AreEqual(1, _runtime.InputBlockCount);
            Assert.IsTrue(_runtime.IsInputBlocked);
            Close(screen);
            Assert.AreEqual(0, _runtime.InputBlockCount);
            Assert.IsFalse(_runtime.IsInputBlocked);
        }

        [Test]
        public void 缓存Lru复用实例_Resident常驻_DestroyOnClose销毁()
        {
            PageSpec lru = Spec("Page_Lru", PageLayer.Screen, true, PageCachePolicy.Lru);
            PageHandle lruHost = Open(lru);
            var lruPage = (TestPage)lruHost.Page;
            Close(lruHost);
            Assert.AreEqual(1, _runtime.CachedPageCount);

            PageHandle lruAgain = Open(lru);
            Assert.AreSame(lruPage, lruAgain.Page, "Lru 页面关闭后应复用实例");
            Assert.AreEqual(1, _factoryCalls);
            Close(lruAgain);

            PageSpec resident = Spec("Page_Resident", PageLayer.Screen, true, PageCachePolicy.Resident);
            PageHandle residentHost = Open(resident);
            var residentPage = (TestPage)residentHost.Page;
            Close(residentHost);
            PageHandle residentAgain = Open(resident);
            Assert.AreSame(residentPage, residentAgain.Page, "Resident 页面应复用实例");
            Assert.AreEqual(2, _factoryCalls);
            Close(residentAgain);

            PageSpec destroy = Spec("Page_Destroy", PageLayer.Screen, true, PageCachePolicy.DestroyOnClose);
            PageHandle destroyHost = Open(destroy);
            var destroyPage = (TestPage)destroyHost.Page;
            int cachedBefore = _runtime.CachedPageCount;
            Close(destroyHost);
            Assert.IsTrue(destroyPage == null, "DestroyOnClose 页面关闭即销毁");
            Assert.AreEqual(cachedBefore, _runtime.CachedPageCount, "DestroyOnClose 页面不进缓存");

            Open(destroy);
            Assert.AreEqual(4, _factoryCalls, "DestroyOnClose 页面重开必须重新创建");
        }

        [Test]
        public void 缓存预算超出时淘汰最久未用的Lru页面()
        {
            var runtime = new UiRuntime(CreatePageAsync, 1);
            runtime.Initialize();
            try
            {
                PageHandle residentHost = runtime.OpenAsync<TestPage>(
                    Spec("Page_Resident", PageLayer.Screen, false, PageCachePolicy.Resident)).GetAwaiter().GetResult();
                var residentPage = (TestPage)residentHost.Page;
                runtime.CloseAsync(residentHost).GetAwaiter().GetResult();

                PageHandle lruHost = runtime.OpenAsync<TestPage>(
                    Spec("Page_Lru", PageLayer.Screen, false, PageCachePolicy.Lru)).GetAwaiter().GetResult();
                var lruPage = (TestPage)lruHost.Page;
                runtime.CloseAsync(lruHost).GetAwaiter().GetResult();

                Assert.AreEqual(1, runtime.CachedPageCount, "超出预算时只淘汰 Lru 缓存页");
                Assert.IsTrue(lruPage == null, "被淘汰的缓存页面必须销毁");
                Assert.AreEqual(2, _factoryCalls);

                PageHandle residentAgain = runtime.OpenAsync<TestPage>(
                    Spec("Page_Resident", PageLayer.Screen, false, PageCachePolicy.Resident)).GetAwaiter().GetResult();
                Assert.AreSame(residentPage, residentAgain.Page, "Resident 页面不参与淘汰");
                Assert.AreEqual(2, _factoryCalls);
            }
            finally
            {
                runtime.Shutdown();
            }
        }

        [Test]
        public void 复用缓存页面时展示代次递增_旧代次不再当前()
        {
            PageSpec lru = Spec("Page_Lru");
            PageHandle first = Open(lru);
            int firstGeneration = first.DisplayGeneration;
            CancellationToken firstToken = first.Lifetime.Token;
            Assert.IsTrue(first.IsCurrent(firstGeneration));

            Close(first);
            Assert.IsTrue(first.IsClosed, "关闭后句柄必须标记为已关闭");
            Assert.IsFalse(first.IsCurrent(firstGeneration));
            Assert.IsTrue(firstToken.IsCancellationRequested);

            PageHandle second = Open(lru);
            Assert.AreSame(first.Page, second.Page);
            Assert.Greater(second.DisplayGeneration, firstGeneration);
            Assert.IsTrue(second.IsCurrent(second.DisplayGeneration));
            Assert.IsFalse(second.IsCurrent(firstGeneration));
            Assert.IsFalse(second.Lifetime.Token.IsCancellationRequested);
        }

        [Test]
        public void 页面绑定_按BindName取控件并可SetText_SetVisible_SetInteractable()
        {
            TestPage page = NewPage("Page_Bind");
            Button button = AddChild<Button>(page.transform, "Btn_Ok", "Btn_Ok");
            GameObject icon = AddChild(page.transform, "Img_Icon", "Img_Icon");
            TextMeshProUGUI text = AddChild<TextMeshProUGUI>(page.transform, "Txt_Title", "Txt_Title");

            Assert.AreSame(button, page.BindControl<Button>("Btn_Ok"));
            page.SetTextValue("Txt_Title", "登录");
            Assert.AreEqual("登录", text.text);
            page.SetVisibleValue("Img_Icon", false);
            Assert.IsFalse(icon.activeSelf);
            page.SetInteractableValue("Btn_Ok", false);
            Assert.IsFalse(button.interactable);
        }

        [Test]
        public void 页面绑定_缺控件抛异常()
        {
            TestPage page = NewPage("Page_Bind");
            var error = Assert.Throws<InvalidOperationException>(() => page.BindControl<Button>("Missing"));
            StringAssert.Contains("缺少绑定控件", error.Message);
        }

        [Test]
        public void 页面绑定_BindName重名抛异常()
        {
            TestPage page = NewPage("Page_Dup");
            AddChild<Button>(page.transform, "Btn_A", "Dup");
            AddChild<Button>(page.transform, "Btn_B", "Dup");

            var error = Assert.Throws<InvalidOperationException>(() => page.BindControl<Button>("Dup"));
            StringAssert.Contains("重名", error.Message);
        }

        [Test]
        public void 页面关闭后按钮点击解绑()
        {
            int clicks = 0;
            Button button = null;
            _buildPage = spec =>
            {
                TestPage page = NewPage("Page_Click");
                button = AddChild<Button>(page.transform, "Btn_Ok", "Btn_Ok");
                page.ShowHook = () => page.BindClick("Btn_Ok", () => clicks++);
                return page;
            };

            PageHandle handle = Open(Spec("Page_Click"));
            Assert.IsNotNull(button);
            button.onClick.Invoke();
            Assert.AreEqual(1, clicks);

            Close(handle);
            button.onClick.Invoke();
            Assert.AreEqual(1, clicks, "关闭页面必须解绑点击监听");
        }

        [Test]
        public void Tick推进时间与转场窗口()
        {
            PageHandle handle = Open(Spec("Page_Tick"));
            var page = (TestPage)handle.Page;
            Assert.IsTrue(_runtime.IsTransitioning, "打开页面进入转场窗口");

            _runtime.Tick(0.05f);
            Assert.AreEqual(0.05f, _runtime.ElapsedSeconds, 1e-4f);
            Assert.Greater(page.TickCount, 0, "Tick 必须转发到打开页面的 OnTick");
            Assert.IsTrue(_runtime.IsTransitioning);

            _runtime.Tick(1f);
            Assert.AreEqual(1.05f, _runtime.ElapsedSeconds, 1e-4f);
            Assert.IsFalse(_runtime.IsTransitioning, "转场窗口由 Tick 推进到结束");

            _runtime.Tick(0f);
            _runtime.Tick(-1f);
            Assert.AreEqual(1.05f, _runtime.ElapsedSeconds, 1e-4f);
        }

        [Test]
        public void Shutdown关闭全部页面并释放缓存()
        {
            PageHandle lru = Open(Spec("Page_Lru", PageLayer.Screen, true, PageCachePolicy.Lru));
            PageHandle destroy = Open(Spec("Page_Close", PageLayer.Screen, true, PageCachePolicy.DestroyOnClose));
            Assert.AreEqual(2, _runtime.OpenPageCount);

            _runtime.Shutdown();

            Assert.IsTrue(lru.IsClosed);
            Assert.IsTrue(destroy.IsClosed);
            Assert.AreEqual(0, _runtime.OpenPageCount);
            Assert.AreEqual(0, _runtime.CachedPageCount);
            Assert.AreEqual(0, _runtime.InputBlockCount);
            Assert.IsFalse(_runtime.IsInputBlocked);
            Assert.IsFalse(_runtime.IsInitialized);
            Assert.IsTrue(lru.Page == null, "Shutdown 必须销毁缓存页面");
        }

        [Test]
        public void 未初始化时打开页面抛异常()
        {
            var runtime = new UiRuntime(CreatePageAsync);
            Assert.Throws<InvalidOperationException>(
                () => runtime.OpenAsync<TestPage>(Spec("Page_X")).GetAwaiter().GetResult());
        }

        private UniTask<PageBase> CreatePageAsync(PageSpec spec, CancellationToken cancellationToken)
        {
            _factoryCalls++;
            TestPage page = _buildPage != null ? _buildPage(spec) : NewPage("Page_" + spec.Address);
            return UniTask.FromResult<PageBase>(page);
        }

        private TestPage NewPage(string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            _objects.Add(go);
            return go.AddComponent<TestPage>();
        }

        private static GameObject AddChild(Transform parent, string goName, string bindName)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            var bind = go.AddComponent<BindName>();
            bind.Name = bindName;
            return go;
        }

        private static T AddChild<T>(Transform parent, string goName, string bindName) where T : Component
        {
            GameObject go = AddChild(parent, goName, bindName);
            return go.AddComponent<T>();
        }

        private PageHandle Open(PageSpec spec) => _runtime.OpenAsync<TestPage>(spec).GetAwaiter().GetResult();

        private void Close(PageHandle handle) => _runtime.CloseAsync(handle).GetAwaiter().GetResult();

        private bool Back() => _runtime.BackAsync().GetAwaiter().GetResult();

        private static PageSpec Spec(string address) => new PageSpec(address);

        private static PageSpec Spec(string address, PageLayer layer, bool fullScreen,
            PageCachePolicy policy = PageCachePolicy.Lru) => new PageSpec(address, layer, fullScreen, policy);
    }

    /// <summary>测试页面：记录生命周期调用次数，并暴露 PageBase 的 protected 绑定面给用例。</summary>
    public sealed class TestPage : PageBase
    {
        /// <summary>展示钩子（在 OnShow 内执行，用于断言激活/入栈时序）。</summary>
        public Action ShowHook;

        /// <summary>OnCreate 调用次数。</summary>
        public int CreateCount { get; private set; }

        /// <summary>OnShow 调用次数。</summary>
        public int ShowCount { get; private set; }

        /// <summary>OnHide 调用次数。</summary>
        public int HideCount { get; private set; }

        /// <summary>OnDispose 调用次数。</summary>
        public int DisposeCount { get; private set; }

        /// <summary>OnTick 调用次数。</summary>
        public int TickCount { get; private set; }

        /// <summary>按名取控件（转调 Bind&lt;T&gt;）。</summary>
        public T BindControl<T>(string name) where T : Component => Bind<T>(name);

        /// <summary>登记按钮点击（转调 OnClick）。</summary>
        public void BindClick(string name, Action handler) => OnClick(name, handler);

        /// <summary>设置文本（转调 SetText）。</summary>
        public void SetTextValue(string name, string value) => SetText(name, value);

        /// <summary>设置可见性（转调 SetVisible）。</summary>
        public void SetVisibleValue(string name, bool visible) => SetVisible(name, visible);

        /// <summary>设置可交互（转调 SetInteractable）。</summary>
        public void SetInteractableValue(string name, bool interactable) => SetInteractable(name, interactable);

        protected override void OnCreate() => CreateCount++;

        protected override void OnShow()
        {
            ShowCount++;
            ShowHook?.Invoke();
        }

        protected override void OnHide() => HideCount++;

        protected override void OnDispose() => DisposeCount++;

        protected override void OnTick(float deltaTime) => TickCount++;
    }
}
