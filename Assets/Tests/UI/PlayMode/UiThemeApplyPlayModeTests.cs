using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteClient.UI;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 主题装配应用 PlayMode 用例（P2-3 B2 / AC4——总设计 §6.2「运行期只读解析」）：
    /// ① 真页面（Page_Login 试点节点 Txt_Title 挂 UiStyleRef(color.brand.primary)）打开后
    ///    装配期（PrepareForShow）被写为令牌现值；
    /// ② ThemeProvider 未注入（null）＝降级保持 prefab 烘焙值、开页不抛。
    /// 装配缝＝UIService ctor 形参 themeProvider → RunOpenAsync 赋值 form.ThemeProvider。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiThemeApplyPlayModeTests
    {
        private const int PageLogin = 11;

        private sealed class StaticCatalog : IPageCatalog
        {
            private readonly System.Collections.Generic.Dictionary<int, PageDefinition> _rows =
                new System.Collections.Generic.Dictionary<int, PageDefinition>();
            public void Add(int id, int layer, bool fullScreen, string location)
                => _rows[id] = new PageDefinition
                { Id = id, Layer = layer, FullScreen = fullScreen, LuaPath = null, Location = location };
            public PageDefinition Get(int id)
            {
                if (_rows.TryGetValue(id, out var r)) return r;
                throw new System.Collections.Generic.KeyNotFoundException($"StaticCatalog 无行:{id}");
            }
        }

        private sealed class PlainLease : IUIPrefabLease
        {
            private readonly GameObject _p;
            public PlainLease(GameObject p) => _p = p;
            public GameObject Prefab => _p;
            public void Release() { }
        }

        private sealed class NullLogic : IUIFormLogic
        {
            public void OnInit(UIForm form, IUIData data) { }
            public void OnShow(IUIData data) { }
            public void OnUpdate(float dt) { }
            public void OnPause() { }
            public void OnCover() { }
            public void OnReveal() { }
            public void OnHide() { }
        }

        private PlayModeTestScope _scope;
        private UIService _ui;
        private UiThemeAsset _theme;
        private UiThemeAsset _themeClone;
        private static bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(UiThemeApplyPlayModeTests));
            if (!_assetsReady)
            {
                var init = AssetService.InitAsync();
                yield return Wait(init, 60f);
                Assert.AreNotEqual(UniTaskStatus.Pending, init.Status, "资源包初始化超时（60s）");
                _assetsReady = true;
            }
#if UNITY_EDITOR
            _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<UiThemeAsset>(
                LiteClient.UI.UiThemePaths.DefaultAssetPath);
            Assert.IsNotNull(_theme, "编辑器 PlayMode 直载主题 SO（生成物）");
