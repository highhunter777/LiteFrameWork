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
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 统一反馈入口（《UI框架总设计》§6.1"Loading、空状态、错误/重试、Toast 有统一入口"）：
    /// - Loading 嵌套阻断 + 阻断期 Back = 取消当前操作（不穿透，§6.1）+ 取消传播到被门控操作；
    /// - 错误/重试经 DialogService（重试/退出、同 MergeKey 合并）；
    /// - Toast **多条并存**：各自计时独立消失、纵向堆叠、超限淘汰最旧；
    /// - 三面同为 System 层（layer=3）form，服务不自建视觉（§7）。
    /// 全替身（fake catalog/loader/假件 prefab）；就绪一律泵到**业务事实**（组件武装），不是 IsOpen。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class FeedbackServiceEditModeTests : UnityTestBase
    {
        private const int ErrorFormId = 201;
        private const int LoadingFormId = 202;
        private const int ToastFormId = 203;
        private const int NormalPageId = 10;

        private sealed class FakeCatalog : IUIFormCatalog
        {
            private readonly Dictionary<int, UIFormInfo> _rows = new Dictionary<int, UIFormInfo>();
            public void Add(int id, int layer = 1, bool fullScreen = false)
                => _rows[id] = new UIFormInfo { Id = id, Layer = layer, FullScreen = fullScreen, Location = "x" + id, LuaPath = "" };
            public UIFormInfo Get(int id)
                => _rows.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"FakeCatalog 无行:{id}");
        }

        private sealed class InstantTransition : ITransitionStrategy
        {
            public UniTask PlayShow(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask PlayClose(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
        }

        private UIService _ui;
        private DialogService _dialogs;
        private UINavigationController _nav;
        private FeedbackService _feedback;
        private UIDialog _dialogInstance;

        [SetUp]
        public void SetUp()
        {
            var catalog = new FakeCatalog();
            catalog.Add(ErrorFormId, layer: 3);           // System 层：错误弹窗
            catalog.Add(LoadingFormId, layer: 3);         // System 层：加载遮罩
            catalog.Add(ToastFormId, layer: 3);           // System 层：Toast 承载
            catalog.Add(NormalPageId, layer: 1);          // 普通页（Back 穿透断言用）

            var prefabs = new Dictionary<string, GameObject>
            {
                ["x" + ErrorFormId] = FakeDialogPrefab("errDlg"),
                ["x" + LoadingFormId] = FakeLoadingPrefab("loading"),
                ["x" + ToastFormId] = FakeToastPrefab("toast"),
                ["x" + NormalPageId] = FakePlainPrefab("page"),
            };

            _ui = new UIService(catalog, transitionStrategy: new InstantTransition(),
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(prefabs[loc])));
            _ui.RegisterModal(ErrorFormId);              // 错误弹窗是模态；Loading/Toast 走射线阻断不是模态
            _nav = new UINavigationController(_ui);
            _dialogs = new DialogService(_ui);
            _feedback = new FeedbackService(_ui, _dialogs, _nav);
        }

        [TearDown]
        public void TearDown()
        {
            _feedback?.Dispose();
            _dialogs?.Dispose();
        }

        // ---- 假件 prefab（代码建，仅测试用；生产视觉一律取模板，§7）----

        /// <summary>UIDialog 假件（同 DialogServiceEditModeTests）。</summary>
        private GameObject FakeDialogPrefab(string name)
        {
            GameObject root = Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            UIDialog dialog = root.AddComponent<UIDialog>();
            GameObject title = Scope.CreateGameObject("Title", typeof(RectTransform));
            title.transform.SetParent(root.transform, false);
            dialog.Title = title.AddComponent<TMPro.TextMeshProUGUI>();
            GameObject ok = Scope.CreateGameObject("Ok", typeof(RectTransform), typeof(Button));
            ok.transform.SetParent(root.transform, false);
            GameObject cancel = Scope.CreateGameObject("Cancel", typeof(RectTransform), typeof(Button));
            cancel.transform.SetParent(root.transform, false);
            dialog.OkButton = ok.GetComponent<Button>();
            dialog.CancelButton = cancel.GetComponent<Button>();
            _dialogInstance = dialog;
            return root;
        }

        private GameObject FakeLoadingPrefab(string name)
        {
            GameObject root = Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            LoadingMask mask = root.AddComponent<LoadingMask>();
            GameObject blocker = Scope.CreateGameObject("Blocker", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(root.transform, false);
            mask.Blocker = blocker.GetComponent<Image>();
            GameObject msg = Scope.CreateGameObject("Message", typeof(RectTransform));
            msg.transform.SetParent(root.transform, false);
            mask.Message = msg.AddComponent<TMPro.TextMeshProUGUI>();
            return root;
        }

        private GameObject FakeToastPrefab(string name)
        {
            GameObject root = Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            Toast toast = root.AddComponent<Toast>();
            // 结构对齐真模板：_Template(非激活) → Label(带 TMP_Text，随条目一起克隆)
            GameObject tpl = Scope.CreateGameObject("_Template", typeof(RectTransform), typeof(Image));
            tpl.transform.SetParent(root.transform, false);
            GameObject label = Scope.CreateGameObject("Label", typeof(RectTransform));
            label.transform.SetParent(tpl.transform, false);
            label.AddComponent<TMPro.TextMeshProUGUI>();
            tpl.SetActive(false);
            toast.Template = (RectTransform)tpl.transform;
            return root;
        }

        private GameObject FakePlainPrefab(string name)
            => Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

        // ---- 泵动（EditMode 无 PlayerLoop：等待一律显式泵）----

        private void Pump(int ticks = 200, float dt = 0.05f)
        {
            for (int i = 0; i < ticks; i++) { _dialogs.Tick(dt); _ui.Tick(dt); }
        }

        private T PumpAwait<T>(UniTask<T> task, int maxTicks = 200, float dt = 0.05f)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++)
            {
                _dialogs.Tick(dt);
                _ui.Tick(dt);
            }
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            return task.GetAwaiter().GetResult();
        }

        /// <summary>泵到错误弹窗武装完成（DialogService 命令面已挂——同 Dialog 用例的就绪口径）。</summary>
        private UIDialog PumpUntilDialog(string group)
        {
            for (int i = 0; i < 200 && !_dialogs.TryGetActiveDialog(group, out _); i++)
            {
                _dialogs.Tick(0.05f);
                _ui.Tick(0.05f);
            }
            Assert.IsTrue(_dialogs.TryGetActiveDialog(group, out UIDialog dialog), "错误弹窗未展示就绪");
            return dialog;
        }

        /// <summary>预热 Toast 承载（承载**懒打开**：首次 ShowToast 才建 form），随后清场回到空态。</summary>
        private Toast WarmToast()
        {
            _feedback.ShowToast("", seconds: 0.01f);
            Pump();
            Toast toast = _feedback.ToastHost;
            Assert.IsNotNull(toast, "Toast 承载未就绪（业务事实 = 组件已挂，不是表单 IsOpen）");
            toast.Tick(999f);                             // 清掉预热条，回到空态
            Assert.AreEqual(0, toast.VisibleCount, "预热条已清");
            return toast;
        }

        // ---- Loading ----

        [Test]
        public void Loading_嵌套计数_最后一个结束才解除阻断()
        {
            using (var s1 = _feedback.BeginLoading("一"))
            {
                Assert.IsTrue(_feedback.IsLoading);
                using (var s2 = _feedback.BeginLoading("二"))
                {
                    Assert.IsTrue(_feedback.IsLoading);
                }
                Assert.IsTrue(_feedback.IsLoading, "嵌套未清零不得解除");
            }
            Assert.IsFalse(_feedback.IsLoading, "最后一个作用域结束才解除阻断");
        }

        [Test]
        public void Loading_阻断期Back_取消操作且不穿透()
        {
            using (var scope = _feedback.BeginLoading("进房中"))
            {
                CancellationToken token = _feedback.LoadingToken;
                bool canceled = false;
                token.Register(() => canceled = true);

                bool consumed = _nav.BackAsync().GetAwaiter().GetResult();   // Back 进拦截链

                Assert.IsTrue(consumed, "阻断期 Back 被消费");
                Assert.IsTrue(canceled, "取消信号已发给被门控操作");
                Assert.IsTrue(_feedback.IsLoading, "服务只给取消信号——操作自行收尾，不强行结束");
                Assert.IsFalse(_ui.IsOpen(NormalPageId), "不得穿透关闭下层页面（§6.1）");
            }
        }

        [Test]
        public void Loading_非阻断期Back_穿透到正常导航链()
        {
            var open = _nav.GoAsync(NormalPageId);
            PumpAwait(open);
            Assert.IsTrue(_ui.IsOpen(NormalPageId));

            var back = _nav.BackAsync();
            Assert.IsTrue(PumpAwait(back), "Back 提交关闭页面");
            Assert.IsFalse(_ui.IsOpen(NormalPageId), "非阻断期：正常导航链生效");
        }

        [Test]
        public void Loading_展示遮罩_阻断面吃射线且带文案()
        {
            using (var scope = _feedback.BeginLoading("读取中…"))
            {
                Pump();
                Assert.IsTrue(_ui.IsOpen(LoadingFormId), "Loading 作为 System 层 form 打开");
                var mask = UnityEngine.Object.FindObjectOfType<LoadingMask>();
                Assert.IsNotNull(mask, "遮罩控件实例已挂（业务事实，不是表单 IsOpen）");
                Assert.IsTrue(mask.Blocker.raycastTarget, "阻断面吃掉射线（§6.2 射线阻断）");
                Assert.AreEqual("读取中…", mask.Message.text, "文案已设");
            }
        }

        // ---- 错误/重试 ----

        [Test]
        public void 错误_可重试_选重试返回true()
        {
            var task = _feedback.ShowErrorAsync("连接失败", "无法连接服务器", retryable: true, mergeKey: "e1");
            UIDialog dialog = PumpUntilDialog("error");

            dialog.OkButton.onClick.Invoke();
            Assert.IsTrue(PumpAwait(task), "选重试 → true");
            Assert.IsFalse(_ui.IsOpen(ErrorFormId), "结果落定后弹窗收口");
        }

        [Test]
        public void 错误_不可重试_取消路径返回false()
        {
            var task = _feedback.ShowErrorAsync("损坏", "文件损坏", retryable: false, mergeKey: "e2");
            UIDialog dialog = PumpUntilDialog("error");

            dialog.CancelButton.onClick.Invoke();         // 不可重试：Cancel = 退出/确认
            Assert.IsFalse(PumpAwait(task), "非重试路径 → false");
        }

        [Test]
        public void 错误_同MergeKey重复弹窗_合并不堆叠()
        {
            var t1 = _feedback.ShowErrorAsync("断线", "正在重连", retryable: true, mergeKey: "disconnect");
            UIDialog dialog = PumpUntilDialog("error");
            Assert.IsTrue(_ui.IsOpen(ErrorFormId));

            var t2 = _feedback.ShowErrorAsync("断线", "正在重连", retryable: true, mergeKey: "disconnect");
            Assert.AreEqual(UniTaskStatus.Pending, t2.Status, "合并等待中");
            Assert.AreEqual(0, _dialogs.QueuedCount, "不产生第二条队列条目（§4.3 不堆叠）");

            dialog.OkButton.onClick.Invoke();
            Assert.IsTrue(PumpAwait(t1));
            Assert.IsTrue(PumpAwait(t2), "合并等待者共享结果");
        }

        // ---- Toast：多条并存 ----

        [Test]
        public void Toast_多条并存_各自计时独立消失()
        {
            Toast toast = WarmToast();
            _feedback.ShowToast("第一条", seconds: 1f);
            _feedback.ShowToast("第二条", seconds: 5f);
            Pump(40);

            Assert.AreEqual(2, toast.VisibleCount, "多条并存——新提示不替换旧提示");
            Assert.AreEqual("第一条", _feedback.CurrentToast, "取最早一条");

            toast.Tick(1.5f);                             // 只推进第一条到期
            Assert.AreEqual(1, toast.VisibleCount, "第一条独立消失");
            Assert.AreEqual("第二条", _feedback.CurrentToast, "第二条计时未被重置");

            toast.Tick(4f);
            Assert.AreEqual(0, toast.VisibleCount, "第二条自行到期");
            Assert.AreEqual("", _feedback.CurrentToast);
        }

        [Test]
        public void Toast_超上限_淘汰最旧并计数()
        {
            Toast toast = WarmToast();
            toast.MaxVisible = 3;
            for (int i = 0; i < 5; i++) { _feedback.ShowToast("T" + i, seconds: 5f); Pump(20); }

            Assert.AreEqual(3, toast.VisibleCount, "数量有上限（§6.1）");
            Assert.AreEqual(2, toast.DroppedCount, "淘汰可观察（§6.1 反馈可观察）");
            Assert.AreEqual("T2", _feedback.CurrentToast, "被淘汰的是最旧两条");
        }

        [Test]
        public void Toast_纵向堆叠_位置随序递增不重叠()
        {
            Toast toast = WarmToast();
            _feedback.ShowToast("A", seconds: 5f);
            _feedback.ShowToast("B", seconds: 5f);
            Pump(40);

            Assert.AreEqual(2, toast.VisibleCount);
            Transform host = toast.transform;
            // 可见条目 = 承载下已激活的子物体（模板本身非激活，不计）
            var visible = new List<RectTransform>();
            for (int i = 0; i < host.childCount; i++)
            {
                var child = host.GetChild(i) as RectTransform;
                if (child != null && child.gameObject.activeSelf) visible.Add(child);
            }
            Assert.AreEqual(2, visible.Count, "两条可见");
            Assert.AreNotEqual(visible[0].anchoredPosition, visible[1].anchoredPosition,
                "堆叠排布——两条不在同一位置（不重叠）");
        }

        // ---- Toast 池收编（《对象池专项设计》§5：Stack → 内核池，maxIdle=MaxVisible）----

        [Test]
        public void Toast_归还条入池_复用回落_池深受MaxVisible约束()
        {
            Toast toast = WarmToast();
            Assert.AreEqual(1, toast.PooledCount, "预热条清场后归池（懒池随首次 Show 定型）");

            for (int i = 0; i < 4; i++) { _feedback.ShowToast("T" + i, seconds: 5f); Pump(20); }
            Assert.AreEqual(4, toast.VisibleCount);
            Assert.AreEqual(0, toast.PooledCount, "全在活跃：池空");

            toast.Tick(999f);                                  // 全部到期 → 归池（Retire 复位进内核回调）
            Assert.AreEqual(0, toast.VisibleCount);
            Assert.AreEqual(4, toast.PooledCount, "四条全归池且不超 maxIdle=MaxVisible");

            _feedback.ShowToast("复用条", seconds: 5f); Pump(20);
            Assert.AreEqual(3, toast.PooledCount, "池中直取复用（不再 Instantiate）");
            Assert.AreEqual(1, toast.VisibleCount);
        }
    }
}
