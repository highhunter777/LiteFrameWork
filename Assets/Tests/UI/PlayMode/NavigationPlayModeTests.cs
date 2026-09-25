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

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 导航真资源段 PlayMode 用例（包③覆盖度余部：§6.1 Go/Back/Replace 三操作的真链路 +
    /// §6.3 输入锁"接受转场即生效、恢复为协调者计算值"）。
    ///
    /// 覆盖 EditMode 测不到的两件事：
    /// - **覆盖栈语义**：Go 保留下层页（新页覆盖其上），Back 关顶页即"返回"——多步往返在真实
    ///   层级组/统一排序/转场并发下成立（EditMode 是替身栈）；
    /// - **转场锁时序**：<see cref="ObservingTransition"/> 在策略执行的**第一帧**记录
    ///   interactable——锁在阶段 OnEnter 已生效（"接受即锁，不等策略开始"），完成由 Runner 收尾
    ///   按协调者恢复——这依赖真实 PlayerLoop 驱动 UITransitionRunner。
    ///
    /// 页面：UIMain（真 Lua）+ BaselineA/B（空 LuaPath 展示面——ResolveLogic 短路降级）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class NavigationPlayModeTests
    {
        private const int PageMain = 1;   // UIMain：真 Lua 页面
        private const int PageA = 10;     // BaselineA：展示面（空 LuaPath）
        private const int PageB = 20;     // BaselineB：展示面
        // **层级语义（踩坑记录）**：前进页 A/B 必须与底页 Main **跨层**（1 vs 0）——
        // Go 的"覆盖栈语义"（旧页保留转 Covered）只在跨层成立；若配成同层全屏，
        // UIService 按组内契约"复用_同组已有全屏_判定Replace"关闭底页（U0 批交付语义），
        // "Go 不关下层"即假红。A/B 同层（1）表达 Replace 的"替换当前记录"位置语义。

        private sealed class StaticCatalog : IUIFormCatalog
        {
            private readonly UIFormInfo[] _rows;
            public StaticCatalog(UIFormInfo[] rows) => _rows = rows;
            public UIFormInfo Get(int id)
            {
                foreach (var row in _rows) if (row.Id == id) return row;
                throw new KeyNotFoundException($"StaticCatalog 无行:{id}");
            }
        }

        private sealed class CountingLease : IUIPrefabLease
        {
            public static int Acquired, Released;
            private readonly GameObject _prefab;
            public CountingLease(GameObject prefab) { _prefab = prefab; Acquired++; }
            public GameObject Prefab => _prefab;
            public void Release() { Released++; }
        }

        /// <summary>观测转场（输入锁用例）：策略执行第一帧记录两页的 interactable/blocksRaycasts，
        /// 指定观察页被门住直到放行——窗口确定性（锁时序断言不靠碰运气）。非观察页即时完成。</summary>
        private sealed class ObservingTransition : ITransitionStrategy
        {
            public readonly List<string> Observations = new List<string>();
            public int GateFormId = -1;
            private UniTaskCompletionSource<bool> _gate;

            public UniTask PlayShow(UIForm form, CancellationToken ct)
            {
                Record("show", form);
                return GateAsync(form);
            }

            public UniTask PlayClose(UIForm form, CancellationToken ct)
            {
                Record("close", form);
                return GateAsync(form);
            }

            private void Record(string phase, UIForm form)
            {
                var cg = form.CanvasGroup;
                Observations.Add($"{phase}:{form.Id}:interactable={cg.interactable}:blocksRaycasts={cg.blocksRaycasts}");
            }

            private UniTask GateAsync(UIForm form)
                => form.Id == GateFormId && _gate != null ? _gate.Task : UniTask.CompletedTask;

            public void HoldNext(int formId)
            {
                GateFormId = formId;
                _gate = new UniTaskCompletionSource<bool>();
            }

            public void Release() => _gate?.TrySetResult(true);
        }

        private PlayModeTestScope _scope;
        private LuaEnv _env;
        private UIService _ui;
        private UINavigationController _nav;
        private ObservingTransition _transition;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(NavigationPlayModeTests));
            CountingLease.Acquired = 0;
            CountingLease.Released = 0;

            if (!_assetsReady)
            {
                var init = AssetService.InitAsync();
                yield return WaitUniTask(init, 60f);
                Assert.IsFalse(init.Status == UniTaskStatus.Pending, "资源包初始化超时（60s）");
                _assetsReady = true;
            }

            _env = NewLuaEnv();
            LuaTable module = RequireCountedModule(_env, "UI.UIMain");

            _transition = new ObservingTransition();
            _ui = new UIService(
                new StaticCatalog(new[]
                {
                    new UIFormInfo { Id = PageMain, Layer = 0, FullScreen = true,
                        LuaPath = "UI.UIMain", Location = "Assets/UI/Screens/UIMain.prefab" },
                    new UIFormInfo { Id = PageA, Layer = 1, FullScreen = true,
                        LuaPath = "", Location = "Assets/UI/Screens/BaselineA.prefab" },
                    new UIFormInfo { Id = PageB, Layer = 1, FullScreen = true,
                        LuaPath = "", Location = "Assets/UI/Screens/BaselineB.prefab" },
                }),
                transitionStrategy: _transition,
                logicResolver: info => string.IsNullOrEmpty(info.LuaPath)
                    ? null   // 空 LuaPath 由 ResolveLogic 短路 NullUIFormLogic（本 resolver 不会被调）
                    : new LuaBehaviourAdapter(_env, module),
                loadPrefab: LoadRealPrefabAsync);
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
            _env?.Dispose();
            _env = null;
            yield return _scope.DisposeAsync();
        }

        // ---- 用例 ----

        [UnityTest]
        public IEnumerator 导航_Go前进Back返回_下层保留且复用同一实例()
        {
            var goMain = _nav.GoAsync(PageMain);
            yield return WaitUniTask(goMain, 20f);
            var main = goMain.GetAwaiter().GetResult();
            Assert.IsTrue(_ui.IsOpen(PageMain), "底页打开");

            var goA = _nav.GoAsync(PageA);                 // Go 前进：新页覆盖，旧页保留（§6.1）
            yield return WaitUniTask(goA, 20f);
            Assert.IsTrue(_ui.IsOpen(PageA), "前进页打开");
            Assert.IsTrue(_ui.IsOpen(PageMain), "Go 不关下层（覆盖栈语义——区别于 Replace）");

            var backTop = _nav.BackAsync();                // Back 关闭顶页（异步任务须等终态再取结果）
            yield return WaitUniTask(backTop, 20f);
            Assert.IsTrue(ResultOf(backTop), "Back 提交关闭");
            Assert.IsFalse(_ui.IsOpen(PageA), "Back 关闭顶页");
            Assert.IsTrue(_ui.IsOpen(PageMain), "Back 返回后下层仍在");

            var backToBottom = _nav.BackAsync();            // 再 Back 关底页（进池）
            yield return WaitUniTask(backToBottom, 20f);
            Assert.IsFalse(_ui.IsOpen(PageMain), "Back 到底：底页关闭入池");

            int acquiredBeforeReopen = CountingLease.Acquired;   // 两页（Main+A）各持一份租约——基线为 2
            var reopen = _nav.GoAsync(PageMain);            // 重走打开 = 缓存复用（§4.2）
            yield return WaitUniTask(reopen, 20f);
            Assert.AreSame(main, reopen.GetAwaiter().GetResult(), "重开复用同一实例（不是重建）");
            Assert.AreEqual(acquiredBeforeReopen, CountingLease.Acquired,
                $"复用不获取新租约（重开前后 Acquired 不变，基线={acquiredBeforeReopen}——两页各一份）");
        }

        [UnityTest]
        public IEnumerator 导航_Replace_显式替换当前记录_旧页关闭底层保留()
        {
            var goMain = _nav.GoAsync(PageMain);
            yield return WaitUniTask(goMain, 20f);
            var goA = _nav.GoAsync(PageA);
            yield return WaitUniTask(goA, 20f);
            Assert.IsTrue(_ui.IsOpen(PageMain) && _ui.IsOpen(PageA), "两页就位");

            var replace = _nav.ReplaceAsync(PageB);        // 替换当前记录（A→B）
            yield return WaitUniTask(replace, 20f);

            Assert.IsTrue(_ui.IsOpen(PageB), "新页打开");
            Assert.IsFalse(_ui.IsOpen(PageA), "被替换页关闭（Replace 离场）——Go 不会关它");
            Assert.IsTrue(_ui.IsOpen(PageMain), "底层页不受替换影响");

            var backB = _nav.BackAsync();                  // Back 关闭替换页（异步任务须等终态再取结果）
            yield return WaitUniTask(backB, 20f);
            Assert.IsTrue(ResultOf(backB), "Back 提交关闭");
            Assert.IsFalse(_ui.IsOpen(PageB), "Back 关闭替换页");
            Assert.IsTrue(_ui.IsOpen(PageMain), "Back 回到底层页");
        }

        [UnityTest]
        public IEnumerator 导航_Replace_目标即当前顶_幂等返回同一实例()
        {
            var go = _nav.GoAsync(PageMain);
            yield return WaitUniTask(go, 20f);
            var first = go.GetAwaiter().GetResult();

            var replace = _nav.ReplaceAsync(PageMain);   // 当前记录已是目标
            yield return WaitUniTask(replace, 20f);

            Assert.AreSame(first, replace.GetAwaiter().GetResult(), "幂等返回同一实例（不先关后开）");
            Assert.IsTrue(_ui.IsOpen(PageMain), "仍打开");
            Assert.AreEqual(1, CountingLease.Acquired, "幂等路径不产生第二次加载/租约");
        }

        [UnityTest]
        public IEnumerator 转场输入锁_接受即锁_完成按协调者恢复且全程遮挡()
        {
            _transition.HoldNext(PageMain);              // 观察页的转场被门住——锁时序窗口确定性

            var go = _nav.GoAsync(PageMain);
            yield return null;                            // 走一帧：转场阶段 OnEnter（接受即锁）→ 策略开始

            // 锁在**接受转场时**生效（阶段 OnEnter），不等策略完成——观测点在策略第一帧
            Assert.GreaterOrEqual(_transition.Observations.Count, 1, "转场策略已开始（观测点已记录）");
            Assert.IsTrue(_transition.Observations[0].Contains("interactable=False"),
                $"转场中必须锁 interactable（§6.3 接受即锁）——观测：{_transition.Observations[0]}");
            Assert.IsTrue(_transition.Observations[0].Contains("blocksRaycasts=True"),
                "职责分离：转场锁只锁 interactable，blocksRaycasts 全程保持遮挡（U1-③）");

            _transition.Release();                       // 放行转场
            yield return WaitUniTask(go, 20f);

            var form = go.GetAwaiter().GetResult();
            Assert.IsTrue(form.CanvasGroup.interactable, "转场完成按协调者恢复交互（不无条件写 true——本页无暂停/模态即开）");
            Assert.IsTrue(form.CanvasGroup.blocksRaycasts, "完成态可命中");
        }

        // ---- 辅助（口径同 LuaPagePlayModeTests，此处走真实 PlayerLoop）----

        private static bool ResultOf(UniTask<bool> task)
        {
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "Bool 任务未完成——用例时序有误");
            return task.GetAwaiter().GetResult();
        }

        private static LuaEnv NewLuaEnv()
        {
            var env = new LuaEnv();
            env.AddLoader((ref string filepath) =>
            {
                var key = filepath.Replace('.', '/');
                var path = Path.Combine(Application.dataPath, "LiteGame/Lua", key + ".lua");
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            });
            var logTable = env.NewTable();
            logTable.Set("info", new Action<string>(msg => LiteFramework.Log.Info(msg, "Lua")));
            logTable.Set("warning", new Action<string>(msg => LiteFramework.Log.Warning(msg, "Lua")));
            logTable.Set("error", new Action<string>(msg => LiteFramework.Log.Error(msg, "Lua")));
            env.Global.Set("log", logTable);
            return env;
        }

        private static LuaTable RequireCountedModule(LuaEnv env, string requirePath)
        {
            var chunk = $@"
local m = require('{requirePath}')
m.__shows = 0
local orig = m.OnShow
m.OnShow = function(self, data) m.__shows = m.__shows + 1; if orig then return orig(self, data) end end
return m";
            var result = env.DoString(chunk, "nav_test_wrap");
            return (LuaTable)result[0];
        }

        private static async UniTask<IUIPrefabLease> LoadRealPrefabAsync(string location, CancellationToken ct)
        {
            await UniTask.Yield(ct);
            GameObject prefab = await AssetService.LoadAssetAsync<GameObject>(location, ct);
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
