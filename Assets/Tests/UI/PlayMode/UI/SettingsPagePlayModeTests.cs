using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Map;
using LiteGame.Modules.Settings;
using LiteGame.Modules.UI;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 设置页 PlayMode 用例（PM-SET-1..5）：真 Page_Settings.prefab 走 UIService 完整打开链，
    /// fake 语言服务满足 SetTextKey 绑定前置；fake 设置端口记录读写；空端口走禁用且不抛路径。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class SettingsPagePlayModeTests
    {
        private const string TabsPath = "Cot_SafeArea/Cot_Body/Cot_TabBar/Tabs_Settings";
        private const string HeaderPath = "Cot_SafeArea/Cot_Header";
        private const string PanelRootPath = "Cot_SafeArea/Cot_Body/Cot_Panel";
        private const string AudioPath = PanelRootPath + "/Cot_AudioPanel";
        private const string GraphicsPath = PanelRootPath + "/Cot_GraphicsPanel";
        private const string DisplayPath = PanelRootPath + "/Cot_DisplayPanel";
        private const string ControlPath = PanelRootPath + "/Cot_ControlPanel";
        private const string GeneralPath = PanelRootPath + "/Cot_GeneralPanel";

        /// <summary>空端口时要求禁用的 12 个设置控件（节点面与页面 SettingsControls 同集）。</summary>
        private static readonly string[] SettingsControlPaths =
        {
            AudioPath + "/Sld_Volume",
            AudioPath + "/Tgl_Sound",
            GraphicsPath + "/Ddl_Quality",
            DisplayPath + "/Ddl_DisplayMode",
            DisplayPath + "/Ddl_Resolution",
            DisplayPath + "/Ddl_FrameRate",
            DisplayPath + "/Ddl_VSync",
            ControlPath + "/Sld_LookSensitivity",
            ControlPath + "/Sld_AdsSensitivity",
            ControlPath + "/Tgl_InvertLookY",
            GeneralPath + "/Ddl_Language",
            GeneralPath + "/Btn_ResetDefaults",
        };

        /// <summary>语言服务替身：Get 回显 key（用例只验"绑定生效"，不验译文）。</summary>
        private sealed class FakeLocale : ILocalizationService
        {
            public string Locale => "zh-CN";
            public event Action<string> OnLocaleChanged;
            public int MissingKeyCount => 0;
            public void SetLocale(string locale) => OnLocaleChanged?.Invoke(locale);
            public string Raw(string key) => key;
            public string Get(string key) => Raw(key);
            public string Format(string key, params object[] args) => Raw(key);
            public string FormatPlural(string key, long count, params object[] args) => Raw(key);
        }

        /// <summary>设置端口替身：记录写/动作调用，读面给可辨识的原语值。</summary>
        private sealed class FakeSettingsPort : ISettingsPagePort
        {
            public readonly List<int> QualityWrites = new List<int>();
            public readonly List<float> VolumeWrites = new List<float>();
            public readonly List<int> LastTabWrites = new List<int>();
            public int ResetDefaultsCount;
            public int SaveCount;

            public int LastTab => 2;                       // 非首签：证明页面按端口恢复而非停在 prefab 默认
            public int FrameRateOptionCount => 3;
            public int FrameRateOption(int index) => index == 0 ? 0 : index == 1 ? 60 : 120;
            public int DisplayMode => 0;
            public int ResolutionCount() => 2;
            public string ResolutionLabel(int index) => index == 0 ? "1280x720" : "1920x1080";
            public int ResolutionIndex => 0;
            public float LookSensitivity => 0.5f;
            public float AdsSensitivity => 0.35f;
            public float Volume => 0.4f;
            public bool SoundEnabled => true;
            public int Quality => 1;
            public int FrameRate => 60;
            public int VSync => 1;
            public bool InvertLookY => false;

            public void SetLastTab(int index) => LastTabWrites.Add(index);
            public void SetVolume(float value01) => VolumeWrites.Add(value01);
            public void SetSoundEnabled(bool on) { }
            public void SetQuality(int tier) => QualityWrites.Add(tier);
            public void SetDisplayMode(int mode) { }
            public void SetResolutionIndex(int index) { }
            public void SetFrameRate(int cap) { }
            public void SetVSync(int value) { }
            public void SetLookSensitivity(float multiplier) { }
            public void SetAdsSensitivity(float multiplier) { }
            public void SetInvertLookY(bool on) { }
            public void ResetDefaults() => ResetDefaultsCount++;
            public void Save() => SaveCount++;
        }

        /// <summary>联机面替身：版本号等只读事实可辨识，不触网。</summary>
        private sealed class FakeMeta : IMetaSession
        {
            public bool IsConfigured => true;
            public string AccountId => "acc-test";
            public string ServerUrl => "http://meta.test";
            public string Version => "v-test";

            public UniTask<MetaResult<bool>> GuestLoginAsync(CancellationToken ct = default)
                => UniTask.FromResult(MetaResult<bool>.Success(true));
            public UniTask<MetaResult<IReadOnlyList<MetaRoomRow>>> ListRoomsAsync(CancellationToken ct = default)
                => UniTask.FromResult(MetaResult<IReadOnlyList<MetaRoomRow>>.Success(Array.Empty<MetaRoomRow>()));
            public UniTask<MetaResult<string>> QuickEnterAsync(CancellationToken ct = default)
                => UniTask.FromResult(MetaResult<string>.Success("room"));
            public UniTask<MetaResult<string>> CreateRoomAsync(int mapId, int expectedPlayers, CancellationToken ct = default)
                => UniTask.FromResult(MetaResult<string>.Success("room"));
            public UniTask<MetaResult<string>> EnterRoomAsync(string roomId, CancellationToken ct = default)
                => UniTask.FromResult(MetaResult<string>.Success(roomId));
            public UniTask<MetaResult<IReadOnlyList<MetaRecordRow>>> ListRecordsAsync(CancellationToken ct = default)
                => UniTask.FromResult(MetaResult<IReadOnlyList<MetaRecordRow>>.Success(Array.Empty<MetaRecordRow>()));
            public void LeaveMatch() { }
        }

        /// <summary>地图表替身：设置页不读地图，空表即可。</summary>
        private sealed class FakeMaps : IMapProvider
        {
            public SimMapData GetMap(int mapId = 0) => null;
            public string ScenePath(int mapId) => string.Empty;
            public IReadOnlyList<string> ScenePaths() => Array.Empty<string>();
            public IReadOnlyList<MapOption> Options => Array.Empty<MapOption>();
        }

        /// <summary>房间读口替身：空快照。</summary>
        private sealed class FakeRoom : IRoomView
        {
            public bool IsOwner => false;
            public int ExpectedPlayers => 0;
            public IReadOnlyList<RoomSeat> Seats => Array.Empty<RoomSeat>();
            public event Action<bool> OwnerChanged { add { } remove { } }
            public event Action<IReadOnlyList<RoomSeat>> SeatsChanged { add { } remove { } }
            public event Action<string> StartMatchDenied { add { } remove { } }
            public void RequestStartMatch() { }
        }

        /// <summary>确认弹窗替身。</summary>
        private sealed class FakeConfirm : IConfirmDialog
        {
            public bool IsBound => true;
            public UniTask<bool> ShowAsync(string title, string message, string okText, string cancelText)
                => UniTask.FromResult(true);
        }

        private PlayModeTestScope _scope;
        private FakeLocale _locale;
        private FakeSettingsPort _settings;
        private UIService _ui;
        private UINavigationController _nav;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(SettingsPagePlayModeTests));
            _locale = new FakeLocale();
            _settings = new FakeSettingsPort();

            if (!_assetsReady)
            {
                UniTask init = TestContent.InitAsync();
                yield return WaitUniTask(init, 60f);
                Assert.IsFalse(init.Status == UniTaskStatus.Pending, "资源包初始化超时（60s）");
                _assetsReady = true;
            }

            _ui = new UIService(
                UIPageCatalog.Instance,
                loadPrefab: LoadRealPrefabAsync,
                localeProvider: () => _locale,
                pageContextProvider: _ => _settings == null
                    ? null
                    : new PageServices(_nav, _ui, new FakeConfirm(), new FakeMeta(), new FakeMaps(),
                        new FakeRoom(), _settings));
            _nav = new UINavigationController(_ui);

            GameObject host = _scope.CreateGameObject("SettingsPlayModeHost");
            PlayModeTicker.Attach(host, _ui.Tick);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_ui != null)
            {
                UniTask shutdown = _ui.ShutdownAsync();
                yield return WaitUniTask(shutdown, 20f);
                _ui = null;
            }
            yield return _scope.DisposeAsync();
        }

        [UnityTest]
        public IEnumerator PM_SET_1_打开设置页_不抛且控件按端口初始化()
        {
            UniTask<UIForm> open = _ui.ShowAsync(PageIds.Page_Settings);
            yield return WaitUniTask(open, 20f);
            Assert.IsFalse(open.Status == UniTaskStatus.Pending, "设置页打开超时（真 prefab + UI 服务）");
            UIForm form = open.GetAwaiter().GetResult();

            Assert.IsNotNull(form, "打开返回页面实例");
            Assert.IsInstanceOf<Page_SettingsLogic>(form.Logic, "真 prefab 自带页面逻辑");
            Assert.IsTrue(_ui.IsOpen(PageIds.Page_Settings), "页面处于打开态");
            Assert.AreEqual("Common.Settings", Control<Text>(form, HeaderPath + "/Txt_Title").text,
                "静态文案经 fake 语言服务落值（SetTextKey 已绑定）");

            Assert.AreEqual(0.4f, Control<Slider>(form, AudioPath + "/Sld_Volume").value, 1e-4f, "音量初值来自端口");
            Assert.IsTrue(Control<Toggle>(form, AudioPath + "/Tgl_Sound").isOn, "音效开关初值来自端口");
            Assert.AreEqual(1, Control<Dropdown>(form, GraphicsPath + "/Ddl_Quality").value, "画质初值来自端口");
            Assert.AreEqual(0, Control<Dropdown>(form, DisplayPath + "/Ddl_DisplayMode").value, "显示模式初值来自端口");
            Assert.AreEqual(1, Control<Dropdown>(form, DisplayPath + "/Ddl_FrameRate").value, "帧率档位按端口映射");
            Assert.AreEqual(1, Control<Dropdown>(form, DisplayPath + "/Ddl_VSync").value, "垂直同步档位按端口映射");
            Assert.AreEqual(0, Control<Dropdown>(form, DisplayPath + "/Ddl_Resolution").value, "全屏模式分辨率跟随显示器");
            Assert.IsFalse(Control<Dropdown>(form, DisplayPath + "/Ddl_Resolution").interactable, "全屏模式分辨率不可改");
            Assert.AreEqual(0.5f, Control<Slider>(form, ControlPath + "/Sld_LookSensitivity").value, 1e-4f, "视角灵敏度初值");
            Assert.AreEqual(0.35f, Control<Slider>(form, ControlPath + "/Sld_AdsSensitivity").value, 1e-4f, "开镜灵敏度初值");

            ToggleTabGroup tabs = Control<ToggleTabGroup>(form, TabsPath);
            Assert.AreEqual(2, tabs.Selected, "页签按端口 LastTab 恢复（2＝显示页）");
            Assert.IsTrue(FindNode(form, DisplayPath).gameObject.activeSelf, "恢复页签对应面板可见");
            Assert.IsFalse(FindNode(form, AudioPath).gameObject.activeSelf, "其余面板收起");

            Control<Toggle>(form, TabsPath + "/Tgl_TabAudio").isOn = true;
            Assert.IsTrue(FindNode(form, AudioPath).gameObject.activeSelf, "用户切页签后音频面板可见");
            CollectionAssert.AreEqual(new[] { 0 }, _settings.LastTabWrites, "页签切换写回端口");
            Assert.AreEqual(0, _settings.ResetDefaultsCount, "未确认重置不落端口");
        }

        [UnityTest]
        public IEnumerator PM_SET_2_改画质下拉_端口收到所选索引()
        {
            UniTask<UIForm> open = _ui.ShowAsync(PageIds.Page_Settings);
            yield return WaitUniTask(open, 20f);
            Assert.IsFalse(open.Status == UniTaskStatus.Pending, "设置页打开超时");
            UIForm form = open.GetAwaiter().GetResult();

            Control<Toggle>(form, TabsPath + "/Tgl_TabGraphics").isOn = true;
            Dropdown quality = Control<Dropdown>(form, GraphicsPath + "/Ddl_Quality");
            Assert.AreEqual(1, quality.value, "画质初始档位来自端口");

            quality.value = 2;                              // 用户选择 → onValueChanged
            Assert.AreEqual(1, _settings.QualityWrites.Count, "画质写入一次");
            Assert.AreEqual(2, _settings.QualityWrites[0], "画质按所选索引写入端口");
        }

        [UnityTest]
        public IEnumerator PM_SET_3_改音量滑块_端口收到所选值()
        {
            UniTask<UIForm> open = _ui.ShowAsync(PageIds.Page_Settings);
            yield return WaitUniTask(open, 20f);
            Assert.IsFalse(open.Status == UniTaskStatus.Pending, "设置页打开超时");
            UIForm form = open.GetAwaiter().GetResult();

            Control<Toggle>(form, TabsPath + "/Tgl_TabAudio").isOn = true;
            Slider volume = Control<Slider>(form, AudioPath + "/Sld_Volume");
            Assert.AreEqual(0.4f, volume.value, 1e-4f, "音量初始值来自端口");

            volume.value = 0.73f;                           // 用户拖动 → onValueChanged
            Assert.AreEqual(1, _settings.VolumeWrites.Count, "音量写入一次");
            Assert.AreEqual(0.73f, _settings.VolumeWrites[0], 1e-4f, "音量按滑块值写入端口");
        }

        [UnityTest]
        public IEnumerator PM_SET_4_关闭按钮与Back_均经OnPageHide落盘()
        {
            UniTask<UIForm> open = _ui.ShowAsync(PageIds.Page_Settings);
            yield return WaitUniTask(open, 20f);
            Assert.IsFalse(open.Status == UniTaskStatus.Pending, "设置页打开超时");
            UIForm form = open.GetAwaiter().GetResult();

            Control<Button>(form, HeaderPath + "/Btn_Close").onClick.Invoke();
            yield return WaitUntilClosed(PageIds.Page_Settings, 20f);
            Assert.IsFalse(_ui.IsOpen(PageIds.Page_Settings), "关闭按钮走真实关闭链");
            Assert.AreEqual(2, _settings.SaveCount, "关闭按钮：CloseWithSave 一次 + OnPageHide 同径一次");

            UniTask<UIForm> reopen = _ui.ShowAsync(PageIds.Page_Settings);
            yield return WaitUniTask(reopen, 20f);
            Assert.IsFalse(reopen.Status == UniTaskStatus.Pending, "设置页重开超时");
            int saveBeforeBack = _settings.SaveCount;

            UniTask back = _ui.CloseAsync(PageIds.Page_Settings, UIService.CloseReason.Back);
            yield return WaitUniTask(back, 20f);
            Assert.AreEqual(saveBeforeBack + 1, _settings.SaveCount, "Back 关闭同径 OnPageHide 落盘");
            Assert.IsFalse(_ui.IsOpen(PageIds.Page_Settings), "Back 关闭完成");
        }

        [UnityTest]
        public IEnumerator PM_SET_5_空端口_控件禁用且面板可见页签可切关闭可用()
        {
            _settings = null;                               // PageContext 的 Func<ISettingsPagePort> 返回 null

            UniTask<UIForm> open = _ui.ShowAsync(PageIds.Page_Settings);
            yield return WaitUniTask(open, 20f);
            Assert.IsFalse(open.Status == UniTaskStatus.Pending, "空端口打开设置页不得抛/超时");
            UIForm form = open.GetAwaiter().GetResult();

            Assert.IsInstanceOf<Page_SettingsLogic>(form.Logic, "空端口仍加载真页面逻辑");
            Assert.IsTrue(_ui.IsOpen(PageIds.Page_Settings), "空端口页面仍打开");
            foreach (string path in SettingsControlPaths)
                Assert.IsFalse(Control<Selectable>(form, path).interactable, "空端口设置控件禁用:" + path);

            Assert.IsTrue(FindNode(form, PanelRootPath).gameObject.activeInHierarchy, "面板容器仍可见");
            Assert.IsTrue(FindNode(form, AudioPath).gameObject.activeSelf, "空端口默认首签面板可见");
            Assert.IsFalse(FindNode(form, DisplayPath).gameObject.activeSelf, "非当前页签面板保持收起");

            Control<Toggle>(form, TabsPath + "/Tgl_TabDisplay").isOn = true;
            yield return null;
            Assert.IsTrue(FindNode(form, DisplayPath).gameObject.activeSelf, "空端口 Tabs 仍可切");
            Assert.IsFalse(FindNode(form, AudioPath).gameObject.activeSelf, "切换后旧面板收起");

            Assert.IsTrue(Control<Button>(form, HeaderPath + "/Btn_Close").interactable, "空端口 Btn_Close 仍可用");
            Assert.AreEqual(0, _settings?.SaveCount ?? 0, "空端口未发生落盘");
        }

        private static Transform FindNode(UIForm form, string path)
        {
            Transform node = form.Root.transform.Find(path);
            Assert.IsNotNull(node, "控件节点缺失:" + path);
            return node;
        }

        private static T Control<T>(UIForm form, string path) where T : Component
        {
            T control = FindNode(form, path).GetComponent<T>();
            Assert.IsNotNull(control, "节点缺组件:" + path + "/" + typeof(T).Name);
            return control;
        }

        private IEnumerator WaitUntilClosed(int formId, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (_ui.IsOpen(formId) && Time.realtimeSinceStartup < deadline) yield return null;
        }

        private static IEnumerator WaitUniTask(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private static async UniTask<IUIPrefabLease> LoadRealPrefabAsync(string location, CancellationToken ct)
        {
            GameObject prefab = await TestContent.LoadAssetAsync<GameObject>(location, ct);
            if (prefab == null) throw new InvalidOperationException($"真资源加载失败:{location}");
            return UIPrefabLeases.Unowned(prefab);
        }
    }
}
