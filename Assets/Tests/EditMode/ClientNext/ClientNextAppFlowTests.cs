using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Features.Account.Content;
using LiteGameNext.Platform.Ui;
using LiteGameNext.Platform.Ui.Content;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// T4 顶层流程与关闭契约：AppFlow 假登录/页面关闭回登录/取消静默退出/顶层错误页，
    /// 以及 AOT Lifetime 的逆序释放、异常聚合与已释放登记；页面全部用代码创建，不依赖 prefab。
    /// </summary>
    public sealed class ClientNextAppFlowTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly Dictionary<string, PageBase> _pages = new Dictionary<string, PageBase>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _factoryCalls = new Dictionary<string, int>(StringComparer.Ordinal);

        private UiRuntime _ui;
        private AccountFeature _account;
        private Button _guestButton;
        private TMP_Text _errorTitle;
        private TMP_Text _errorMessage;
        private Button _retryButton;
        private Button _backLoginButton;
        private string _failAddress;

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            _pages.Clear();
            _factoryCalls.Clear();
            _guestButton = null;
            _errorTitle = null;
            _errorMessage = null;
            _retryButton = null;
            _backLoginButton = null;
            _failAddress = null;

            _ui = new UiRuntime(CreatePageAsync);
            _ui.Initialize();
            _account = new AccountFeature(_ui);
            _account.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _ui?.Shutdown();
            for (int index = 0; index < _objects.Count; index++)
                if (_objects[index] != null) UnityEngine.Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [Test]
        public void 假登录后登录页关闭且占位页打开()
        {
            var cts = new CancellationTokenSource();
            UniTask run = new AppFlow(_ui, _account).RunAsync(cts.Token);

            var login = (LoginPage)_pages[LoginPage.Spec.Address];
            Assert.AreSame(login, _ui.Top.Page, "流程第一步必须打开登录页");
            Assert.AreEqual(1, _ui.OpenPageCount);
            Assert.IsFalse(_account.IsSignedIn);

            _guestButton.onClick.Invoke();

            Assert.IsTrue(_account.IsSignedIn, "游客登录必须置登录态");
            Assert.IsInstanceOf<PlaceholderPage>(_ui.Top.Page, "登录成功后必须打开占位页");
            Assert.AreEqual(1, _ui.OpenPageCount, "登录页必须关闭：栈上只剩占位页");
            Assert.IsFalse(login.gameObject.activeSelf, "关闭的登录页必须离开屏幕");
            Assert.AreEqual(UniTaskStatus.Pending, run.Status, "流程应在占位页等待取消（不自行结束）");

            cts.Cancel();
            Assert.AreEqual(UniTaskStatus.Succeeded, run.Status, "App 取消后流程必须静默结束");
            run.GetAwaiter().GetResult();
            cts.Dispose();
        }

        [Test]
        public void 已登录时跳过登录页直接进占位页()
        {
            _account.SignInAsGuest();
            using var cts = new CancellationTokenSource();

            UniTask run = new AppFlow(_ui, _account).RunAsync(cts.Token);

            Assert.IsInstanceOf<PlaceholderPage>(_ui.Top.Page, "已登录应直接进占位页");
            Assert.IsFalse(_factoryCalls.ContainsKey(LoginPage.Spec.Address), "已登录不应装载登录页");

            cts.Cancel();
            Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void 登录页被关闭时回到登录页且不悬挂()
        {
            using var cts = new CancellationTokenSource();
            UniTask run = new AppFlow(_ui, _account).RunAsync(cts.Token);

            var login = (LoginPage)_pages[LoginPage.Spec.Address];
            PageHandle handle = login.Handle;
            Assert.IsNotNull(handle);
            _ui.CloseAsync(handle).GetAwaiter().GetResult();

            Assert.AreEqual(UniTaskStatus.Pending, run.Status, "关闭登录页不应结束流程");
            Assert.IsInstanceOf<LoginPage>(_ui.Top.Page, "登录页关闭后必须回到登录页");
            Assert.AreEqual(1, _ui.OpenPageCount);
            Assert.IsTrue(login.gameObject.activeSelf);
            Assert.IsFalse(_account.IsSignedIn, "取消登录不得置登录态");

            _guestButton.onClick.Invoke();
            Assert.IsInstanceOf<PlaceholderPage>(_ui.Top.Page, "重开登录页后仍可完成登录");

            cts.Cancel();
            Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void 登录等待中取消App令牌_静默退出并关闭登录页()
        {
            using var cts = new CancellationTokenSource();
            UniTask run = new AppFlow(_ui, _account).RunAsync(cts.Token);
            var login = (LoginPage)_pages[LoginPage.Spec.Address];

            cts.Cancel();

            Assert.AreEqual(UniTaskStatus.Succeeded, run.Status, "App 取消是正常关闭，流程不得抛错或悬挂");
            run.GetAwaiter().GetResult();
            Assert.IsFalse(login.gameObject.activeSelf, "取消后登录页必须关闭");
            Assert.AreEqual(0, _ui.OpenPageCount);
            Assert.IsFalse(_account.IsSignedIn);
        }

        [Test]
        public void 顶层异常打开错误页并绑定标题消息()
        {
            _failAddress = LoginPage.Spec.Address;
            LogAssert.Expect(LogType.Error, new Regex("AppFlow 失败"));
            using var cts = new CancellationTokenSource();

            UniTask run = new AppFlow(_ui, _account).RunAsync(cts.Token);

            Assert.IsInstanceOf<ErrorPage>(_ui.Top.Page, "顶层异常必须打开错误页");
            Assert.AreEqual("启动失败", _errorTitle.text);
            StringAssert.Contains("登录页装载失败", _errorMessage.text);

            Assert.DoesNotThrow(() => _retryButton.onClick.Invoke(), "重试回调不得抛出");
            Assert.DoesNotThrow(() => _backLoginButton.onClick.Invoke(), "回登录回调不得抛出");
            Assert.AreEqual(UniTaskStatus.Pending, run.Status, "错误页期间流程等待取消");

            cts.Cancel();
            Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void Lifetime逆序释放并取消令牌()
        {
            var order = new List<string>();
            var lifetime = new Lifetime();
            lifetime.Add(new Resource("first", order));
            lifetime.Add(new Resource("second", order));
            CancellationToken token = lifetime.Token;

            lifetime.Dispose();

            CollectionAssert.AreEqual(new[] { "second", "first" }, order);
            Assert.IsTrue(lifetime.IsDisposed);
            Assert.IsTrue(token.IsCancellationRequested, "关闭必须取消令牌");
        }

        [Test]
        public void Lifetime异常聚合且后续资源仍释放()
        {
            var order = new List<string>();
            var lifetime = new Lifetime();
            lifetime.Add(new Resource("first", order));
            lifetime.Add(new Resource("second", order));
            lifetime.Add(new ThrowOnDispose());

            AggregateException error = Assert.Throws<AggregateException>(() => lifetime.Dispose());
            Assert.AreEqual(1, error.InnerExceptions.Count);
            Assert.IsInstanceOf<InvalidOperationException>(error.InnerExceptions[0]);
            CollectionAssert.AreEqual(new[] { "second", "first" }, order, "一个资源抛错不得跳过其余资源");
            Assert.IsTrue(lifetime.IsDisposed);
        }

        [Test]
        public void Lifetime已释放后Add抛ObjectDisposedException并释放传入资源()
        {
            var order = new List<string>();
            var lifetime = new Lifetime();
            lifetime.Dispose();

            var late = new Resource("late", order);
            Assert.Throws<ObjectDisposedException>(() => lifetime.Add(late));
            Assert.IsTrue(late.Disposed, "已关闭后的登记必须立即释放该资源");
        }

        private UniTask<PageBase> CreatePageAsync(PageSpec spec, CancellationToken cancellationToken)
        {
            _factoryCalls.TryGetValue(spec.Address, out int calls);
            _factoryCalls[spec.Address] = calls + 1;

            if (_failAddress == spec.Address) throw new InvalidOperationException("登录页装载失败（夹具）");

            PageBase page = BuildPage(spec);
            _pages[spec.Address] = page;
            return UniTask.FromResult(page);
        }

        private PageBase BuildPage(PageSpec spec)
        {
            if (spec.Address == LoginPage.Spec.Address)
            {
                LoginPage login = NewPage<LoginPage>("LoginPage(Clone)");
                AddText(login.transform, "Txt_Title", "Txt_Title");
                AddText(login.transform, "Txt_Status", "Txt_Status");
                _guestButton = AddButton(login.transform, "Btn_GuestLogin", "Btn_GuestLogin");
                return login;
            }

            if (spec.Address == PlaceholderPage.Spec.Address)
            {
                PlaceholderPage placeholder = NewPage<PlaceholderPage>("PlaceholderPage(Clone)");
                AddText(placeholder.transform, "Txt_Title", "Txt_Title");
                AddText(placeholder.transform, "Txt_Hint", "Txt_Hint");
                return placeholder;
            }

            if (spec.Address == ErrorPage.Spec.Address)
            {
                ErrorPage error = NewPage<ErrorPage>("ErrorPage(Clone)");
                _errorTitle = AddText(error.transform, "Txt_Title", "Txt_Title");
                _errorMessage = AddText(error.transform, "Txt_Message", "Txt_Message");
                _retryButton = AddButton(error.transform, "Btn_Retry", "Btn_Retry");
                _backLoginButton = AddButton(error.transform, "Btn_BackLogin", "Btn_BackLogin");
                return error;
            }

            throw new InvalidOperationException("无用例页面：" + spec.Address);
        }

        private T NewPage<T>(string name) where T : PageBase
        {
            var go = new GameObject(name);
            go.SetActive(false);
            _objects.Add(go);
            return go.AddComponent<T>();
        }

        private static GameObject AddChild(Transform parent, string goName, string bindName)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            var bind = go.AddComponent<BindName>();
            bind.Name = bindName;
            return go;
        }

        private static TMP_Text AddText(Transform parent, string goName, string bindName)
            => AddChild(parent, goName, bindName).AddComponent<TextMeshProUGUI>();

        private static Button AddButton(Transform parent, string goName, string bindName)
            => AddChild(parent, goName, bindName).AddComponent<Button>();

        private sealed class Resource : IDisposable
        {
            private readonly string _name;
            private readonly List<string> _order;

            public Resource(string name, List<string> order)
            {
                _name = name;
                _order = order;
            }

            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
                _order.Add(_name);
            }
        }

        private sealed class ThrowOnDispose : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("释放失败（夹具）");
        }
    }
}
