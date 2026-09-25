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
    /// U2-⑥b Dialog 服务（《UI框架总设计》§4.3"ShowDialogAsync 返回确认/取消/关闭原因，支持有界队列、
    /// 优先级、互斥组和 Scope 取消。重复断线/错误弹窗按键合并，不无限堆叠"）。
    ///
    /// 全替身（FakeCatalog + 同步加载器 + 代码建 UIDialog 假件）——零真资源依赖。
    /// **测试形态对齐 UiNavModalEditModeTests 的已验证泵动模式**（上一批"正式用例 Pending"之谜的
    /// 预防措施）：等待一律经 Await 泵双服务 Tick，不在用例里裸 await。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class DialogServiceEditModeTests : UnityTestBase
    {
        private const int DialogFormId = 201;

        private sealed class FakeCatalog : IUIFormCatalog
        {
            private readonly Dictionary<int, UIFormInfo> _rows = new Dictionary<int, UIFormInfo>();
            public void Add(int id, int layer = 1, bool fullScreen = false)
                => _rows[id] = new UIFormInfo { Id = id, Layer = layer, FullScreen = fullScreen, Location = "x" + id, LuaPath = "x" };
            public UIFormInfo Get(int id)
                => _rows.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"FakeCatalog 无行:{id}");
        }

        /// <summary>即时转场（弹窗语义隔离用——转场契约由 UiTransitionEditModeTests 专测）。</summary>
        private sealed class InstantTransition : ITransitionStrategy
        {
            public UniTask PlayShow(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask PlayClose(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
        }

        private UIService _ui;
        private DialogService _dialogs;

        /// <summary>代码建 UIDialog 假件（Canvas + CanvasGroup + UIDialog + Title/Message + Ok/Cancel 按钮）。</summary>
        private GameObject FakeDialogPrefab(string name)
        {
            GameObject root = Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            UIDialog dialog = root.AddComponent<UIDialog>();
            GameObject title = Scope.CreateGameObject("Title", typeof(RectTransform));
            title.transform.SetParent(root.transform, false);
            dialog.Title = title.AddComponent<TMPro.TextMeshProUGUI>();
            GameObject message = Scope.CreateGameObject("Message", typeof(RectTransform));
            message.transform.SetParent(root.transform, false);
            dialog.Message = message.AddComponent<TMPro.TextMeshProUGUI>();
            GameObject ok = Scope.CreateGameObject("Ok", typeof(RectTransform), typeof(Button));
            ok.transform.SetParent(root.transform, false);
            GameObject cancel = Scope.CreateGameObject("Cancel", typeof(RectTransform), typeof(Button));
            cancel.transform.SetParent(root.transform, false);
            dialog.OkButton = ok.GetComponent<Button>();
            dialog.CancelButton = cancel.GetComponent<Button>();
            return root;
        }

        private GameObject SetUpService(int queueCapacity = 8)
        {
            var catalog = new FakeCatalog();
            catalog.Add(DialogFormId, layer: 2);
            GameObject prefab = FakeDialogPrefab("dlg" + DialogFormId);
            _ui = new UIService(catalog, transitionStrategy: new InstantTransition(),
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(prefab)));
            _ui.RegisterModal(DialogFormId);              // 弹窗按模态登记（生产形态）
            _dialogs = new DialogService(_ui, queueCapacity);
            return prefab;
        }

        /// <summary>泵动等待（双服务）——本仓库异步 EditMode 用例的既定模式（裸 await 在编辑态无 PlayerLoop 会悬挂）。</summary>
        private static T Await<T>(UniTask<T> task, int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++)
            {
                _instance._dialogs.Tick(0.05f);
                _instance._ui.Tick(0.05f);
            }
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            return task.GetAwaiter().GetResult();
        }

        /// <summary>非泛型重载（CloseAsync/ShutdownAsync 等无结果任务）。</summary>
        private static void Await(UniTask task, int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++)
            {
                _instance._dialogs.Tick(0.05f);
                _instance._ui.Tick(0.05f);
            }
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            task.GetAwaiter().GetResult();
        }

        private static DialogServiceEditModeTests _instance;

        /// <summary>
        /// 泵到指定组对话框**武装完成**（结果等待已挂上）。
        /// 关键：表单 IsOpen 先于转场收尾与命令面注册——泵 IsOpen 会过早退出
        /// （上一批"探针成功/正式用例 Pending"之谜的机制）。
        /// </summary>
        private UIDialog PumpUntilDialog(string group, int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks && !_dialogs.TryGetActiveDialog(group, out _); i++)
            {
                _dialogs.Tick(0.05f);
                _ui.Tick(0.05f);
            }
            Assert.IsTrue(_dialogs.TryGetActiveDialog(group, out UIDialog dialog), "弹窗未展示就绪");
            return dialog;
        }

        private static void ClickOk(UIDialog dialog) => dialog.OkButton.onClick.Invoke();
        private static void ClickCancel(UIDialog dialog) => dialog.CancelButton.onClick.Invoke();

        [SetUp]
        public void CaptureInstance()
        {
            _instance = this;                              // 静态泵动助手访问当前 fixture（NUnit 串行执行同 fixture）
        }

        [TearDown]
        public void ReleaseInstance()
        {
            _instance = null;
            _dialogs?.Dispose();
        }

        // ---- 结果路径 ----

        [Test]
        public void 弹窗_确认路径_结果Ok且表单关闭()
        {
            SetUpService();
            var task = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "标题", "正文"));
            UIDialog dialog = PumpUntilDialog(null);

            ClickOk(dialog);
            Assert.AreEqual(DialogResult.Ok, Await(task), "确认按钮 → Ok");
            Assert.IsFalse(_ui.IsOpen(DialogFormId), "结果落定后表单收口关闭");
            Assert.AreEqual(0, _dialogs.ActiveCount, "组占用随收尾释放");
        }

        [Test]
        public void 弹窗_取消路径_结果Cancel()
        {
            SetUpService();
            var task = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "标题", "正文"));
            UIDialog dialog = PumpUntilDialog(null);

            ClickCancel(dialog);
            Assert.AreEqual(DialogResult.Cancel, Await(task), "取消按钮 → Cancel");
            Assert.IsFalse(_ui.IsOpen(DialogFormId));
        }

        // ---- 合并与互斥 ----

        [Test]
        public void 弹窗_同MergeKey重复请求_合并共享结果不堆叠()
        {
            SetUpService();
            var t1 = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "断线", "正在重连", mergeKey: "reconnect"));
            var t2 = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "断线", "正在重连", mergeKey: "reconnect"));
            UIDialog dialog = PumpUntilDialog(null);
            Assert.AreEqual(0, _dialogs.QueuedCount, "第二个请求并入等待——不产生新队列条目（§4.3 不无限堆叠）");

            ClickOk(dialog);
            Assert.AreEqual(DialogResult.Ok, Await(t1));
            Assert.AreEqual(DialogResult.Ok, Await(t2), "合并等待者共享同一结果");
        }

        [Test]
        public void 弹窗_互斥组_同组串行_不同组并行()
        {
            var catalog = new FakeCatalog();
            catalog.Add(201, layer: 2);
            catalog.Add(202, layer: 2);
            GameObject p1 = FakeDialogPrefab("dlg201");
            GameObject p2 = FakeDialogPrefab("dlg202");
            _ui = new UIService(catalog, transitionStrategy: new InstantTransition(),
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(
                    UIPrefabLeases.Unowned(loc == "x201" ? p1 : p2)));
            _dialogs = new DialogService(_ui);

            var ta = _dialogs.ShowAsync(new DialogService.Request(201, "A", "a", group: "A"));
            PumpUntilDialog("A");
            var tb = _dialogs.ShowAsync(new DialogService.Request(202, "B", "b", group: "B"));
            PumpUntilDialog("B");
            Assert.IsTrue(_dialogs.TryGetActiveDialog("B", out _), "不同组可并行展示");

            var ta2 = _dialogs.ShowAsync(new DialogService.Request(201, "A2", "a2", group: "A"));
            Assert.AreEqual(1, _dialogs.QueuedCount, "同组展示中：后到排队");
            Assert.AreEqual(UniTaskStatus.Pending, ta2.Status);

            ClickOk(PumpUntilDialog("A"));                 // A 收尾 → 泵出队 A2
            Await(ta, 20);
            ClickOk(PumpUntilDialog("A"));
            Assert.AreEqual(DialogResult.Ok, Await(ta2));
            ClickOk(PumpUntilDialog("B"));                 // B 独立收尾
            Assert.AreEqual(DialogResult.Ok, Await(tb));
        }

        [Test]
        public void 弹窗_优先级_同组高先出_同级FIFO()
        {
            var catalog = new FakeCatalog();
            catalog.Add(201, layer: 2);
            GameObject prefab = FakeDialogPrefab("dlg201");
            _ui = new UIService(catalog, transitionStrategy: new InstantTransition(),
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(prefab)));
            _dialogs = new DialogService(_ui);

            var t1 = _dialogs.ShowAsync(new DialogService.Request(201, "占位", "x", group: "A"));
            PumpUntilDialog("A");
            var tLow = _dialogs.ShowAsync(new DialogService.Request(201, "低", "x", priority: 0, group: "A"));
            var tHigh = _dialogs.ShowAsync(new DialogService.Request(201, "高", "x", priority: 5, group: "A"));
            var tLow2 = _dialogs.ShowAsync(new DialogService.Request(201, "低2", "x", priority: 1, group: "A"));
            Assert.AreEqual(3, _dialogs.QueuedCount);

            ClickOk(PumpUntilDialog("A"));                 // 关占位 → 高优先级先展示
            Await(t1, 20);
            UIDialog high = PumpUntilDialog("A");
            Assert.AreEqual("高", high.Title.text, "优先级降序出队");
            ClickOk(high);
            Await(tHigh, 20);

            UIDialog low2 = PumpUntilDialog("A");
            Assert.AreEqual("低2", low2.Title.text, "同级 FIFO（低 先于 低2 入队，低2 在 低 之后）");
            ClickOk(low2);
            Await(tLow2, 20);

            UIDialog low = PumpUntilDialog("A");           // 最低优先级最后出队
            Assert.AreEqual("低", low.Title.text);
            ClickOk(low);
            Assert.AreEqual(DialogResult.Ok, Await(tLow));
        }

        [Test]
        public void 弹窗_队列满_类型化拒绝()
        {
            SetUpService(queueCapacity: 1);
            var t1 = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "占位", "x"));
            PumpUntilDialog(null);
            _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "排1", "x"));   // 排队 1/1

            var ex = Assert.Throws<UIOpenException>(() =>
                _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "超", "x")), "队列满必须拒绝");
            Assert.AreEqual(UIOpenFailure.Rejected, ex.Reason);
            Assert.AreEqual(1, _dialogs.QueuedCount, "被拒请求不入队");

            ClickOk(PumpUntilDialog(null));
            Assert.AreEqual(DialogResult.Ok, Await(t1));
        }

        // ---- 收口 ----

        [Test]
        public void 弹窗_外部关闭收口_结果Closed()
        {
            SetUpService();
            var task = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "标题", "正文"));
            PumpUntilDialog(null);

            var close = _ui.CloseAsync(DialogFormId);      // 非按钮路径关闭（如导航 Back）
            Await(close, 10);
            Assert.AreEqual(DialogResult.Closed, Await(task), "外部关闭 → Closed（§4.3 关闭原因）");
            _dialogs.Tick(0.05f);                          // 收尾路径不应再关已关的表单（幂等守卫）
        }

        [Test]
        public void 弹窗_Shutdown收口_结果Closed()
        {
            SetUpService();
            var task = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "标题", "正文"));
            PumpUntilDialog(null);

            var shutdown = _ui.ShutdownAsync();
            for (int i = 0; i < 100 && shutdown.Status == UniTaskStatus.Pending; i++) _ui.Tick(0.05f);
            Assert.AreEqual(DialogResult.Closed, Await(task), "系统关闭路径同样收口");
        }

        [Test]
        public void 弹窗_等待者取消_只解除本人_他人照常拿到结果()
        {
            SetUpService();
            var t1 = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "断线", "x", mergeKey: "m"));
            using var cts = new CancellationTokenSource();
            var t2 = _dialogs.ShowAsync(new DialogService.Request(DialogFormId, "断线", "x", mergeKey: "m"), cts.Token);
            PumpUntilDialog(null);

            cts.Cancel();                                  // 等待者 2 撤销本人等待
            bool canceled2 = false;
            try { Await(t2, 20); Assert.Fail("预期取消"); }
            catch (OperationCanceledException) { canceled2 = true; }
            Assert.IsTrue(canceled2, "等待者自身取消只解除本人");
            Assert.IsTrue(_ui.IsOpen(DialogFormId), "对话框不受单人等待取消影响");

            ClickOk(PumpUntilDialog(null));
            Assert.AreEqual(DialogResult.Ok, Await(t1), "其余等待者照常拿到结果");
        }

        // ---- UIDialog 等待式（widget 直测）----

        [Test]
        public void UIDialog_等待式_按钮落定_外部收口幂等()
        {
            GameObject prefab = FakeDialogPrefab("w");
            UIDialog dialog = prefab.GetComponent<UIDialog>();

            UniTask<bool> wait = dialog.WaitAsync("t", "m", "确定", "取消");
            Assert.AreEqual(UniTaskStatus.Pending, wait.Status);
            dialog.OkButton.onClick.Invoke();
            Assert.IsTrue(wait.GetAwaiter().GetResult(), "Ok → true");
            Assert.IsFalse(dialog.SettleExternally(), "结果已落定：外部收口无效（幂等）");

            UniTask<bool> wait2 = dialog.WaitAsync("t2", "m2");     // 复用实例重新武装
            Assert.AreEqual(UniTaskStatus.Pending, wait2.Status);
            Assert.IsTrue(dialog.SettleExternally(), "外部收口 → false");
            Assert.IsFalse(dialog.SettleExternally(), "重复收口幂等");
            Assert.IsFalse(wait2.GetAwaiter().GetResult());
            Assert.IsFalse(dialog.SettleExternally(), "收口后按钮回调已清——再点不复活");
            dialog.OkButton.onClick.Invoke();
        }

        [Test]
        public void 弹窗_缺UIDialog组件_类型化失败()
        {
            var catalog = new FakeCatalog();
            catalog.Add(301, layer: 2);
            GameObject bad = Scope.CreateGameObject("bad", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            _ui = new UIService(catalog, transitionStrategy: new InstantTransition(),
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(bad)));
            _dialogs = new DialogService(_ui);

            var task = _dialogs.ShowAsync(new DialogService.Request(301, "x", "x"));
            for (int i = 0; i < 100 && task.Status == UniTaskStatus.Pending; i++)
            {
                _dialogs.Tick(0.05f);
                _ui.Tick(0.05f);
            }
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status);
            Assert.Throws<UIOpenException>(() => task.GetAwaiter().GetResult(), "表单缺组件 = 装配错误，类型化失败");
        }

        /// <summary>泵到指定组展示就绪。</summary>
        private void PumpUntilActiveGroup(string group, int maxTicks = 50)
        {
            for (int i = 0; i < maxTicks && !_dialogs.TryGetActiveDialog(group, out _); i++)
            {
                _dialogs.Tick(0.05f);
                _ui.Tick(0.05f);
            }
            Assert.IsTrue(_dialogs.TryGetActiveDialog(group, out _), $"组[{group}] 弹窗未展示就绪");
        }
    }
}
