using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using XLua;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// UI U0 正确性止血验收（《UI框架总设计》§13 U0 行 + §12.1 分层用例）：
    /// ① 真 Lua 首开/关闭/复用（真配置投影 + 真 prefab + 真 LuaEnv + 真注册表，仅资源加载走替身——
    ///    待办总览 §7 "U0 可先 fake loader"）；
    /// ② 复用与首次同管线（复位 alpha/重新排序/复用同判 Replace——UI-02）；
    /// ③ 三层全屏遮盖 + Covered/Paused 可关 + CloseAllOpen 全清（UI-04）；
    /// ④ OnInit/OnShow 失败回滚（无残留、可重试——§4.1）；
    /// ⑤ env 重建零旧引用（DropAllLogic → dispose → 新 env/新注册表 → 复用重解析——§10.2）；
    /// ⑥ VirtualList 窗口复用：有界节点、首尾往返、缩容、Refresh 不重复重绑（UI-07/UI-08）。
    ///
    /// 时序口径与 UiTransitionEditModeTests 相同：假策略 + `Tick(dt)` 手动驱动，await 内联完成，
    /// 零 PlayerLoop 依赖。真 Lua 用例已在 Editor 实测 xlua.dll 可用（U0 探针，总设计 §2.2）。
    /// </summary>
    public sealed class UiU0EditModeTests : UnityTestBase
    {
        private const string UIMainPrefab = "Assets/UI/Screens/UIMain.prefab";
        private const string UIMainRequire = "UI.UIMain";

        [TearDown]
        protected void KillUiRoots()
        {
            // UIService 自建的 [UIRoot] 不经 Scope——按名收尾（先于基类 Scope.Dispose 执行）
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                if (go.name == "[UIRoot]") UnityEngine.Object.DestroyImmediate(go);
        }

        // ---- 替身 ----

        /// <summary>贴真转场：入场把 alpha 收到 1、离场收到 0（复刻 FadeSlideTransition 的 alpha 语义，
        /// 让"复用不复位就不可见"的缺陷可被断言抓到）。</summary>
        private sealed class MimicTransition : ITransitionStrategy
        {
            public int ShowCount, CloseCount;
            public float AlphaAtShowEntry = float.NaN;

            public UniTask PlayShow(UIForm form)
            {
                ShowCount++;
                AlphaAtShowEntry = form.CanvasGroup.alpha;
                form.CanvasGroup.alpha = 1f;
                return UniTask.CompletedTask;
            }

            public UniTask PlayClose(UIForm form)
            {
                CloseCount++;
                form.CanvasGroup.alpha = 0f;
                return UniTask.CompletedTask;
            }
        }

        private sealed class CountingReplace : IReplaceTransition
        {
            public int Count;
            public UniTask PlayReplace(UIForm outgoing, UIForm incoming)
            {
                Count++;
                return UniTask.CompletedTask;
            }
        }

        /// <summary>C# 侧生命周期记录件（含失败注入）。</summary>
        private sealed class RecordingLogic : IUIFormLogic
        {
            public int OnInitCount, OnShowCount, OnHideCount, OnCoverCount, OnRevealCount;
            public bool FailOnInit;
            public bool FailOnceOnShow;

            public void OnInit(UIForm form, IUIData data)
            {
                OnInitCount++;
                if (FailOnInit) throw new InvalidOperationException("init-boom");
            }

            public void OnShow(IUIData data)
            {
                OnShowCount++;
                if (FailOnceOnShow) { FailOnceOnShow = false; throw new InvalidOperationException("show-boom"); }
            }

            public void OnUpdate(float deltaTime) { }
            public void OnPause() { }
            public void OnCover() => OnCoverCount++;
            public void OnReveal() => OnRevealCount++;
            public void OnHide() => OnHideCount++;
        }

        /// <summary>目录替身（§3 可注入面）：不拉配置链路即可声明任意层/全屏组合。</summary>
        private sealed class FakeCatalog : IUIFormCatalog
        {
            private readonly Dictionary<int, UIFormInfo> _rows = new Dictionary<int, UIFormInfo>();

            public void Add(int id, int layer, bool fullScreen, string location = "x")
                => _rows[id] = new UIFormInfo { Id = id, Layer = layer, FullScreen = fullScreen, Location = location, LuaPath = "x" };

            public UIFormInfo Get(int id)
                => _rows.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"FakeCatalog 无行:{id}");
        }

        private sealed class RecordingSource : IVirtualListSource, IVirtualListUnbind
        {
            public int Count { get; set; }
            public int BindCalls, UnbindCalls;
            public int FirstBound = int.MaxValue, LastBound = int.MinValue;

            public void Bind(int index, Component item)
            {
                BindCalls++;
                FirstBound = Math.Min(FirstBound, index);
                LastBound = Math.Max(LastBound, index);
            }

            public void Unbind(Component item) => UnbindCalls++;
        }

        // ---- 驱动/工具 ----

        /// <summary>Tick 驱动到任务完成（EditMode 无 PlayerLoop，转场收尾靠手动 Tick 推进）。</summary>
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

        /// <summary>工程内 Assets 相对路径 → 物理路径。</summary>
        private static string ToFullPath(string assetPath)
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        /// <summary>
        /// 替身界面 prefab：**Canvas/CanvasGroup 必须预建**——EditMode 下 UIForm 走 AddComponent 分支
        /// 会紧接着访问 renderMode 抛 MissingComponentException（native 组件未就绪，2026-09-19 实测）。
        /// </summary>
        private GameObject FakePrefab(string name)
            => Scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

        /// <summary>真 LuaEnv：loader 从磁盘读 `Assets/LiteGame/Lua/**.lua`（与 LuaPreloader 同一 key 约定），
        /// 并绑定 `log` 全局表（与 <c>LuaComponent.BindLog</c> 同款——真实页面脚本会调用 log.info）。</summary>
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

        /// <summary>require 真模块并挂测试钩子：`__shows` 计数（不改仓库 Lua 文件——包装在实例工厂的类表层，
        /// 实例经元表继承到包装后的 OnShow）。</summary>
        private static LuaTable RequireCountedModule(LuaEnv env, string requirePath)
        {
            var chunk = $@"
local m = require('{requirePath}')
m.__shows = 0
local orig = m.OnShow
m.OnShow = function(self, data) m.__shows = m.__shows + 1; if orig then return orig(self, data) end end
return m";
            var result = env.DoString(chunk, "u0_test_wrap");
            Assert.AreEqual(1, result.Length, "包装 require 应返回模块表");
            return (LuaTable)result[0];
        }

        private static int LuaShows(LuaEnv env)
            => Convert.ToInt32(env.DoString($"return require('{UIMainRequire}').__shows")[0]);

        private static Button FindButton(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<Button>();
            return null;
        }

        // ---- ① 真 Lua 首开/关闭/复用（UI-01：实例工厂 + 显式传 self）----

        [Test]
        [Category(TestCategory.Integration)]
        public void 真Lua页面_首开_关闭_复用_实例与self契约()
        {
            // 真配置投影（磁盘 bytes → Luban 同步建表）+ 真 prefab + 真 Lua
            var config = new ConfigService((loc, ct) => UniTask.FromResult(File.ReadAllBytes(ToFullPath(loc))));
            config.LoadAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            var catalog = new UIFormCatalog(config);

            LuaEnv env = NewLuaEnv();
            try
            {
                var registry = new UiLuaRegistry();
                registry.Fill(UIMainRequire, RequireCountedModule(env, UIMainRequire));

                var trans = new MimicTransition();
                var service = new UIService(catalog,
                    transitionStrategy: trans,
                    logicResolver: info => new LuaBehaviourAdapter(env, registry.Get(info.LuaPath)),
                    loadPrefab: (loc, ct) => UniTask.FromResult(UIPrefabLeases.Unowned(AssetDatabase.LoadAssetAtPath<GameObject>(loc))));

                // 首开：真 UIMain 的 OnShow 访问 self.ui:OnButton + self.ui:Pulse——self 未传必炸（§2.2 复现口径）
                var form = Await(service.ShowAsync(1), service);
                Assert.IsNotNull(form, "真 Lua 页面首开应成功");
                Assert.IsTrue(service.IsOpen(1));
                Assert.AreEqual(1, LuaShows(env), "OnShow 应恰好执行一次（self 契约成立的前提）");

                var adapter = (LuaBehaviourAdapter)form.Logic;
                Assert.AreNotSame(adapter.Module, adapter.Logic, "实例工厂：页面应持实例而非共享模块（§5.1）");
                Assert.AreEqual("UIMain", adapter.Logic.Get<string>("_viewName"), "module.new() 应跑过 ctor");
                Assert.IsNotNull(adapter.Logic.Get<object>("ui"), "self.ui 应挂在实例上");
                Assert.AreEqual(1f, trans.AlphaAtShowEntry, 0.0001f, "首开入场前 alpha 应为 1");

                // 按钮真的绑上了：点击走 Lua 闭包 → self.ui:Flash（无新错误即成功路径）
                var btn = FindButton(form.Root, "BtnClose");
                Assert.IsNotNull(btn, "prefab 应有 BtnClose");
                int errBefore = LiteFramework.Log.ErrorCount;
                btn.onClick.Invoke();
                Assert.AreEqual(errBefore, LiteFramework.Log.ErrorCount, "Lua 按钮回调不应产生错误");

                // 关闭：OnHide + 离场 alpha=0；适配器保持（池化，未换表不释放）
                Await(service.CloseAsync(1), service);
                Assert.IsFalse(service.IsOpen(1));
                Assert.AreEqual(0f, form.CanvasGroup.alpha, 0.0001f, "离场表现后 alpha=0（缺陷现场）");
                Assert.IsFalse(((LuaBehaviourAdapter)form.Logic).Released, "普通关闭不换表——适配器随池保留");

                // 复用：复位 alpha（入场前=1）+ 重跑 OnShow + 同一 Lua 实例（UI-02 回归）
                var form2 = Await(service.ShowAsync(1), service);
                Assert.AreSame(form, form2, "池化复用应返回同一 UIForm 实例");
                var adapter2 = (LuaBehaviourAdapter)form2.Logic;
                Assert.AreSame(adapter, adapter2, "复用不换表——适配器不变");
                Assert.AreSame(adapter.Logic, adapter2.Logic, "复用不重建 Lua 实例");
                Assert.AreEqual(2, LuaShows(env), "复用应重跑 OnShow");
                Assert.AreEqual(2, trans.ShowCount, "复用同样播入场表现（首次与复用同管线）");
                Assert.AreEqual(1f, trans.AlphaAtShowEntry, 0.0001f, "复用入场前 alpha 应回 1（UI-02 复位）");
                Assert.GreaterOrEqual(form2.Canvas.sortingOrder, form.Canvas.sortingOrder, "复用应重新分配排序");
            }
            finally
            {
                env.Dispose();
            }
        }

        // ---- ② 复用与首次同管线：同组全屏 → Replace（UI-02）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 复用_同组已有全屏_判定Replace并关闭旧页()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1, 1, true);
            catalog.Add(2, 1, true);
            var trans = new MimicTransition();
            var replace = new CountingReplace();
            var service = new UIService(catalog,
                transitionStrategy: trans, replaceTransition: replace,
                logicResolver: _ => new RecordingLogic(),
                loadPrefab: (loc, ct) => UniTask.FromResult(UIPrefabLeases.Unowned(FakePrefab("p" + loc))));

            var f1 = Await(service.ShowAsync(1), service);
            int f1FirstOrder = f1.Canvas.sortingOrder;                // 复用会重排——先记首开值（同一 UIForm 对象）
            Await(service.CloseAsync(1), service);                    // 落池
            var f2 = Await(service.ShowAsync(2), service);
            Assert.AreEqual(0, replace.Count, "开 2 时组内无全屏——Push");

            var f1b = Await(service.ShowAsync(1), service);           // 复用 1，同组已有全屏 2
            Assert.AreEqual(1, replace.Count, "复用同样推导 Replace（原实现只判首次——UI-02）");
            Assert.IsFalse(service.IsOpen(2), "被替换的旧全屏应收池");
            Assert.IsTrue(service.IsOpen(1));
            Assert.Greater(f1b.Canvas.sortingOrder, f1FirstOrder, "复用应重新分配排序（组基序+递增槽位）");
        }

        // ---- ③ 三层全屏：遮盖状态 + Covered/Paused 可关 + CloseAllOpen 全清（UI-04）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 三层全屏_Covered与Paused可关_CloseAllOpen全清()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1, 0, true);
            catalog.Add(2, 1, true);
            catalog.Add(3, 2, true);
            var logics = new Dictionary<int, RecordingLogic>();
            var service = new UIService(catalog,
                transitionStrategy: new MimicTransition(),
                logicResolver: info => { var l = new RecordingLogic(); logics[info.Id] = l; return l; },
                loadPrefab: (loc, ct) => UniTask.FromResult(UIPrefabLeases.Unowned(FakePrefab("p" + loc))));

            var f1 = Await(service.ShowAsync(1), service);
            var f2 = Await(service.ShowAsync(2), service);
            var f3 = Await(service.ShowAsync(3), service);

            Assert.AreEqual(UIFormState.Covered, f1.State, "底组全屏被遮盖");
            Assert.AreEqual(UIFormState.Covered, f2.State, "中组全屏被遮盖");
            Assert.AreEqual(UIFormState.Active, f3.State);
            Assert.AreEqual(1, logics[1].OnCoverCount);
            Assert.AreEqual(1, logics[2].OnCoverCount);

            // 遮盖中的界面可以直接关（UI-04：Close 只接纳 Active 是缺陷）
            Await(service.CloseAsync(2), service);
            Assert.IsFalse(service.IsOpen(2));
            Assert.AreEqual(UIFormState.Covered, f1.State, "顶层全屏仍在——底层保持遮盖");
            Assert.AreEqual(1, logics[2].OnHideCount, "被关的 Covered 页照常 OnHide");

            // 暂停的界面也可以关（UI-04）
            service.Pause(3);
            Assert.AreEqual(UIFormState.Paused, f3.State);
            Await(service.CloseAsync(3), service);
            Assert.IsFalse(service.IsOpen(3));

            // 顶层全屏关闭后重算遮盖：底层自动 OnReveal（不是等下一次开关）
            Assert.AreEqual(UIFormState.Active, f1.State, "遮盖源消失应立即恢复");
            Assert.AreEqual(1, logics[1].OnRevealCount);

            // CloseAllOpen 从全部打开集合清理（不再只抓 Active 快照）
            Await(service.ShowAsync(2), service);
            Assert.AreEqual(UIFormState.Covered, f1.State);
            Await(service.CloseAllOpen(), service);
            Assert.IsFalse(service.IsOpen(1) || service.IsOpen(2), "全关后不得残留任何打开界面");
        }

        // ---- ④ 初始化失败回滚（§4.1）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 打开失败_OnInit抛异常_回滚无残留且可重试()
        {
            var catalog = new FakeCatalog();
            catalog.Add(1, 1, false);
            var logic = new RecordingLogic { FailOnInit = true };
            var service = new UIService(catalog,
                transitionStrategy: new MimicTransition(),
                logicResolver: _ => logic,
                loadPrefab: (loc, ct) => UniTask.FromResult(UIPrefabLeases.Unowned(FakePrefab("p" + loc))));

            int errBefore = LiteFramework.Log.ErrorCount;
            var task = service.ShowAsync(1);
            for (int i = 0; i < 10 && task.Status == UniTaskStatus.Pending; i++) service.Tick(0.05f);
            // U1-①：初始化失败升级为类型化异常（Reason=InitFailed；基类 InvalidOperationException 保持旧捕获点兼容）
            var openEx = Assert.Throws<UIOpenException>(() => task.GetAwaiter().GetResult(),
                "初始化失败必须对外报失败（禁止 Active+NullLogic 伪装成功）");
            Assert.AreEqual(UIOpenFailure.InitFailed, openEx.Reason);
            Assert.Greater(LiteFramework.Log.ErrorCount, errBefore, "SafeCall 应记录 OnInit 异常");
            Assert.IsFalse(service.IsOpen(1), "回滚后不得登记在打开集合");

            // 可重试：同一界面修复后再次打开成功（字典/加载守卫无残留）
            logic.FailOnInit = false;
            var form = Await(service.ShowAsync(1), service);
            Assert.IsNotNull(form);
            Assert.IsTrue(service.IsOpen(1));
            Assert.AreEqual(2, logic.OnInitCount, "失败一次 + 成功一次 = OnInit 恰好两回");
        }

        // ---- ⑤ env 重建零旧引用（§10.2：drop → dispose → 新 env/注册表 → 复用重解析）----

        [Test]
        [Category(TestCategory.Integration)]
        public void env重建_逻辑落空后复用_按新注册表重解析且旧引用已释放()
        {
            var config = new ConfigService((loc, ct) => UniTask.FromResult(File.ReadAllBytes(ToFullPath(loc))));
            config.LoadAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            var catalog = new UIFormCatalog(config);

            LuaEnv env = NewLuaEnv();
            var registry = new UiLuaRegistry();
            var service = new UIService(catalog,
                transitionStrategy: new MimicTransition(),
                logicResolver: info => new LuaBehaviourAdapter(EnvOf(), registry.Get(info.LuaPath)),
                loadPrefab: (loc, ct) => UniTask.FromResult(UIPrefabLeases.Unowned(AssetDatabase.LoadAssetAtPath<GameObject>(loc))));
            // resolver 经局部函数取"当前 env"——模拟 LuaComponent.Shutdown/Init 交接
            LuaEnv EnvOf() => env;

            LuaEnv env2 = null;
            try
            {
                registry.Fill(UIMainRequire, RequireCountedModule(env, UIMainRequire));
                var form = Await(service.ShowAsync(1), service);
                var oldAdapter = (LuaBehaviourAdapter)form.Logic;
                var oldInstance = oldAdapter.Logic;
                Await(service.CloseAsync(1), service);                // 落池

                // DevReload ⓪/⓪′：全关 → 逻辑落空（env 还活着时释放 Lua 引用）
                service.DropAllLogic();
                Assert.IsTrue(oldAdapter.Released, "env 重建前必须释放旧 Lua 引用");
                Assert.IsInstanceOf<NullUIFormLogic>(form.Logic, "落空后不得保留死引用");

                // ③ env.Dispose → 新 env → ⑥ 新注册表（新模块 = 新实例源）
                env.Dispose();
                env = env2 = NewLuaEnv();
                registry.Clear();
                registry.Fill(UIMainRequire, RequireCountedModule(env2, UIMainRequire));

                // 复用：按当前注册表重解析 + 补跑 OnInit（否则按钮全不响应）
                var form2 = Await(service.ShowAsync(1), service);
                Assert.AreSame(form, form2);
                var newAdapter = (LuaBehaviourAdapter)form2.Logic;
                Assert.AreNotSame(oldAdapter, newAdapter, "复用应换新适配器（新 env 的实例）");
                Assert.AreNotSame(oldInstance, newAdapter.Logic, "不得复用旧 env 的 Lua 实例（旧 env 零访问）");
                Assert.IsTrue(newAdapter.Logic.Get<object>("ui") != null, "新实例应重新挂 self.ui");
                Assert.AreEqual(1, LuaShows(env2), "新 env 的 OnShow 计数从零开始");
                Assert.IsTrue(service.IsOpen(1));
            }
            finally
            {
                env.Dispose();
            }
        }

        // ---- ⑥ VirtualList 窗口复用（UI-07/UI-08）----

        [Test]
        [Category(TestCategory.Unit)]
        public void 虚拟列表_窗口复用_首尾往返_缩容_刷新不重绑()
        {
            var root = Scope.CreateGameObject("list", typeof(RectTransform));
            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(360f, 400f);
            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            var vp = Scope.CreateGameObject("viewport", typeof(RectTransform));
            var vpRt = (RectTransform)vp.transform;
            vpRt.SetParent(rootRt, false);
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero;
            vp.AddComponent<RectMask2D>();

            var content = Scope.CreateGameObject("content", typeof(RectTransform));
            var contentRt = (RectTransform)content.transform;
            contentRt.SetParent(vpRt, false);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            var list = content.AddComponent<VirtualList>();
            var tpl = Scope.CreateGameObject("_tpl", typeof(RectTransform));
            var tplRt = (RectTransform)tpl.transform;
            tplRt.SetParent(contentRt, false);
            tplRt.anchorMin = new Vector2(0f, 1f);
            tplRt.anchorMax = new Vector2(1f, 1f);
            tplRt.pivot = new Vector2(0.5f, 1f);
            tplRt.sizeDelta = new Vector2(0f, 48f);
            tpl.SetActive(false);                                     // 模板必须非激活

            list.Template = tplRt;
            list.Direction = VirtualList.Axis.Vertical;
            list.Spacing = 8f;
            list.Overscan = 2;
            scroll.viewport = vpRt;
            scroll.content = contentRt;

            var src = new RecordingSource { Count = 500 };
            list.SetSource(src);

            const float step = 48f + 8f;                               // 56
            // 容量 = ceil(400/56)=8 + 上下各 overscan 2 + 1 = 13 → 节点数有界（不随 500 线性建）
            Assert.AreEqual(13, list.RealizedCount, "节点数 = 可见容量 + overscan（UI-08）");
            Assert.AreEqual(13, src.BindCalls);
            Assert.AreEqual(500 * step - 8f, contentRt.sizeDelta.y, 0.5f, "Content 总尺寸按数据量维护");
            Assert.AreEqual(0, list.FirstIndex);

            // 滚到尾部：窗口平移到末段，条目重绑（旧实现滚出去就永远回不来——UI-07）
            contentRt.anchoredPosition = new Vector2(0f, -27592f);
            list.RefreshWindow();
            Assert.GreaterOrEqual(list.FirstIndex, 488, "尾部窗口的首索引应接近数据末尾");
            Assert.LessOrEqual(list.FirstIndex + list.RealizedCount, 500, "窗口不得越界");
            int bindsAtTail = src.BindCalls;
            Assert.Greater(bindsAtTail, 13, "滚动应触发窗口内重绑");

            // 滚回顶部：首条重新入窗（UI-07 往返）
            src.FirstBound = int.MaxValue;                            // 窗口读数重置（FirstBound/LastBound 是累计口径）
            src.LastBound = int.MinValue;
            contentRt.anchoredPosition = Vector2.zero;
            list.RefreshWindow();
            Assert.AreEqual(0, list.FirstIndex);
            Assert.AreEqual(13, list.RealizedCount);
            Assert.Greater(src.BindCalls, bindsAtTail, "回滚到顶部应重新绑定首段条目");
            Assert.AreEqual(0, src.FirstBound, "首条重新入窗");
            Assert.AreEqual(12, src.LastBound, "顶部窗口末条");

            // 反复 Refresh：已绑索引不重绑（旧实现每次 Refresh 重复 Bind——UI-08）
            int before = src.BindCalls;
            for (int i = 0; i < 5; i++) list.Refresh();
            Assert.AreEqual(before, src.BindCalls, "窗口未变时 Refresh 不得重复绑定");

            // 缩容：500 → 5（节点回收、解绑、Content 收缩）
            src.Count = 5;
            list.Refresh();
            Assert.AreEqual(5, list.RealizedCount);
            Assert.AreEqual(5 * step - 8f, contentRt.sizeDelta.y, 0.5f);
            Assert.Greater(src.UnbindCalls, 0, "窗口外节点应解绑（可选解绑口）");

            // 清零
            src.Count = 0;
            list.Refresh();
            Assert.AreEqual(0, list.RealizedCount);
        }
    }
}
