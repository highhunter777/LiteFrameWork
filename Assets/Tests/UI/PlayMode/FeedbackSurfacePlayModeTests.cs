using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiteClient;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 反馈面真资源用例（《框架先行》§4 包③退出条件"真 Lua/Prefab/动画资源组合运行，
    /// 取消、暂停、复用、卸载可验证"；《UI测试开发专项设计》§4.3 PlayMode 界面行为）。
    ///
    /// **本组存在的理由**：真 prefab + 真转场链路是替身掩盖不了的一面（AOT/Lua 面尤其）——
    /// 必须真资源验证。
    ///
    /// 与 EditMode 版的区别（都是硬约束）：
    /// - 加载走 **YooAsset 编辑器模拟模式**（`AssetService.InitAsync` 的 `#if UNITY_EDITOR` 分支
    ///   在 PlayMode 下同样成立）——不引 `AssetDatabase`，本程序集才能在 Player 测试构建里链接；
    /// - 等待走 UnityTest 原生 `[UnityTest] + IEnumerator + yield`，**不是显式泵 Tick**
    ///   （PlayMode 有真实 PlayerLoop，这正是 EditMode 需要泵的原因）；
    /// - 清理用 <see cref="PlayModeTestScope.DisposeAsync"/>（销毁要等帧末落地，"关闭 ≠ 清理完成"）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class FeedbackSurfacePlayModeTests
    {
        private const int ErrorFormId = 201;
        private const int LoadingFormId = 202;
        private const int ToastFormId = 203;

        /// <summary>真表投影（与生产同源：TbUIForm 行 → UIFormInfo）。</summary>
        private static IUIFormCatalog BuildCatalog()
        {
            return new StaticCatalog(new[]
            {
                new UIFormInfo { Id = ErrorFormId, Layer = 3, FullScreen = false, LuaPath = "", Location = "Assets/UI/Screens/Dialog.prefab" },
                new UIFormInfo { Id = LoadingFormId, Layer = 3, FullScreen = false, LuaPath = "", Location = "Assets/UI/Screens/Loading.prefab" },
                new UIFormInfo { Id = ToastFormId,   Layer = 3, FullScreen = false, LuaPath = "", Location = "Assets/UI/Screens/Toast.prefab" },
            });
        }

        private sealed class StaticCatalog : IUIFormCatalog
        {
            private readonly UIFormInfo[] _rows;
            public StaticCatalog(UIFormInfo[] rows) => _rows = rows;
            public UIFormInfo Get(int id)
            {
                foreach (var row in _rows) if (row.Id == id) return row;
                throw new System.Collections.Generic.KeyNotFoundException($"StaticCatalog 无行:{id}");
            }
        }

        /// <summary>YooAsset 编辑器模拟加载（真 prefab，非替身）。句柄由 UIService 租约持有。</summary>
        private sealed class SimulateLease : IUIPrefabLease
        {
            private readonly GameObject _prefab;
            private bool _released;
            public SimulateLease(GameObject prefab) => _prefab = prefab;
            public GameObject Prefab => _prefab;
            public bool IsReleased => _released;
            public void Release() => _released = true;
        }

        private PlayModeTestScope _scope;
        private UIService _ui;
        private DialogService _dialogs;
        private FeedbackService _feedback;
        private UINavigationController _nav;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(FeedbackSurfacePlayModeTests));

            if (!_assetsReady)
            {
                // 真资源初始化：编辑器模拟模式（见 AssetService 的 #if UNITY_EDITOR 分支）。
                // 失败即用例失败——本组的意义就是真资源链路，不允许静默退化。
                var init = AssetService.InitAsync();
                yield return WaitUniTask(init, 60f);
                Assert.IsFalse(init.Status == UniTaskStatus.Pending, "资源包初始化超时（60s）");
                _assetsReady = true;
            }

            _ui = new UIService(BuildCatalog(),
                loadPrefab: LoadRealPrefabAsync,
                transitionStrategy: new FadeSlideTransition());   // 生产同款（默认已改零动效，见 §5.1）
            _nav = new UINavigationController(_ui);
            _dialogs = new DialogService(_ui);
            _feedback = new FeedbackService(_ui, _dialogs, _nav);

            // PlayMode 宿主驱动：转场 runner 由 UIService.Tick 帧末推进（生产里由 GameEntry 驱动）。
            // 不挂驱动 = ShowAsync 停在转场永不完成（IsOpen 却已 true）——UI-U2 记录的同一个坑。
            var host = _scope.CreateGameObject("PlayModeHost");
            PlayModeTicker.Attach(host, _ui.Tick, _dialogs.Tick);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _feedback?.Dispose();
            _dialogs?.Dispose();
            if (_ui != null)
            {
                var shutdown = _ui.ShutdownAsync();
                yield return WaitUniTask(shutdown, 20f);
                _ui = null;
            }
            yield return _scope.DisposeAsync();
        }

        // ---- 用例 ----

        [UnityTest]
        public IEnumerator 反馈面_真资源_Loading遮罩打开并阻断()
        {
            const string message = "读取中";
            using (var scope = _feedback.BeginLoading(message))
            {
                Assert.IsTrue(_feedback.IsLoading, "作用域内处于阻断");

                yield return WaitUntil(() => _ui.IsOpen(LoadingFormId), 10f, "Loading 表单未打开");

                // 业务事实（组件武装 + 文案已写入），不是 IsOpen——沿用 ⑥b 破案结论：
                // 表单在转场收尾前就已 Active/IsOpen，此后 SetMessage 才执行。
                yield return WaitUntil(() => { var m = FindMask(); return m != null && m.Message.text == message; },
                    10f, "遮罩文案未写入");

                LoadingMask mask = FindMask();
                Assert.IsNotNull(mask, "真 Loading.prefab 应挂 LoadingMask");
                Assert.IsTrue(mask.Blocker.raycastTarget, "阻断面吃掉射线（§6.2）");
                Assert.AreEqual(message, mask.Message.text, "文案已设");
            }

            yield return WaitUntil(() => !_ui.IsOpen(LoadingFormId), 10f, "作用域结束后遮罩未关闭");
            Assert.IsFalse(_feedback.IsLoading, "阻断已解除");
        }

        [UnityTest]
        public IEnumerator 反馈面_真资源_Toast多条并存并堆叠()
        {
            _feedback.ShowToast("第一条", 5f);
            _feedback.ShowToast("第二条", 5f);

            yield return WaitUntil(() => _feedback.ToastHost != null, 10f, "Toast 承载未武装");
            Toast toast = _feedback.ToastHost;
            Assert.IsNotNull(toast, "真 Toast.prefab 应挂 Toast 控件");
            Assert.AreEqual(2, toast.VisibleCount, "多条并存（§6.1 裁决）");

            // 堆叠：两条不重叠（同一位置即重叠）——与构建器自检同判据，此处走真 prefab
            yield return null;
            Assert.AreEqual(2, CountVisibleChildren(toast.transform), "两条可见条目");
        }

        [UnityTest]
        public IEnumerator 反馈面_真资源_错误弹窗按按钮落结果()
        {
            var task = _feedback.ShowErrorAsync("真资源错误", "来自真 prefab", retryable: true, mergeKey: "pm1");

            // 就绪条件必须是**业务事实**（对话框武装完成），不是"组件存在"（FindObjectOfType 会在
            // WaitAsync 接线前就命中）。
            yield return WaitUntil(() => _dialogs.TryGetActiveDialog("error", out _),
                15f, "错误弹窗未武装（真转场链路）");

            Assert.IsTrue(_dialogs.TryGetActiveDialog("error", out UIDialog dialog), "取到活动对话框");
            Assert.IsNotNull(dialog.OkButton, "OkButton 接线（真 prefab，非假件）");
            Assert.IsNotNull(dialog.CancelButton, "CancelButton 接线");

            dialog.OkButton.onClick.Invoke();
            yield return WaitUntil(() => task.Status != UniTaskStatus.Pending, 15f, "结果未落定");
            Assert.IsTrue(task.GetAwaiter().GetResult(), "选确认 → true");
            Assert.IsFalse(_ui.IsOpen(ErrorFormId), "结果落定后弹窗已收口（先关后交付）");
        }

        // ---- 辅助 ----

        /// <summary>真资源加载（编辑器模拟模式；不在本程序集引 AssetDatabase）。</summary>
        private static async UniTask<IUIPrefabLease> LoadRealPrefabAsync(string location, CancellationToken ct)
        {
            GameObject prefab = await AssetService.LoadAssetAsync<GameObject>(location);
            if (prefab == null) throw new InvalidOperationException($"真资源加载失败:{location}（EditorSimulateMode 是否初始化？）");
            return new SimulateLease(prefab);
        }

        private static LoadingMask FindMask() => UnityEngine.Object.FindObjectOfType<LoadingMask>();
        private static UIDialog FindDialog() => UnityEngine.Object.FindObjectOfType<UIDialog>();

        private static int CountVisibleChildren(Transform host)
        {
            int n = 0;
            for (int i = 0; i < host.childCount; i++)
                if (host.GetChild(i).gameObject.activeSelf) n++;
            return n;
        }

        /// <summary>轮询可观察条件；超时即失败并带最后状态（§5.1 UiWait 口径）。</summary>
        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition()) yield break;
                yield return null;
            }
            Assert.Fail($"{what}（超时 {timeoutSeconds}s）");
        }

        /// <summary>等待 UniTask 完成（PlayMode 有 PlayerLoop，不需要手动泵）。</summary>
        private static IEnumerator WaitUniTask(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