#endif
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
            if (_themeClone != null) { UnityEngine.Object.Destroy(_themeClone); _themeClone = null; }
            yield return _scope.DisposeAsync();
        }

        private UIService BuildService(Func<UiThemeAsset> themeProvider)
        {
            var catalog = new StaticCatalog();
            catalog.Add(PageLogin, layer: 0, fullScreen: true, "Assets/ShareResource/UI/Screens/Page_Login.prefab");
            return new UIService(catalog,
                logicResolver: info => new NullLogic(),
                loadPrefab: LoadLeaseAsync,
                transitionStrategy: new FadeSlideTransition(),
                themeProvider: themeProvider);
        }

        [UnityTest]
        public IEnumerator 装配应用_真页面Ref节点打开后等于令牌现值()
        {
            Assume.That(_theme, Is.Not.Null, "仅编辑器 PlayMode（真 SO 直载）跑本例");
            _ui = BuildService(() => _theme);
            var host = _scope.CreateGameObject("Host");
            PlayModeTicker.Attach(host, _ui.Tick);

            var open = _ui.ShowAsync(PageLogin);
            yield return Wait(open, 20f);
            UIForm form = open.GetAwaiter().GetResult();
            Assert.IsNotNull(form, "页面打开须成功");

            var resolver = UiStyleResolver.FromAsset(_theme);
            Assert.IsTrue(resolver.TryColor("color.brand.primary", out Color expected), "令牌应存在");

            Transform title = form.Root.transform.Find("Cot_SafeArea/Txt_Title");
            Assert.IsNotNull(title, "试点节点应在页面内");
            var styleRef = title.GetComponent<UiStyleRef>();
            Assert.IsNotNull(styleRef, "试点节点应挂 UiStyleRef");
            Assert.AreEqual("color.brand.primary", styleRef.ColorToken, "试点声明");
            Assert.AreEqual(expected, title.GetComponent<Graphic>().color, "装配期写入令牌现值");
        }

        [UnityTest]
        public IEnumerator 降级_未注入主题保持烘焙值且开页不抛()
        {
            _ui = BuildService(null);                                  // 无 ThemeProvider＝降级
            var host = _scope.CreateGameObject("Host");
            PlayModeTicker.Attach(host, _ui.Tick);

            var open = _ui.ShowAsync(PageLogin);
            yield return Wait(open, 20f);
            UIForm form = open.GetAwaiter().GetResult();
            Assert.IsNotNull(form, "无主题注入不得阻断开页");

            var resolver = UiStyleResolver.FromAsset(
                UnityEditor.AssetDatabase.LoadAssetAtPath<UiThemeAsset>(UiThemePaths.DefaultAssetPath));
            resolver.TryColor("color.brand.primary", out Color tokenValue);

            Transform title = form.Root.transform.Find("Cot_SafeArea/Txt_Title");
            // 烘焙值＝refs 应用段落盘写入的令牌现值（试点零视觉变化前提），降级路径只是"不再重写"
            Assert.AreEqual(tokenValue, title.GetComponent<Graphic>().color, "降级＝保持 prefab 烘焙值（本例与注入态同值）");
            Assert.IsNotNull(title.GetComponent<UiStyleRef>(), "声明组件仍在（下次注入即恢复应用）");
        }

        [UnityTest]
        public IEnumerator 装配应用_Selectable状态色随主题变化()
        {
            Assume.That(_theme, Is.Not.Null, "仅编辑器 PlayMode");
            _themeClone = UnityEngine.Object.Instantiate(_theme);
            Color custom = new Color(0.12f, 0.34f, 0.56f, 1f);
            UiThemeColorEntry[] colors = _themeClone.Colors;
            for (int i = 0; i < colors.Length; i++)
                if (colors[i].Id == UiSelectableTheme.NormalToken) colors[i].Value = custom;
            _themeClone.Colors = colors;

            _ui = BuildService(() => _themeClone);
            var host = _scope.CreateGameObject("Host");
            PlayModeTicker.Attach(host, _ui.Tick);

            var open = _ui.ShowAsync(PageLogin);
            yield return Wait(open, 20f);
            UIForm form = open.GetAwaiter().GetResult();
            Assert.IsNotNull(form, "页面打开须成功");

            var themes = form.Root.GetComponentsInChildren<UiSelectableTheme>(true);
            Assert.IsNotEmpty(themes, "Page_Login 应至少有 1 个 Selectable 状态色接缝（迁移后）");
            var selectable = themes[0].GetComponent<UnityEngine.UI.Selectable>();
            Assert.IsNotNull(selectable, "接缝节点应带 Selectable");
            Assert.IsTrue(UiStyleResolver.Hits(selectable.colors.normalColor, custom),
                $"装配期应按修改后的 selectable token 注入（实得 {selectable.colors.normalColor}）");
        }

        [UnityTest]
        public IEnumerator 降级_主题资产null只告警一次()
        {
            _ui = BuildService(() => null);
            var host = _scope.CreateGameObject("Host");
            PlayModeTicker.Attach(host, _ui.Tick);
            LiteFramework.Log.ResetForTesting();

            var open = _ui.ShowAsync(PageLogin);
            yield return Wait(open, 20f);
            UIForm form = open.GetAwaiter().GetResult();
            Assert.IsNotNull(form, "null 资产不得阻断开页");

            var close = _ui.CloseAsync(PageLogin);
            yield return Wait(close, 20f);
            var reopen = _ui.ShowAsync(PageLogin);
            yield return Wait(reopen, 20f);
            Assert.IsNotNull(reopen.GetAwaiter().GetResult(), "复用/重开应成功");

            int warnings = LiteFramework.Log.Recent.Count(e => e.Level == LiteFramework.LogLevel.Warning
                && (e.Message ?? string.Empty).Contains("主题资产未就绪"));
            Assert.AreEqual(1, warnings, "同一 UIForm 实例的 null 资产只告警一次（C-4）");
        }

        private static async UniTask<IUIPrefabLease> LoadLeaseAsync(string location, CancellationToken ct)
        {
            var go = await AssetService.LoadAssetAsync<GameObject>(location, ct);
            if (go == null) throw new System.InvalidOperationException($"真资源加载失败:{location}");
            return new PlainLease(go);
        }

        private static IEnumerator Wait(UniTask task, float timeoutSeconds)
        {
            float deadline = UnityEngine.Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending)
            {
                if (UnityEngine.Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"等待超时（{timeoutSeconds}s）");
                yield return null;
            }
        }
    }
}
