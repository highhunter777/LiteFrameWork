using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// UI U1-① 操作记录与取消验收（《UI框架总设计》§4.3——UI-03）：
    /// 并发 Show 合流共享一次加载（不返回 null）；数据冲突 Busy；调用方 token 只取消本人等待、
    /// 全员退出撤工作；Close 对在途打开发权威取消；失败类型化（UIOpenException）；
    /// 展示代次（复用递增、迟到代次核验失败）与展示作用域 CTS（关闭即取消）；Tick 快照重入安全。
    /// 时序口径与 UiU0EditModeTests 相同（假策略 + 手动 Tick；UTCS 定向续延控制加载窗口）。
    /// </summary>
    public sealed class UiU1EditModeTests : UnityTestBase
    {
        [TearDown]
        protected void KillUiRoots()
        {
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                if (go.name == "[UIRoot]") UnityEngine.Object.DestroyImmediate(go);
        }

        // ---- 替身 ----

        private sealed class MimicTransition : ITransitionStrategy
        {
            public UniTask PlayShow(UIForm form) { form.CanvasGroup.alpha = 1f; return UniTask.CompletedTask; }
            public UniTask PlayClose(UIForm form) { form.CanvasGroup.alpha = 0f; return UniTask.CompletedTask; }
        }

        private sealed class RecordingLogic : IUIFormLogic
        {
            public int OnHideCount;
            public Action<float> OnUpdateHook;
            public void OnInit(UIForm form, IUIData data) { }
            public void OnShow(IUIData data) { }
            public void OnUpdate(float deltaTime) => OnUpdateHook?.Invoke(deltaTime);
            public void OnPause() { }
            public void OnCover() { }
            public void OnReveal() { }
            public void OnHide() => OnHideCount++;
        }

        private sealed class FakeCatalog : IUIFormCatalog
        {
            private readonly Dictionary<int, UIFormInfo> _rows = new Dictionary<int, UIFormInfo>();
            public void Add(int id, int layer = 1, bool fullScreen = false)
                => _rows[id] = new UIFormInfo { Id = id, Layer = layer, FullScreen = fullScreen, Location = "x" + id, LuaPath = "x" };
            public UIFormInfo Get(int id)
                => _rows.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"FakeCatalog 无行:{id}");
        }

        /// <summary>门控加载器：加载挂起在 UTCS 上（定向续延——测试控制"加载窗口期"）；
        /// 履行加载契约的取消语义（§4.3：ct 贯穿加载全程——权威取消时以 OCE 完成挂起任务）；
        /// 返回**租约**（U1-②：UIService 加载口契约为 IUIPrefabLease——Unowned 便于断言释放计数）。</summary>
        private sealed class GateLoader
        {
            public int Calls;
            public int Releases;
            public CancellationToken LastWorkCt;
            private UniTaskCompletionSource<IUIPrefabLease> _pending;
            private CancellationTokenRegistration _registration;

            public Func<string, CancellationToken, UniTask<IUIPrefabLease>> Loader => (loc, ct) =>
            {
                Calls++;
                LastWorkCt = ct;
                _pending = new UniTaskCompletionSource<IUIPrefabLease>();
                _registration.Dispose();
                _registration = ct.Register(() => _pending.TrySetException(new OperationCanceledException(ct)));
                return _pending.Task;
            };

            public void Deliver(IUIPrefabLease lease) => _pending?.TrySetResult(lease);
            public void Deliver(GameObject prefab) => Deliver(UIPrefabLeases.Unowned(prefab));
            public void Fail(Exception ex) => _pending?.TrySetException(ex);
        }

        /// <summary>计数租约：Release 次数可断言（U1-② 泄漏口径）。</summary>
        private sealed class CountingLease : IUIPrefabLease
        {
            public GameObject Prefab { get; }
            public int Releases;
            public CountingLease(GameObject prefab) => Prefab = prefab;
            public void Release() => Releases++;
        }

        private GameObject FakePrefab(string name)
            => Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

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

        private UIService Service(FakeCatalog catalog, GateLoader loader, RecordingLogic logic = null,
            IPopInterceptor pop = null, int cacheBudget = UIService.DefaultCacheBudget)
            => new UIService(catalog,
                transitionStrategy: new MimicTransition(),
                popInterceptor: pop,
                logicResolver: _ => logic ?? new RecordingLogic(),
                loadPrefab: loader.Loader,
                cacheBudget: cacheBudget);
        // ---- 并发合流（UI-03：不再返回 null）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 并发Show合流_共享一次加载_同数据两等待者同实例()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);

            var prefab = FakePrefab("p1");
            var t1 = service.ShowAsync(1);
            var t2 = service.ShowAsync(1);                  // 加载窗口期内的并发请求
            var t3 = service.ShowAsync(1);
            Assert.AreEqual(1, loader.Calls, "三次并发 Show 必须共享一次底层加载");

            loader.Deliver(prefab);
            var f1 = Await(t1, service);
            var f2 = Await(t2, service);
            var f3 = Await(t3, service);

            Assert.AreSame(f1, f2);
            Assert.AreSame(f1, f3);                          // 同一实例（没有 null、没有双实例）
            Await(service.CloseAsync(1), service);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 并发Show数据不同_首请求持有数据_后续Busy()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);
            var dataB = new FakeData();

            var t1 = service.ShowAsync(1, null);
            var ex = Assert.Throws<UIOpenException>(() => service.ShowAsync(1, dataB).GetAwaiter().GetResult());
            Assert.AreEqual(UIOpenFailure.Busy, ex.Reason, "数据冲突不静默覆盖——首请求持有数据（§4.3）");

            loader.Deliver(FakePrefab("p1"));
            Await(t1, service);                              // 首请求不受 Busy 影响
            Await(service.CloseAsync(1), service);
        }

        private sealed class FakeData : IUIData { }

        // ---- 取消语义 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 调用方取消_只退本人_共享工作继续_其余等待者成功()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);
            var cts1 = new CancellationTokenSource();

            var t1 = service.ShowAsync(1, null, cts1.Token);
            var t2 = service.ShowAsync(1);

            cts1.Cancel();                                    // 第一个等待者退出
            Assert.Throws<OperationCanceledException>(() => t1.GetAwaiter().GetResult());
            Assert.False(loader.LastWorkCt.IsCancellationRequested, "单人取消不牵连共享工作（§4.3）");

            loader.Deliver(FakePrefab("p1"));
            var f2 = Await(t2, service);
            Assert.NotNull(f2);
            Await(service.CloseAsync(1), service);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 全部等待者退出_撤销在途工作()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);
            var cts = new CancellationTokenSource();

            var t = service.ShowAsync(1, null, cts.Token);
            cts.Cancel();                                     // 唯一等待者退出

            Assert.Throws<OperationCanceledException>(() => t.GetAwaiter().GetResult());
            Assert.True(loader.LastWorkCt.IsCancellationRequested, "全员退出且未提交 → 撤销工作");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Close取消在途打开_等待者收类型化Canceled_页面从未登记()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);

            var t = service.ShowAsync(1);
            Await(service.CloseAsync(1), service);            // 加载窗口期内关闭 = 权威取消

            var ex = Assert.Throws<UIOpenException>(() => Await(t, service, 2));
            Assert.AreEqual(UIOpenFailure.Canceled, ex.Reason);
            Assert.False(service.IsOpen(1), "被取消的打开从未登记为打开");
        }

        // ---- 类型化失败 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 加载失败_类型化LoadFailed_含location与根因_无残留()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);
            var boom = new InvalidOperationException("资源损坏");

            var t = service.ShowAsync(1);
            loader.Fail(boom);

            var ex = Assert.Throws<UIOpenException>(() => Await(t, service, 2));
            Assert.AreEqual(UIOpenFailure.LoadFailed, ex.Reason);
            StringAssert.Contains("x1", ex.Message, "失败信息含 location（§4.3 诊断）");
            Assert.AreSame(boom, ex.InnerException, "根因保留");
            Assert.False(service.IsOpen(1));
        }

        // ---- 展示代次与展示作用域（§4.4）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 展示代次_每次打开递增_迟到代次核验失败()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);

            var t1 = service.ShowAsync(1);
            loader.Deliver(FakePrefab("p1"));
            var f1 = Await(t1, service);
            int gen1 = f1.DisplayGeneration;
            Assert.True(f1.IsDisplayCurrent(gen1));
            Await(service.CloseAsync(1), service);

            var t2 = service.ShowAsync(1);                   // 池中复用 = 新的展示代次
            loader.Deliver(FakePrefab("p1"));
            var f2 = Await(t2, service);
            Assert.AreEqual(gen1 + 1, f2.DisplayGeneration, "每次打开递增（复用同实例）");
            Assert.AreSame(f1, f2);
            Assert.False(f1.IsDisplayCurrent(gen1), "旧代次核验失败——迟到回调不得写入（§4.3）");
            Assert.True(f2.IsDisplayCurrent(f2.DisplayGeneration));
            Await(service.CloseAsync(1), service);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 展示作用域CTS_打开可用_关闭即取消()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);

            var t = service.ShowAsync(1);
            loader.Deliver(FakePrefab("p1"));
            var form = Await(t, service);
            var token = form.DisplayToken;
            Assert.False(token.IsCancellationRequested, "展示期内令牌健康（页面内异步绑定它）");

            Await(service.CloseAsync(1), service);
            Assert.True(token.IsCancellationRequested, "关闭即取消——展示作用域在途异步级联终止（§4.4）");
        }

        // ---- Tick 重入安全（§4.3）----

        [Test]
        [Category(TestCategory.Contract)]
        public void Tick重入_OnUpdate内开新界面_快照迭代不炸()
        {
            var catalog = new FakeCatalog(); catalog.Add(1); catalog.Add(2);

            // 同步完成加载器（开界面全内联——重入窗口最真实）；svc 闭包在赋值后使用
            UIService svc = null;
            svc = new UIService(catalog,
                transitionStrategy: new MimicTransition(),
                logicResolver: info => info.Id == 1
                    ? new HookLogic(() => svc.ShowAsync(2).Forget())
                    : (IUIFormLogic)new RecordingLogic(),
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(FakePrefab("p" + loc))));
            var f1 = Await(svc.ShowAsync(1), svc);
            Assert.AreEqual(UIFormState.Active, f1.State);

            Assert.DoesNotThrow(() => svc.Tick(0.05f), "OnUpdate 内开新界面不得在枚举 _forms 时改集合（旧实现此处 InvalidOperationException）");
            Assert.True(svc.IsOpen(2), "重入打开成功");
        }

        /// <summary>带一次性钩子的逻辑件（OnUpdate 触发）。</summary>
        private sealed class HookLogic : IUIFormLogic
        {
            private readonly Action _hook;
            private bool _fired;
            public HookLogic(Action hook) => _hook = hook;
            public void OnInit(UIForm form, IUIData data) { }
            public void OnShow(IUIData data) { }
            public void OnUpdate(float dt) { if (!_fired) { _fired = true; _hook(); } }
            public void OnPause() { }
            public void OnCover() { }
            public void OnReveal() { }
            public void OnHide() { }
        }

        // ---- U1-②：租约 / 缓存预算 / 销毁 / Shutdown（UI-05/06）----

        private sealed class BlockingPop : IPopInterceptor
        {
            public bool Intercepts;
            public bool CanClose(UIForm form) => !Intercepts;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 租约生命周期_打开与缓存期间持有_销毁才释放()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);
            var lease = new CountingLease(FakePrefab("p1"));

            var t = service.ShowAsync(1);
            loader.Deliver(lease);
            Await(t, service);                                 // 打开
            Assert.AreEqual(0, lease.Releases, "打开期间持有租约（UI-05：不提前释放）");

            Await(service.CloseAsync(1), service);             // 落池（缓存实例仍是租约使用者）
            Assert.AreEqual(0, lease.Releases, "缓存期间继续持有（§5.2：缓存实例算使用者）");

            service.Destroy(1);                                // 完整销毁
            Assert.AreEqual(1, lease.Releases, "实例真正销毁后释放——底层引用归零");

            var t2 = service.ShowAsync(1);                     // 再开 = 全新实例 + 全新租约
            var lease2 = new CountingLease(FakePrefab("p1b"));
            loader.Deliver(lease2);
            Await(t2, service);
            Assert.AreEqual(2, loader.Calls, "销毁后的再开是一次全新加载（首次 + 再开 = 2 次）");
            Await(service.CloseAsync(1), service);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 缓存预算_超预算LRU淘汰最旧非打开实例()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1); catalog.Add(2); catalog.Add(3);
            var loader = new GateLoader();
            var service = Service(catalog, loader, cacheBudget: 2);   // 预算 2
            var leases = new Dictionary<int, CountingLease>();

            foreach (int id in new[] { 1, 2, 3 })             // 依次开-关三个界面（关闭即落池）
            {
                var t = service.ShowAsync(id);
                leases[id] = new CountingLease(FakePrefab("p" + id));
                loader.Deliver(leases[id]);
                Await(t, service);
                Await(service.CloseAsync(id), service);
            }

            Assert.AreEqual(1, leases[1].Releases, "LRU 淘汰最旧（id=1 最久未用）——租约释放");
            Assert.AreEqual(0, leases[2].Releases, "预算内保留");
            Assert.AreEqual(0, leases[3].Releases, "预算内保留");

            var tAgain = service.ShowAsync(1);                // 被淘汰的再开 = 重新加载（全新租约）
            Assert.AreEqual(4, loader.Calls, "淘汰后 id=1 再开触发第 4 次加载");
            loader.Deliver(new CountingLease(FakePrefab("p1b")));
            Await(tAgain, service);
            Await(service.CloseAsync(1), service);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Destroy_打开中拒绝_关闭后可销毁()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var service = Service(catalog, loader);

            var t = service.ShowAsync(1);
            loader.Deliver(FakePrefab("p1"));
            Await(t, service);

            Assert.Throws<InvalidOperationException>(() => service.Destroy(1), "打开中不可销毁（先 Close）");
            Await(service.CloseAsync(1), service);
            Assert.DoesNotThrow(() => service.Destroy(1));     // 关闭（池中）后可销毁
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 系统关闭_跳过出栈拦截与离场表现_OnHide仍执行()
        {
            var catalog = new FakeCatalog(); catalog.Add(1);
            var loader = new GateLoader();
            var logic = new RecordingLogic();
            var service = Service(catalog, loader, logic, pop: new BlockingPop { Intercepts = true });

            var t = service.ShowAsync(1);
            loader.Deliver(FakePrefab("p1"));
            Await(t, service);

            // 用户语义：被拦截
            Await(service.CloseAsync(1), service);
            Assert.True(service.IsOpen(1), "用户关闭被出栈拦截挡下");

            // 系统语义（ScopeExit）：跳过拦截、不播离场——OnHide 照常
            Await(service.CloseAsync(1, UIService.CloseReason.ScopeExit), service);
            Assert.False(service.IsOpen(1), "系统清理不受业务守卫阻挡（§4.4）");
            Assert.AreEqual(1, logic.OnHideCount, "OnHide 照常执行");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Shutdown_全链释放_在途取消_缓存销毁_租约归零_Root销毁_幂等()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1); catalog.Add(2); catalog.Add(3);
            var loader = new GateLoader();
            var service = Service(catalog, loader);

            var lease1 = new CountingLease(FakePrefab("p1"));
            var t1 = service.ShowAsync(1);
            loader.Deliver(lease1);
            Await(t1, service);                                // 打开的

            var lease2 = new CountingLease(FakePrefab("p2"));
            var t2 = service.ShowAsync(2);
            loader.Deliver(lease2);
            Await(t2, service);
            Await(service.CloseAsync(2), service);             // 池中的

            var t3 = service.ShowAsync(3);                     // 在途加载（Shutdown 应取消）

            Await(service.ShutdownAsync(), service);

            var ex = Assert.Throws<UIOpenException>(() => Await(t3, service, 2));
            Assert.AreEqual(UIOpenFailure.Canceled, ex.Reason, "在途打开被 Shutdown 取消");
            Assert.AreEqual(1, lease1.Releases, "打开实例的租约归零");
            Assert.AreEqual(1, lease2.Releases, "缓存实例的租约归零");
            Assert.Throws<UIOpenException>(() => service.ShowAsync(1).GetAwaiter().GetResult(),
                "Shutdown 后停止接入");
            Assert.AreEqual(UIOpenFailure.Rejected,
                Assert.Throws<UIOpenException>(() => service.ShowAsync(1).GetAwaiter().GetResult()).Reason);

            Assert.DoesNotThrow(() => Await(service.ShutdownAsync(), service, 1), "幂等");
            Assert.IsNull(FindUiRoot(), "DDoL Root 已销毁");
        }

        private static GameObject FindUiRoot()
        {
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                if (go.name == "[UIRoot]") return go;
            return null;
        }
    }
}
