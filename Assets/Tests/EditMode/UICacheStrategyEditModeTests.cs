using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// per-form 缓存策略（§5.2 U2 表列：Resident 不淘汰 / DestroyOnClose 关即销毁 / LRU 默认）。
    /// 全替身零真资源——与 UiU1EditModeTests 同口径（含 Await 泵转场）。
    /// </summary>
    public sealed class UICacheStrategyEditModeTests : UnityTestBase
    {
        private sealed class FakeCatalog : IUIFormCatalog
        {
            private readonly Dictionary<int, UIFormInfo> _rows = new Dictionary<int, UIFormInfo>();
            public void Add(int id, int layer = 1, UICacheStrategy strategy = UICacheStrategy.Lru)
                => _rows[id] = new UIFormInfo { Id = id, Layer = layer, Location = "x" + id, LuaPath = "x", CacheStrategy = strategy };
            public UIFormInfo Get(int id)
                => _rows.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"FakeCatalog 无行:{id}");
        }

        private UIService _svc;
        private FakeCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = new FakeCatalog();
            _svc = new UIService(_catalog,
                transitionStrategy: new InstantTransition(),
                loadPrefab: (loc, ct) =>
                    UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(FakePrefab("p" + loc))));
        }

        /// <summary>即时转场（缓存策略语义隔离：不播表现——转场契约由 UiTransitionEditModeTests 专测）。</summary>
        private sealed class InstantTransition : ITransitionStrategy
        {
            public UniTask PlayShow(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask PlayClose(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
        }

        [TearDown]
        public void TearDown()
        {
            var r = TestContext.CurrentContext.Result;
            if (r.FailCount > 0)
                UnityEngine.Debug.Log($"[CacheTestFail] {TestContext.CurrentContext.Test.Name} :: {r.Message}");
            if (_svc != null)
            {
                var shutdown = _svc.ShutdownAsync();
                for (int i = 0; i < 20 && shutdown.Status == UniTaskStatus.Pending; i++) _svc.Tick(0.1f);
                _svc = null;
            }
        }

        private GameObject FakePrefab(string name)
            => Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

        private UIForm Await(UniTask<UIForm> task)
        {
            for (int i = 0; i < 20 && task.Status == UniTaskStatus.Pending; i++) _svc.Tick(0.1f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            return task.GetAwaiter().GetResult();
        }

        private void AwaitDone(UniTask task)
        {
            for (int i = 0; i < 20 && task.Status == UniTaskStatus.Pending; i++) _svc.Tick(0.1f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成");
            task.GetAwaiter().GetResult();
        }

        // ---- DestroyOnClose ----

        [Test]
        [Category(TestCategory.Contract)]
        public void DestroyOnClose_关即销毁_再Show为全新实例()
        {
            _catalog.Add(1, strategy: UICacheStrategy.DestroyOnClose);
            _catalog.Add(2);

            var f1 = Await(_svc.ShowAsync(1));
            var f1Root = f1.Root;
            AwaitDone(_svc.CloseAsync(1));
            Assert.IsFalse(_svc.IsOpen(1));

            var f1b = Await(_svc.ShowAsync(1));
            Assert.AreNotEqual(f1Root, f1b.Root, "DestroyOnClose 关闭后复用=全新实例（不进池）");
            AwaitDone(_svc.CloseAsync(1));

            // 对比：LRU 策略的页面关闭后复用 = 同一实例
            var f2 = Await(_svc.ShowAsync(2));
            var f2Root = f2.Root;
            AwaitDone(_svc.CloseAsync(2));
            var f2b = Await(_svc.ShowAsync(2));
            Assert.AreEqual(f2Root, f2b.Root, "LRU 策略复用=同一实例");
        }

        // ---- Resident vs LRU ----

        [Test]
        [Category(TestCategory.Contract)]
        public void Resident不参与LRU淘汰()
        {
            _catalog.Add(1, strategy: UICacheStrategy.Resident);
            _catalog.Add(2, strategy: UICacheStrategy.Lru);
            _svc.CacheBudget = 1;

            Await(_svc.ShowAsync(1));
            Await(_svc.ShowAsync(2));
            AwaitDone(_svc.CloseAsync(1));
            AwaitDone(_svc.CloseAsync(2));
            // 池中 2 个 > 预算 1 → 淘汰最旧非 Resident → LRU(2) 被淘汰

            Assert.DoesNotThrow(() => _svc.Destroy(1), "Resident(1) 在池中——可 Destroy");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void LRU超预算_淘汰最旧_Resident幸存()
        {
            _catalog.Add(1, strategy: UICacheStrategy.Resident);
            _catalog.Add(2, strategy: UICacheStrategy.Lru);
            _catalog.Add(3, strategy: UICacheStrategy.Lru);
            _svc.CacheBudget = 2;

            Await(_svc.ShowAsync(1));
            Await(_svc.ShowAsync(2));
            Await(_svc.ShowAsync(3));
            AwaitDone(_svc.CloseAsync(1));
            AwaitDone(_svc.CloseAsync(2));
            AwaitDone(_svc.CloseAsync(3));
            // 池中 3 个 > 预算 2 → 淘汰 1 个（最旧非 Resident）

            var f = Await(_svc.ShowAsync(1));
            Assert.IsNotNull(f, "Resident 幸存——重开成功");
            AwaitDone(_svc.CloseAsync(1));
        }
    }
}
