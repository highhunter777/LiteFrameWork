using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 页面生命周期 PlayMode 用例（真页面逻辑载体＝C# 逻辑探针；语义保留：
    /// 冷开 OnShow 一次、复用不重跑 OnInit、Shutdown 释放租约、在途取消类型化）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class CSharpPagePlayModeTests
    {
        private const int TestPageFormId = 9001;
        private const string TestPageLocation = "Assets/LiteGame/Modules/UI/Content/Screens/Tmp_UITestPage.prefab";

        /// <summary>生命周期探针（页面逻辑＝C# 对象替身）。</summary>
        private sealed class ProbeLogic : IUIFormLogic
        {
            public int Inits;
            public int Shows;

            public void OnInit(UIForm form, IUIData data) => Inits++;
            public void OnShow(IUIData data) => Shows++;
            public void OnUpdate(float deltaTime) { }
            public void OnPause() { }
            public void OnCover() { }
            public void OnReveal() { }
            public void OnHide() { }
        }

        private sealed class StaticCatalog : IPageCatalog
        {
            private readonly PageDefinition[] _rows;
            public StaticCatalog(PageDefinition[] rows) => _rows = rows;
            public PageDefinition Get(int id)
            {
                foreach (var row in _rows) if (row.Id == id) return row;
                throw new KeyNotFoundException($"StaticCatalog 无行:{id}");
            }
        }

        /// <summary>租约替身只做引用计数（真释放语义），prefab 本身走真资源链路。</summary>
        private sealed class CountingLease : IUIPrefabLease
        {
            public static int Acquired;
            public static int Released;

            private readonly GameObject _prefab;
            public CountingLease(GameObject prefab) { _prefab = prefab; Acquired++; }
            public GameObject Prefab => _prefab;
            public void Release() { Released++; }
        }

        private PlayModeTestScope _scope;
        private ProbeLogic _probe;
        private UIService _ui;
        private UINavigationController _nav;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(CSharpPagePlayModeTests));
            CountingLease.Acquired = 0;
            CountingLease.Released = 0;

            if (!_assetsReady)
            {
                var init = TestContent.InitAsync();
                yield return WaitUniTask(init, 60f);
                Assert.IsFalse(init.Status == UniTaskStatus.Pending, "资源包初始化超时（60s）");
                _assetsReady = true;
            }

            _probe = new ProbeLogic();
            _ui = new UIService(
                new StaticCatalog(new[]
                {
                    new PageDefinition { Id = TestPageFormId, Layer = 0, FullScreen = true, Location = TestPageLocation },
                }),
                logicResolver: _ => _probe,
                loadPrefab: LoadRealPrefabAsync,
                transitionStrategy: new FadeSlideTransition());
            _nav = new UINavigationController(_ui);

            var host = _scope.CreateGameObject("PlayModeHost");
            PlayModeTicker.Attach(host, _ui.Tick);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_ui != null)
            {
                var shutdown = _ui.ShutdownAsync();
                yield return WaitUniTask(shutdown, 20f);
                _ui = null;
            }
            yield return _scope.DisposeAsync();
        }

        [UnityTest]
        public IEnumerator 真CSharp页面_冷开_OnShow执行且实例与租约各一次()
        {
            var open = _ui.ShowAsync(TestPageFormId);
            yield return WaitUniTask(open, 20f);
            Assert.IsFalse(open.Status == UniTaskStatus.Pending, "测试载体冷开超时（真 prefab + 真转场）");

            UIForm form = open.GetAwaiter().GetResult();
            Assert.IsNotNull(form, "冷开返回实例");
            Assert.AreEqual(1, _probe.Inits, "OnInit 一次");
            Assert.AreEqual(1, _probe.Shows, "OnShow 恰好一次");
            Assert.AreEqual(1, CountingLease.Acquired, "冷开获取租约一次");
            Assert.AreEqual(0, CountingLease.Released, "销毁前不得释放（租约覆盖缓存实例寿命，§5.2）");
        }

        [UnityTest]
        public IEnumerator 真CSharp页面_关闭后复用_不重复OnInit但重跑OnShow()
        {
            var open = _ui.ShowAsync(TestPageFormId);
            yield return WaitUniTask(open, 20f);
            UIForm first = open.GetAwaiter().GetResult();
            Assert.AreEqual(1, _probe.Shows, "首开 OnShow 一次");

            var close = _ui.CloseAsync(TestPageFormId, UIService.CloseReason.User);
            yield return WaitUniTask(close, 20f);
            Assert.IsFalse(_ui.IsOpen(TestPageFormId), "已关闭");

            var reopen = _ui.ShowAsync(TestPageFormId);
            yield return WaitUniTask(reopen, 20f);
            Assert.IsFalse(reopen.Status == UniTaskStatus.Pending, "复用打开超时");
            UIForm second = reopen.GetAwaiter().GetResult();

            Assert.AreSame(first, second, "复用同一实例（Created 后 Reused，不是新建）");
            Assert.AreEqual(1, _probe.Inits, "复用不重跑 OnInit");
            Assert.AreEqual(2, _probe.Shows, "复用重跑 OnShow（§4.2 首次与复用合流）");
            Assert.AreEqual(1, CountingLease.Acquired, "复用不再获取第二份租约");
        }

        [UnityTest]
        public IEnumerator 真CSharp页面_Shutdown_释放租约且旧实例不可访问()
        {
            var open = _ui.ShowAsync(TestPageFormId);
            yield return WaitUniTask(open, 20f);
            Assert.AreEqual(1, CountingLease.Acquired);

            var shutdown = _ui.ShutdownAsync();
            yield return WaitUniTask(shutdown, 20f);
            Assert.IsFalse(shutdown.Status == UniTaskStatus.Pending, "Shutdown 超时（§4.4 全链释放）");

            Assert.AreEqual(1, CountingLease.Released, "Shutdown 释放租约（引用归零）");
            Assert.IsFalse(_ui.IsOpen(TestPageFormId), "旧实例不再打开");
        }

        [UnityTest]
        public IEnumerator 真CSharp页面_打开中关闭_在途被权威取消且不残留()
        {
            var open = _ui.ShowAsync(TestPageFormId);
            Assert.AreEqual(UniTaskStatus.Pending, open.Status, "首帧应处于在途");

            var close = _ui.CloseAsync(TestPageFormId, UIService.CloseReason.User);
            yield return WaitUniTask(close, 20f);
            yield return WaitUniTask(open, 20f);

            Assert.AreNotEqual(UniTaskStatus.Pending, open.Status, "在途 Show 必须收到终态（不悬挂）");
            Assert.IsFalse(_ui.IsOpen(TestPageFormId), "权威取消后页面不得残留打开态");

            var ex = Assert.Throws<UIOpenException>(() => open.GetAwaiter().GetResult(),
                "取消应交付 UIOpenException 而非悬挂/静默");
            Assert.AreEqual(UIOpenFailure.Canceled, ex.Reason, "失败分类为 Canceled");

            Assert.AreEqual(CountingLease.Acquired, CountingLease.Released,
                "取消路径租约对称（获取即释放，不留半成品）");
            Assert.AreEqual(0, _probe.Shows, "取消路径不得跑过 OnShow（页面从未完成展示）");
        }

        private static async UniTask<IUIPrefabLease> LoadRealPrefabAsync(string location, CancellationToken ct)
        {
            // 让出一帧再加载：把"在途窗口"变成确定性的，取消用例才测得到取消语义。
            await UniTask.Yield(ct);
            GameObject prefab = await TestContent.LoadAssetAsync<GameObject>(location, ct);
            if (prefab == null) throw new InvalidOperationException($"真资源加载失败:{location}");
            return new CountingLease(prefab);
        }

        private static IEnumerator WaitUniTask(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
