using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XLua;
using LiteClient;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 页面状态语义 PlayMode 用例（《框架先行》§4 包③退出条件"…**暂停**…可验证"；
    /// 《UI测试开发专项设计》§4.3"状态断言至少包括 Covered/Paused/Visible"）。
    ///
    /// 覆盖两件 EditMode 测不到的事：
    /// - **暂停**：`Pause` 后 OnPause 回调、且 **OnUpdate 停止派发**（§8.1"分级驱动：OnUpdate 仅 Active 态"）；
    ///   `Resume` 走 OnShow（`UIFormState.Paused` 注释："恢复走 OnShow"）。
    /// - **覆盖**：**另一页**全屏打开 → 本页 Covered → OnCover；关闭后者 → OnReveal。
    ///   同层全屏触发的是 **Replace**（入库/出库），必须是**跨层**才走 Covered 语义——故 BaselineA 在
    ///   layer 0、BaselineB 在 layer 2（见 `#uiform` 表）。
    ///
    /// 真实资源：真 Lua（`Baseline` 无 LuaPath，用空路径降级）+ 真 prefab + 真转场。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class UiStatePlayModeTests
    {
        private const int PageA = 204;
        private const int PageB = 205;

        private sealed class StaticCatalog : IUIFormCatalog
        {
            // 非空 LuaPath：本用例走 **resolver 注入**（类层计数替身，不落盘、不建 LuaEnv）。
            // 空 LuaPath 会被 ResolveLogic 短路成 NullUIFormLogic（"无业务逻辑的展示面"口径，
            // 那条路径由 FeedbackSurface 用例覆盖），此处要的是可计数的逻辑层。
            private const string InjectedLogicPath = "Tests.CountingLogic";

            private readonly Dictionary<int, UIFormInfo> _rows = new Dictionary<int, UIFormInfo>();
            public void Add(int id, int layer, bool fullScreen, string location)
                => _rows[id] = new UIFormInfo
                { Id = id, Layer = layer, FullScreen = fullScreen, LuaPath = InjectedLogicPath, Location = location };
            public UIFormInfo Get(int id)
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

        /// <summary>计数用的 UI 逻辑（本用例验证的是**壳的状态语义**，逻辑层用类替身计数——
        /// 真 Lua 面已由 LuaPagePlayModeTests 覆盖，此处不重复）。</summary>
        private sealed class CountingLogic : IUIFormLogic
        {
            public int Updates, Pauses, Covers, Reveals, Shows, Hides;
            public void OnInit(UIForm form, IUIData data) { }
            public void OnShow(IUIData data) => Shows++;
            public void OnUpdate(float dt) => Updates++;
            public void OnPause() => Pauses++;
            public void OnCover() => Covers++;
            public void OnReveal() => Reveals++;
            public void OnHide() => Hides++;
        }

        private PlayModeTestScope _scope;
        private UIService _ui;
        private bool _assetsReady;
        private readonly Dictionary<int, CountingLogic> _logics = new Dictionary<int, CountingLogic>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(UiStatePlayModeTests));
            if (!_assetsReady)
            {
                var init = AssetService.InitAsync();
                yield return Wait(init, 60f);
                _assetsReady = true;
            }

            var catalog = new StaticCatalog();
            catalog.Add(PageA, layer: 0, fullScreen: true, "Assets/UI/Screens/BaselineA.prefab");
            catalog.Add(PageB, layer: 2, fullScreen: true, "Assets/UI/Screens/BaselineB.prefab");

            _logics.Clear();
            _ui = new UIService(catalog,
                logicResolver: info =>
                {
                    if (!_logics.TryGetValue(info.Id, out var logic)) _logics[info.Id] = logic = new CountingLogic();
                    return logic;
                },
                loadPrefab: (loc, ct) => LoadLeaseAsync(loc, ct),
                transitionStrategy: new FadeSlideTransition());   // 生产同款（默认已改零动效，见 §5.1）

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

        // ---- 暂停段 ----

        [UnityTest]
        public IEnumerator 暂停_Pause回调且OnUpdate停派发_Resume后恢复()
        {
            var open = _ui.ShowAsync(PageA);
            yield return Wait(open, 20f);
            Assert.IsFalse(open.Status == UniTaskStatus.Pending, "页面 A 打开超时");

            var logic = _logics[PageA];
            yield return Frames(3);
            int updatesWhileActive = logic.Updates;
            Assert.Greater(updatesWhileActive, 0, "Active 态应派发 OnUpdate（§8.1）");

            _ui.Pause(PageA);
            // **同步派发**：Pause → UIForm.EnterPaused → SafeCall(OnPause) 当场执行，不等帧末
            //（帧末只用于转场推进 UIService.Tick 的 _transitions）
            Assert.AreEqual(1, logic.Pauses, "OnPause 同步恰好一次");

            int updatesAtPause = logic.Updates;
            yield return Frames(5);
            Assert.AreEqual(updatesAtPause, logic.Updates, "Paused 态**不得**派发 OnUpdate（§8.1 分级驱动）");

            int showsBefore = logic.Shows;
            _ui.Resume(PageA);
            Assert.Greater(logic.Shows, showsBefore, "恢复走 OnShow（EnterActiveFromPaused，同步）");
            yield return Frames(3);
            Assert.Greater(logic.Updates, updatesAtPause, "恢复后 OnUpdate 继续派发");
        }

        [UnityTest]
        public IEnumerator 暂停_Close覆盖Paused态()
        {
            var open = _ui.ShowAsync(PageA);
            yield return Wait(open, 20f);
            _ui.Pause(PageA);
            yield return Frames(2);
            Assert.IsTrue(_ui.IsOpen(PageA), "Paused 仍算打开（IsOpen = Active/Covered/Paused）");
            var close = _ui.CloseAsync(PageA, UIService.CloseReason.User);
            yield return Wait(close, 20f);
            Assert.IsFalse(_ui.IsOpen(PageA), "Close 必须覆盖 Paused（§4.4）");
            Assert.AreEqual(1, _logics[PageA].Hides, "关闭走 OnHide 一次");
        }

        // ---- 覆盖段 ----

        [UnityTest]
        public IEnumerator 覆盖_跨层全屏_下层转Covered并回调_关闭后Reveal()
        {
            var openA = _ui.ShowAsync(PageA);
            yield return Wait(openA, 20f);
            Assert.IsTrue(_ui.IsOpen(PageA), "A 已打开");

            var openB = _ui.ShowAsync(PageB);              // B 在更高层且全屏 → A 应转 Covered
            yield return Wait(openB, 20f);
            Assert.IsFalse(openB.Status == UniTaskStatus.Pending, "B 打开超时");

            yield return Frames(3);
            var logicA = _logics[PageA];
            Assert.GreaterOrEqual(logicA.Covers, 1, "下层全屏被更高层全屏遮盖 → OnCover（§6.2）");
            Assert.AreEqual(0, logicA.Reveals, "此时尚未揭示");

            int updatesAtCover = logicA.Updates;
            yield return Frames(4);
            Assert.AreEqual(updatesAtCover, logicA.Updates, "Covered 态不得派发 OnUpdate");

            var closeB = _ui.CloseAsync(PageB, UIService.CloseReason.User);
            yield return Wait(closeB, 20f);
            yield return Frames(3);
            Assert.GreaterOrEqual(logicA.Reveals, 1, "遮盖源关闭 → 下层 OnReveal（配对，§6.2）");
            Assert.IsTrue(_ui.IsOpen(PageA), "A 仍在（关 B 不影响 A）");
        }

        // ---- 辅助 ----

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
