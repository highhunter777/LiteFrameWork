using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// U2 首版导航与模态栈（《UI框架总设计》§4.3 单写者串行/队列上限/等待超时/取消；
    /// §6.2 模态优先 Back、模态射线遮蔽、输入协调组成输入）+ UiFx 原语中断复位（动画专项 §14 清单）。
    /// 全替身（FakeCatalog/GateLoader/Unowned 租约）——零真资源依赖。
    /// </summary>
    public sealed class UiNavModalEditModeTests : UnityTestBase
    {

        private sealed class FakeCatalog : IUIFormCatalog
        {
            private readonly Dictionary<int, UIFormInfo> _rows = new Dictionary<int, UIFormInfo>();
            public void Add(int id, int layer = 1, bool fullScreen = false)
                => _rows[id] = new UIFormInfo { Id = id, Layer = layer, FullScreen = fullScreen, Location = "x" + id, LuaPath = "x" };
            public UIFormInfo Get(int id)
                => _rows.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"FakeCatalog 无行:{id}");
        }

        /// <summary>门控加载器：加载挂起在 UTCS（测试控制"执行窗口期"）；取消契约同 U1 用例。</summary>
        private sealed class GateLoader
        {
            public readonly List<string> CallOrder = new List<string>();
            private UniTaskCompletionSource<IUIPrefabLease> _pending;
            private CancellationTokenRegistration _registration;

            public Func<string, CancellationToken, UniTask<IUIPrefabLease>> Loader => (loc, ct) =>
            {
                CallOrder.Add(loc);
                _pending = new UniTaskCompletionSource<IUIPrefabLease>();
                _registration.Dispose();
                _registration = ct.Register(() => _pending.TrySetException(new OperationCanceledException(ct)));
                return _pending.Task;
            };

            public void Deliver(GameObject prefab) => _pending?.TrySetResult(UIPrefabLeases.Unowned(prefab));
        }

        private GameObject FakePrefab(string name)
            => Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

        private static T Await<T>(UniTask<T> task, UIService service, int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++) service.Tick(0.05f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            return task.GetAwaiter().GetResult();
        }

        private static UIOpenException AwaitFailure<T>(UniTask<T> task, UIService service, int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++) service.Tick(0.05f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            try { task.GetAwaiter().GetResult(); Assert.Fail("预期失败但任务成功"); return null; }
            catch (UIOpenException ex) { return ex; }
        }

        /// <summary>等待取消结果：外部 token 的 OCE（AttachExternalCancellation 先行）或类型化 Canceled 均合法。</summary>
        private static void AwaitCancellation<T>(UniTask<T> task, UIService service, int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++) service.Tick(0.05f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            try
            {
                task.GetAwaiter().GetResult();
                Assert.Fail("预期取消但任务成功");
            }
            catch (OperationCanceledException) { /* 调用方 token 取消本人等待——标准语义 */ }
            catch (UIOpenException ex)
            {
                Assert.AreEqual(UIOpenFailure.Canceled, ex.Reason, "类型化取消路径必须是 Canceled");
            }
        }

        /// <summary>即时转场（导航/模态语义隔离用：不播表现——转场契约由 UiTransitionEditModeTests 专测）。</summary>
        private sealed class InstantTransition : ITransitionStrategy
        {
            public UniTask PlayShow(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask PlayClose(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
        }

        private UIService Service(FakeCatalog catalog, GateLoader loader)
            => new UIService(catalog, transitionStrategy: new InstantTransition(), loadPrefab: loader.Loader);

        // ---- 导航：单写者串行（§4.3）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 导航_单写者串行_前一操作完成才执行下一个()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1); catalog.Add(2);
            var loader = new GateLoader();
            var svc = Service(catalog, loader);
            var nav = new UINavigationController(svc);

            var t1 = nav.GoAsync(1);
            var t2 = nav.GoAsync(2);                     // t1 被加载门挂住——t2 只能排队

            Assert.AreEqual(1, loader.CallOrder.Count, "串行：t2 不得在 t1 完成前执行");
            Assert.AreEqual(1, nav.QueuedCount, "t2 在队");

            loader.Deliver(FakePrefab("p1"));            // t1 完成 → 队列立即消费 t2
            var f1 = Await(t1, svc);
            Assert.AreEqual(2, loader.CallOrder.Count, "t1 完成后串行推进 t2");
            Assert.AreEqual("x1", loader.CallOrder[0]);
            Assert.AreEqual("x2", loader.CallOrder[1]);

            loader.Deliver(FakePrefab("p2"));
            var f2 = Await(t2, svc);
            Assert.IsTrue(svc.IsOpen(f1.Id) && svc.IsOpen(f2.Id), "两页均打开");
            Assert.AreEqual(2, nav.Executed);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 导航_队列满_在创建入栈OnShow之前拒绝()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1); catalog.Add(2); catalog.Add(3);
            var loader = new GateLoader();
            var svc = Service(catalog, loader);
            var nav = new UINavigationController(svc, queueCapacity: 1);

            var t1 = nav.GoAsync(1);                     // 占用执行位（被门挂住）
            var t2 = nav.GoAsync(2);                     // 排队 1/1
            Assert.AreEqual(1, nav.QueuedCount);

            var ex = Assert.Throws<UIOpenException>(() => nav.GoAsync(3), "队列满必须拒绝（§4.3）");
            Assert.AreEqual(UIOpenFailure.Rejected, ex.Reason);
            Assert.AreEqual(1, nav.RejectedByCapacity);

            // 收尾：放行两单，断言 3 从未执行（拒绝发生在创建/入栈/OnShow 之前）
            loader.Deliver(FakePrefab("p1"));
            Await(t1, svc);
            loader.Deliver(FakePrefab("p2"));
            Await(t2, svc);
            Assert.AreEqual(2, loader.CallOrder.Count, "被拒绝的 Go 不得产生任何加载");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 导航_排队等待超时_可观测拒绝()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1); catalog.Add(2); catalog.Add(3);
            var loader = new GateLoader();
            var svc = Service(catalog, loader);
            long nowMs = 0;
            var nav = new UINavigationController(svc, openTimeoutSeconds: 1f, nowMs: () => nowMs);

            var t1 = nav.GoAsync(1);                     // 执行位被占用（门挂住）
            var t2 = nav.GoAsync(2);                     // 排队（nowMs=0）
            nowMs = 2000;                                 // 排队等待超过 1s

            var t3 = nav.GoAsync(3);                     // 入队调度点清点过期项：t2 超时失败
            var ex = AwaitFailure(t2, svc);
            Assert.AreEqual(UIOpenFailure.Timeout, ex.Reason, "等待超时必须可观测（§4.3）");
            Assert.AreEqual(1, nav.TimedOut);

            loader.Deliver(FakePrefab("p1"));             // t1 放行 → t3 正常执行（未超时）
            Await(t1, svc);
            loader.Deliver(FakePrefab("p3"));
            Await(t3, svc);
            Assert.AreEqual(2, loader.CallOrder.Count, "超时的 t2 不得执行");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 导航_排队期取消_出队移除不执行()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1); catalog.Add(2);
            var loader = new GateLoader();
            var svc = Service(catalog, loader);
            var nav = new UINavigationController(svc);

            var t1 = nav.GoAsync(1);
            using var cts = new CancellationTokenSource();
            var t2 = nav.GoAsync(2, ct: cts.Token);
            Assert.AreEqual(1, nav.QueuedCount);

            cts.Cancel();                                 // 排队期取消：标记放弃——出队时跳过，不执行
            AwaitCancellation(t2, svc);
            Assert.AreEqual(1, nav.CancelledWhileQueued);

            loader.Deliver(FakePrefab("p1"));             // t1 放行——t2 已不可达
            Await(t1, svc);
            Assert.AreEqual(1, loader.CallOrder.Count, "被取消的 Go 不得产生加载");
        }

        // ---- 模态栈（§6.2）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 模态_Back先关最顶模态_再组栈顶()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1, layer: 1);                     // Window
            catalog.Add(9, layer: 2); catalog.Add(10, layer: 2);   // Top：两个模态
            var loader = new GateLoader();
            var svc = Service(catalog, loader);
            var nav = new UINavigationController(svc);
            svc.RegisterModal(9);
            svc.RegisterModal(10);

            // 同步放行三单（loader 即刻交付）
            Func<int, UniTask<UIForm>> open = id =>
            {
                var t = nav.GoAsync(id);
                loader.Deliver(FakePrefab("p" + id));
                return t;
            };
            Await(open(1), svc);
            Await(open(9), svc);
            Await(open(10), svc);
            Assert.IsTrue(svc.IsModalOpen, "模态打开中");
            Assert.AreEqual(10, svc.TopModalId, "最顶模态按画布序取");

            Assert.IsTrue(Await(nav.BackAsync(), svc), "Back 提交关闭（§6.2 平台返回统一处理）");
            Assert.IsFalse(svc.IsOpen(10), "先关最顶模态");
            Assert.IsTrue(svc.IsOpen(9));

            Assert.IsTrue(Await(nav.BackAsync(), svc));
            Assert.IsFalse(svc.IsOpen(9));
            Assert.IsFalse(svc.IsModalOpen, "模态全关");

            Assert.IsTrue(Await(nav.BackAsync(), svc));   // 无模态：关最高非空组栈顶
            Assert.IsFalse(svc.IsOpen(1));

            Assert.IsFalse(Await(nav.BackAsync(), svc), "无可返回目标 = 确定结果（不抛）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 模态_射线遮蔽_下方页面不可命中且关闭后恢复()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1, layer: 1);                     // Window 普通页
            catalog.Add(8, layer: 2);                      // Top 普通页（开在模态之前——视觉下方）
            catalog.Add(9, layer: 2);                      // Top 模态
            var loader = new GateLoader();
            var svc = Service(catalog, loader);
            var nav = new UINavigationController(svc);

            Func<int, UniTask<UIForm>> open = id =>
            {
                var t = nav.GoAsync(id);
                loader.Deliver(FakePrefab("p" + id));
                return t;
            };
            var f1 = Await(open(1), svc);
            var f8 = Await(open(8), svc);
            Assert.IsTrue(f1.CanvasGroup.blocksRaycasts && f8.CanvasGroup.blocksRaycasts, "无模态时可命中");

            svc.RegisterModal(9);
            var f9 = Await(open(9), svc);
            Assert.IsFalse(f1.CanvasGroup.blocksRaycasts, "更低层级组：模态下方不可命中（§6.2）");
            Assert.IsFalse(f8.CanvasGroup.blocksRaycasts, "同组更早打开：模态下方不可命中");
            Assert.IsTrue(f9.CanvasGroup.blocksRaycasts, "模态自身照常命中");

            Assert.IsTrue(Await(nav.BackAsync(), svc));    // 关模态 → 下方恢复
            Assert.IsTrue(f1.CanvasGroup.blocksRaycasts, "模态关闭后下方恢复可命中");
            Assert.IsTrue(f8.CanvasGroup.blocksRaycasts);
        }

        // ---- UiFx 原语中断复位（动画专项 §14 清单）----

        [Test]
        [Category(TestCategory.Contract)]
        public void UiFx_中断复位_完成态即基线()
        {
            var go = Scope.CreateGameObject("fx", typeof(RectTransform), typeof(Image));
            var image = go.GetComponent<Image>();
            var rt = (RectTransform)go.transform;

            // Pulse：alpha 呼吸——中断复位回原透明度（不依赖 DOTween 内部播放状态——编辑态行为有 G20e 先例）
            image.color = new Color(1f, 1f, 1f, 0.8f);
            using (var h = UiFx.Pulse(image))
            {
                Assert.DoesNotThrow(() => h.Stop(complete: true), "中断收尾必须不抛");
            }
            Assert.AreEqual(0.8f, image.color.a, 0.0001f, "Pulse 中断复位=原透明度");

            // Flash：白高亮回落——完成态=原色（修正登记：原实现回落固定色）
            image.color = new Color(0.9f, 0.2f, 0.3f, 1f);
            using (var h = UiFx.Flash(image))
            {
                h.Stop(complete: true);
            }
            Assert.IsTrue(Mathf.Approximately(0.9f, image.color.r)
                && Mathf.Approximately(0.2f, image.color.g)
                && Mathf.Approximately(0.3f, image.color.b)
                && Mathf.Approximately(1f, image.color.a), "Flash 复位=原色（通道级近似比较——tween 求值器有浮点舍入）");

            // Slide：位移入场——完成态=原位
            rt.anchoredPosition = new Vector2(10f, 10f);
            using (var h = UiFx.Slide(rt, new Vector2(50f, 0f)))
            {
                h.Dispose();                              // 展示作用域收尾口径（=复位离场）
            }
            Assert.AreEqual(new Vector2(10f, 10f), rt.anchoredPosition, "Slide 复位=原位");
        }
    }
}
