using System;
using System.Collections;
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
    /// 真 Lua 页面生命周期 PlayMode 用例（《框架先行》§4 包③退出条件"**真 Lua**/Prefab/
    /// 动画资源组合运行，取消、暂停、**复用**、**卸载**可验证"）。
    ///
    /// 与 `UiU0EditModeTests` 的差异是本组存在的理由：EditMode 版由用例显式泵 Tick，PlayMode 走
    /// 真实 PlayerLoop + 真实转场 runner——**转场期间的状态迁移在 EditMode 下根本不发生**。
    ///
    /// 链路：真 LuaEnv（loader 读磁盘 `LiteGame/Lua/**.lua`）→ `LuaBehaviourAdapter`
    /// （真 ui-API 派发表）→ 真注册表模块 `UI.UIMain` → 真 prefab → `UIService` 真转场。
    ///
    /// 断言口径取 [UI测试开发专项 §4.3](../docs)：页面状态 / 实例 Created·Reused·Destroyed /
    /// 资源 Acquired·Released——**不只断言"最终可见"**。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class LuaPagePlayModeTests
    {
        private const int UIMainFormId = 1;
        private const string UIMainLocation = "Assets/UI/Screens/UIMain.prefab";
        private const string UIMainRequire = "UI.UIMain";

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

        /// <summary>租约替身**只做引用计数**（真释放语义），prefab 本身走真资源链路。</summary>
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
        private LuaEnv _env;
        private UIService _ui;
        private UINavigationController _nav;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(LuaPagePlayModeTests));
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
            LuaTable module = RequireCountedModule(_env, UIMainRequire);

            _ui = new UIService(
                new StaticCatalog(new[]
                {
                    new UIFormInfo { Id = UIMainFormId, Layer = 0, FullScreen = true,
                        LuaPath = UIMainRequire, Location = UIMainLocation },
                }),
                logicResolver: _ => new LuaBehaviourAdapter(_env, module),
                loadPrefab: LoadRealPrefabAsync,
                transitionStrategy: new FadeSlideTransition());   // 生产同款（默认已改零动效，见 §5.1）
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
        public IEnumerator 真Lua页面_冷开_OnShow执行且实例与租约各一次()
        {
            var open = _ui.ShowAsync(UIMainFormId);
            yield return WaitUniTask(open, 20f);
            Assert.IsFalse(open.Status == UniTaskStatus.Pending, "UIMain 冷开超时（真 Lua + 真 prefab + 真转场）");

            UIForm form = open.GetAwaiter().GetResult();
            Assert.IsNotNull(form, "冷开返回实例");
            Assert.AreEqual(1, LuaShows(_env), "真 Lua OnShow 执行且恰好一次");
            Assert.AreEqual(1, CountingLease.Acquired, "冷开获取租约一次");
            Assert.AreEqual(0, CountingLease.Released, "销毁前不得释放（租约覆盖缓存实例寿命，§5.2）");
        }

        [UnityTest]
        public IEnumerator 真Lua页面_关闭后复用_不重复OnInit但重跑OnShow()
        {
            var open = _ui.ShowAsync(UIMainFormId);
            yield return WaitUniTask(open, 20f);
            UIForm first = open.GetAwaiter().GetResult();
            Assert.AreEqual(1, LuaShows(_env), "首开 OnShow 一次");

            var close = _ui.CloseAsync(UIMainFormId, UIService.CloseReason.User);
            yield return WaitUniTask(close, 20f);
            Assert.IsFalse(_ui.IsOpen(UIMainFormId), "已关闭");

            var reopen = _ui.ShowAsync(UIMainFormId);
            yield return WaitUniTask(reopen, 20f);
            Assert.IsFalse(reopen.Status == UniTaskStatus.Pending, "复用打开超时");
            UIForm second = reopen.GetAwaiter().GetResult();

            Assert.AreSame(first, second, "复用同一实例（Created 后 Reused，不是新建）");
            Assert.AreEqual(2, LuaShows(_env), "复用重跑 OnShow（§4.2 首次与复用合流：省实例初始化不省显示）");
            Assert.AreEqual(1, CountingLease.Acquired, "复用不再获取第二份租约");
        }

        [UnityTest]
        public IEnumerator 真Lua页面_Shutdown_释放租约且旧实例不可访问()
        {
            var open = _ui.ShowAsync(UIMainFormId);
            yield return WaitUniTask(open, 20f);
            Assert.AreEqual(1, CountingLease.Acquired);

            var shutdown = _ui.ShutdownAsync();
            yield return WaitUniTask(shutdown, 20f);
            Assert.IsFalse(shutdown.Status == UniTaskStatus.Pending, "Shutdown 超时（§4.4 全链释放）");

            Assert.AreEqual(1, CountingLease.Released, "Shutdown 释放租约（引用归零）");
            Assert.IsFalse(_ui.IsOpen(UIMainFormId), "旧实例不再打开");
        }

        [UnityTest]
        public IEnumerator 真Lua页面_打开中关闭_在途被权威取消且不残留()
        {
            // 加载口显式让出一帧 → 在途窗口确定性存在（见 LoadRealPrefabAsync）。
            var open = _ui.ShowAsync(UIMainFormId);
            Assert.AreEqual(UniTaskStatus.Pending, open.Status, "首帧应处于在途");

            var close = _ui.CloseAsync(UIMainFormId, UIService.CloseReason.User);
            yield return WaitUniTask(close, 20f);
            yield return WaitUniTask(open, 20f);

            Assert.AreNotEqual(UniTaskStatus.Pending, open.Status, "在途 Show 必须收到终态（不悬挂）");
            Assert.IsFalse(_ui.IsOpen(UIMainFormId), "权威取消后页面不得残留打开态");

            // 取消必须**类型化**（§4.3）：不是静默吞掉，也不是普通异常
            var ex = Assert.Throws<UIOpenException>(() => open.GetAwaiter().GetResult(),
                "取消应交付 UIOpenException 而非悬挂/静默");
            Assert.AreEqual(UIOpenFailure.Canceled, ex.Reason, "失败分类为 Canceled");

            // 半成品已回滚：租约获取与回归必须对称（§4.4 引用归零）
            Assert.AreEqual(CountingLease.Acquired, CountingLease.Released,
                "取消路径租约对称（获取即释放，不留半成品）");
            Assert.AreEqual(0, LuaShows(_env), "取消路径不得跑过 OnShow（页面从未完成展示）");
        }

        // ---- 辅助（口径同 UiU0EditModeTests，此处走真实 PlayerLoop）----

        /// <summary>真 LuaEnv：loader 读磁盘 `Assets/LiteGame/Lua/**.lua`，并绑 log 全局表。</summary>
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

        /// <summary>require 真模块并挂 __shows 计数钩子（包装在类表层，不改仓库 Lua 文件）。</summary>
        private static LuaTable RequireCountedModule(LuaEnv env, string requirePath)
        {
            var chunk = $@"
local m = require('{requirePath}')
m.__shows = 0
local orig = m.OnShow
m.OnShow = function(self, data) m.__shows = m.__shows + 1; if orig then return orig(self, data) end end
return m";
            var result = env.DoString(chunk, "pm_test_wrap");
            Assert.AreEqual(1, result.Length, "包装 require 应返回模块表");
            return (LuaTable)result[0];
        }

        private static int LuaShows(LuaEnv env)
            => Convert.ToInt32(env.DoString($"return require('{UIMainRequire}').__shows")[0]);

        private static async UniTask<IUIPrefabLease> LoadRealPrefabAsync(string location, CancellationToken ct)
        {
            // 让出一帧再加载：把"在途窗口"变成**确定性**的，取消用例才测得到取消语义
            // （真实 IO 若同帧完成，CloseAsync 走的是"已打开"的转场关闭路径）。
            // 这不是放宽断言——是让用例测量的正是它声称测量的东西（UI-U2"最小可复现"口径）。
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
