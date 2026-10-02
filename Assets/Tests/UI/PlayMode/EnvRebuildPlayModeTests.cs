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
using LiteClient;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 环境重建 / 循环计数 / 诊断关联 PlayMode 用例（三块准入项证据的 UI 侧载体）：
    ///
    /// - **DevReload 环境重建段**（§10.2）：复刻《UI总设计》钉死的顺序——全关 → `DropAllLogic` →
    ///   env.Dispose → 新 env/重 require → 重新打开。断言**缓存实例接管新逻辑**：复用不重建 GameObject，
    ///   新逻辑来自新 env（旧 env 已 Dispose——若复用路径触碰旧 LuaFunction 必抛"死环境"异常）。
    ///   这正是 `DropAllLogic`（"env 重建前清旧引用"）在真实复用路径上的验证——
    ///   EditMode 无法验证"env 死亡时点"（无真 LuaEnv）。
    ///
    /// - **循环计数**（准入项「资源与缓存稳定」的 UI 侧证据，§12.1"只验证最终状态不够，需断言
    ///   回调次数、引用/订阅数量"）：同页开关 10 次——租约恰好一次（缓存复用不增长）、OnShow 计数
    ///   逐次递增、无第二实例。
    ///
    /// - **诊断关联**（准入项「可诊断」，§4.1"回调异常必须有 formId、阶段和内容版本"）：
    ///   注入 OnInit 失败 → 类型化 `UIOpenException{InitFailed}` 携带 formId 与阶段描述、回滚对称。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class EnvRebuildPlayModeTests
    {
        private const int UIMainFormId = 1;

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

        private sealed class CountingLease : IUIPrefabLease
        {
            public static int Acquired, Released;
            private readonly GameObject _prefab;
            public CountingLease(GameObject prefab) { _prefab = prefab; Acquired++; }
            public GameObject Prefab => _prefab;
            public void Release() { Released++; }
        }

        private PlayModeTestScope _scope;
        private LuaEnv _env;                    // 可变：环境重建用例在 Dispose 后换新 env（resolver 读字段）
        private LuaTable _module;               // 可变：随 env 一起换新 require 结果
        private UIService _ui;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(EnvRebuildPlayModeTests));
            CountingLease.Acquired = 0;
            CountingLease.Released = 0;

            if (!_assetsReady)
            {
                var init = AssetService.InitAsync();
                yield return WaitUniTask(init, 60f);
                Assert.IsFalse(init.Status == UniTaskStatus.Pending, "资源包初始化超时（60s）");
                _assetsReady = true;
            }

            (_env, _module) = NewEnvWithModule("UI.UIMain");

            _ui = new UIService(
                new StaticCatalog(new[]
                {
                    new UIFormInfo { Id = UIMainFormId, Layer = 0, FullScreen = true,
                        LuaPath = "UI.UIMain", Location = "Assets/UI/Screens/UIMain.prefab" },
                }),
                // resolver 读**实例字段**——env 重建后同一 UIService 的复用路径会解析到新 env 的模块
                logicResolver: info => new LuaBehaviourAdapter(_env, _module),
                loadPrefab: LoadRealPrefabAsync,
                transitionStrategy: new FadeSlideTransition());   // 生产同款（默认已改零动效，见 §5.1）

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
        public IEnumerator 环境重建_复刻DevReload顺序_缓存实例接管新逻辑零旧访问()
        {
            var open = _ui.ShowAsync(UIMainFormId);
            yield return WaitUniTask(open, 20f);
            var first = open.GetAwaiter().GetResult();
            var firstGo = first.Root;
            Assert.AreEqual(1, LuaShows(_env), "env1 首开 OnShow 一次");

            var close = _ui.CloseAsync(UIMainFormId, UIService.CloseReason.User);
            yield return WaitUniTask(close, 20f);
            Assert.IsFalse(_ui.IsOpen(UIMainFormId), "已关闭入池（缓存实例仍在）");

            // --- 复刻 §10.2 顺序：DropAllLogic（env.Dispose 前清旧引用）→ env 死亡 → 新 env/重 require ---
            _ui.DropAllLogic();
            _env.Dispose();                              // 旧 env 死亡时点——旧 LuaFunction 全部失效
            (_env, _module) = NewEnvWithModule("UI.UIMain");   // 重预载/重 require（模块计数归零）

            var reopen = _ui.ShowAsync(UIMainFormId);      // 复用缓存实例：新逻辑必须来自新 env
            yield return WaitUniTask(reopen, 20f);
            var second = reopen.GetAwaiter().GetResult();

            Assert.AreSame(firstGo, second.Root,
                "env 重建后复用同一 GameObject（缓存实例不重建——重建=丢复用语义）");
            Assert.AreEqual(1, LuaShows(_env),
                "新 env 的 OnShow 恰好一次（新逻辑接管；若复用路径触碰旧 env 的 LuaFunction 会抛死环境异常）");
            Assert.IsTrue(_ui.IsOpen(UIMainFormId), "重建后页面正常打开");
        }

        [UnityTest]
        public IEnumerator 循环开关_同页十次_租约与计数回基线()
        {
            const int Loops = 10;
            var first = default(UIForm);
            for (int i = 0; i < Loops; i++)
            {
                var open = _ui.ShowAsync(UIMainFormId);
                yield return WaitUniTask(open, 20f);
                var form = open.GetAwaiter().GetResult();
                if (i == 0) first = form;
                Assert.AreSame(first, form, $"第 {i + 1} 次打开必须复用同一实例（无第二实例=无泄漏实例）");
                Assert.AreEqual(i + 1, LuaShows(_env), $"OnShow 逐次递增（第 {i + 1} 次应计 {i + 1}）");

                var close = _ui.CloseAsync(UIMainFormId, UIService.CloseReason.User);
                yield return WaitUniTask(close, 20f);
            }

            // 「资源与缓存稳定」循环判据：计数回基线、无持续增长（§12.1 断言回调次数与引用数量）
            Assert.AreEqual(1, CountingLease.Acquired, "十次开关租约恰好一次（缓存复用不获取第二份租约）");
            Assert.AreEqual(0, CountingLease.Released, "缓存实例寿命内租约不释放（§5.2 租约覆盖缓存期）");
            Assert.IsFalse(_ui.IsOpen(UIMainFormId), "最终关闭");
            Assert.AreEqual(Loops, LuaShows(_env), "回调计数=循环次数（无重复/遗漏派发）");

            var shutdown = _ui.ShutdownAsync();           // 收尾销毁：对称释放
            yield return WaitUniTask(shutdown, 20f);
            Assert.AreEqual(1, CountingLease.Released, "Shutdown 释放租约——引用归零对称");
        }

        [UnityTest]
        public IEnumerator 诊断关联_注入初始化失败_异常含formId与阶段且回滚对称()
        {
            // 注入 OnInit 失败（诊断样例：一次注入失败必须可定位——formId、阶段、回滚状态）。
            // **断言通道（踩坑记录）**：SafeCall 走 LiteFramework.Log（环形缓冲 + ErrorCount，
            // helper 未注入时不桥接 UnityEngine 日志）——LogAssert 看不到它。可诊断断言用
            // **产品诊断面**：ErrorCount 增量 + Recent 缓冲内容（含 formId/阶段/异常类型与内容），
            // 与 EditMode 先例（UiU0EditModeTests 的 ErrorCount 口径）一致。
            int errBefore = LiteFramework.Log.ErrorCount;

            _env.DoString(@"
local m = require('UI.UIMain')
m.OnInit = function(self, ctx, data) error('诊断注入:OnInit 失败') end");

            var open = _ui.ShowAsync(UIMainFormId);
            yield return WaitUniTask(open, 20f);
            Assert.AreNotEqual(UniTaskStatus.Pending, open.Status, "打开必须终态（不悬挂）");

            var ex = Assert.Throws<UIOpenException>(() => open.GetAwaiter().GetResult(),
                "初始化失败必须类型化交付（§4.1 初始化失败不得以 Active+NullLogic 伪装成功）");
            Assert.AreEqual(UIOpenFailure.InitFailed, ex.Reason, "失败分类 InitFailed");
            Assert.AreEqual(UIMainFormId, ex.FormId, "异常携带 formId（§4.1 回调异常必须有 formId）");
            StringAssert.Contains("初始化失败", ex.Message, "异常消息含阶段（可定位到初始化阶段）");
            StringAssert.Contains($"UIForm[{UIMainFormId}]", ex.Message, "异常消息含页面标识（诊断关联）");

            Assert.IsFalse(_ui.IsOpen(UIMainFormId), "回滚：失败页不残留打开态");
            Assert.AreEqual(CountingLease.Acquired, CountingLease.Released,
                "回滚对称：租约获取与释放一致（半成品清理，§4.4 引用归零）");

            // 可诊断（§4.1）：SafeCall 的错误日志可从产品诊断面读取——含 formId、阶段与注入内容
            Assert.Greater(LiteFramework.Log.ErrorCount, errBefore, "SafeCall 记录 OnInit 异常（ErrorCount 递增）");
            bool logged = false;
            foreach (var entry in LiteFramework.Log.Recent)
            {
                if (entry.Level != LiteFramework.LogLevel.Error) continue;
                if (entry.Message.Contains($"UIForm[{UIMainFormId}].OnInit")
                    && entry.Message.Contains("LuaException")
                    && entry.Message.Contains("诊断注入"))
                { logged = true; break; }
            }
            Assert.IsTrue(logged,
                "错误日志含页面标识、阶段与异常内容（§4.1 可诊断——经 LiteFramework.Log.Recent 可读）");
        }

        // ---- 辅助 ----

        private static (LuaEnv env, LuaTable module) NewEnvWithModule(string requirePath)
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

            var chunk = $@"
local m = require('{requirePath}')
m.__shows = 0
local orig = m.OnShow
m.OnShow = function(self, data) m.__shows = m.__shows + 1; if orig then return orig(self, data) end end
return m";
            var module = (LuaTable)env.DoString(chunk, "envrebuild_wrap")[0];
            return (env, module);
        }

        private int LuaShows(LuaEnv env)
            => Convert.ToInt32(env.DoString("return require('UI.UIMain').__shows")[0]);

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
