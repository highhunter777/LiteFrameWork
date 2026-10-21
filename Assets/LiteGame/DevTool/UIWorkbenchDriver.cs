#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;
using LiteClient.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LiteGame
{
    /// <summary>
    /// UI 工作台驱动（《UI工作台设计》W0→W2——dev-only，DevHUD 族"不担产品职责"）：
    /// 面板目录一键开/关任意已登记表单（真导航单写者）+ 反馈面演示组（Loading/错误/Toast）+ 状态读数；
    /// **W2 扩**：语言切换行、状态注入（空列表/长文本）、HUD 预览开关、prefab 回写工具入口。
    ///
    /// **三宏门控（W2 发布剥离）**：整件与 `DevHUD.cs` 同款 `#if` 口径——release 剥净，
    /// 不留"随包发出但不可达"的产品 API 调用点（设计 §5 发布剥离口径）。
    ///
    /// **不旁路装配**：Update 轮询 `GameEntry.TakeContainer()`（未装配下帧再试），服务经容器 Resolve；
    /// 就绪门控 = `ConfigService.Loaded`（表已发布）∧ `StageMachine.Current == ProcedureId.Main`
    /// （内容初始化完成、开页安全）——未就绪按钮置灰 + 状态行提示。**真链路照跑**：Patch 走本地通道、
    /// Main 阶段照常加载玩法场景（面板不阻止；W2 已评"不成负担"，见施工记录）。Play 内直开，无编译参与。
    ///
    /// **状态注入作用域**：只作用于**本工作台目录打开过**的页面（`_lastForm`）——页面实例取用口
    /// 未对产品开（UIService 公开面只有 IsOpen），故不追全局开页来源；注入是预览性质，
    /// 页面重开/数据推送即自然复位（长文本另存原文做显式还原）。
    /// </summary>
    public sealed class UIWorkbenchDriver : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _panelGroup;
        [SerializeField] private RectTransform _rowsParent;
        [SerializeField] private GameObject _rowTemplate;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Button _btnLoading;
        [SerializeField] private Button _btnProgress;
        [SerializeField] private Button _btnEndLoading;
        [SerializeField] private Button _btnError;
        [SerializeField] private Button _btnToast;
        [SerializeField] private TMP_Text _toolsText;          // 工具条状态行（叠加层，页面打开时也可见）
        [SerializeField] private Button _btnLangZh;
        [SerializeField] private Button _btnLangEn;
        [SerializeField] private TMP_Text _localeText;         // 当前语言（工具条语言行读数）
        [SerializeField] private Button _btnHudPreview;
        [SerializeField] private TMP_Text _hudPreviewLabel;
        [SerializeField] private Button _btnInjectEmpty;
        [SerializeField] private Button _btnInjectLong;
        [SerializeField] private Button _btnInjectReset;
        [SerializeField] private Button _btnWriteBack;
        [SerializeField] private TMP_Text _writeBackLabel;

        /// <summary>回写工具菜单路径（`PrefabWriteBackWindow.MenuPath` 同值——单源由 EditMode 守卫用例钉住，
        /// 两程序集无引用关系故不能直接引用常量）。</summary>
        private const string WriteBackMenuPath = "LiteGame/快捷/Prefab 回写";

        /// <summary>长文本注入样本（伪本地化富文本检验；单源常量，注入/还原同一份）。</summary>
        private const string LongTextSample = "（长文本溢出检验 LongText-Overflow-Check 0123456789）";

        private ServiceContainer _container;
        private UINavigationController _nav;
        private UIService _ui;
        private FeedbackService _feedback;
        private IContentService _content;
        private cfg.Tbuiform _forms;
        private StageMachine<ProcedureId, ProcedureArgs> _fsm;
        private FeedbackService.LoadingScope? _loadingScope;
        private bool _ready;
        private bool _gated;                       // 置灰已应用（避免每帧重复写）
        private int _frame;
        private string _lastOp = "—";
        private int _demoStep;                       // 进度演示步进（0.25/步——四步到 1.0 后回卷）

        /// <summary>对局菜单（设置页）表单 id——落地复用**对局退场 UI**：tbuiform 登记 15。</summary>
        private const int MenuFormId = 15;

        /// <summary>退出钮文案键（落点＝工作台）：tbltext `UI.Settings.ExitWorkbench` 单源。</summary>
        private const string ExitWorkbenchKey = "UI.Settings.ExitWorkbench";

        private LuaComponent _lua;                   // 菜单导航数据表（LuaUIData）的构造源
        private Func<UniTask<bool>> _savedBack;      // 平台返回原装订（还原用）
        private UIForm _menuForm;                    // 本件开出的菜单页（识别"退出已完成"）
        private int _lobbyFormId = -1;

        private UIForm _lastForm;                    // 目录打开的最近页（状态注入目标——见类注释作用域）
        private AssetLease<GameObject> _hudLease;    // HUD 预览租约（与实例同生命周期）
        private GameObject _hudInstance;
        private readonly List<KeyValuePair<TMP_Text, string>> _inflated = new List<KeyValuePair<TMP_Text, string>>();
        private bool _listsCleared;                  // 本页列表是否被注入清空（复位走重开真链路）
        private Button[] _gatedButtons;              // 置灰面单源（Awake 建一次）

        /// <summary>空列表注入源（零行；预览件——注入后页面下一次数据推送即覆盖）。</summary>
        private sealed class EmptyListSource : IVirtualListSource
        {
            public int Count => 0;
            public void Bind(int index, Component item) { }
        }

        private static readonly IVirtualListSource EmptySource = new EmptyListSource();

        private void Awake()
        {
            _btnLoading.onClick.AddListener(OnLoading);
            _btnProgress.onClick.AddListener(OnProgress);
            _btnEndLoading.onClick.AddListener(OnEndLoading);
            _btnError.onClick.AddListener(() => OnError().Forget());
            _btnToast.onClick.AddListener(OnToast);
            _btnLangZh.onClick.AddListener(() => SetLocale(LocalizationCatalog.SourceLocale));
            _btnLangEn.onClick.AddListener(() => SetLocale(LocalizationCatalog.EnglishLocale));
            _btnHudPreview.onClick.AddListener(OnHudPreview);
            _btnInjectEmpty.onClick.AddListener(OnInjectEmpty);
            _btnInjectLong.onClick.AddListener(OnInjectLong);
            _btnInjectReset.onClick.AddListener(OnInjectReset);
            _btnWriteBack.onClick.AddListener(OnWriteBack);
            _gatedButtons = new[]
            {
                _btnLoading, _btnProgress, _btnEndLoading, _btnError, _btnToast,
                _btnLangZh, _btnLangEn, _btnHudPreview, _btnInjectEmpty, _btnInjectLong, _btnInjectReset,
            };
#if !UNITY_EDITOR
            // 回写工具是编辑器窗口（LiteGame.Editor）——开发版 Player 里只留提示，不给死入口
            if (_btnWriteBack != null) _btnWriteBack.interactable = false;
            if (_writeBackLabel != null) _writeBackLabel.text = "回写（仅编辑器）";
#endif
        }

        private void OnDestroy()
        {
            if (_ui != null && _ui.Focus.PlatformBack == MenuBack) _ui.Focus.PlatformBack = _savedBack;   // 还原平台返回
            CloseHudPreview();                        // 租约/实例随面板销毁归还（幂等）
            foreach (var pair in _inflated)
                if (pair.Key != null) pair.Key.text = pair.Value;
            _inflated.Clear();
        }

        private void Update()
        {
            if (!_ready) TryBecomeReady();
            if (DebugTuner.TakeWorkbenchHideRequest())  // 场景不可卸形态的落点收尾：自我隐藏（回大厅页）
            {
                ShutdownWorkbenchUi();
                return;
            }
            if (DebugTuner.TakeWorkbenchMenuRequest())  // 开发页「设置菜单」钮：与 ESC 同一条入口
                OpenMenu();
            WatchMenuExit();                            // 菜单退出 → 请求关工作台场景（落点收尾）
            if ((_frame++ & 31) == 0)                 // dev 面板节流：约半秒一刷
            {
                string locale = CurrentLocale();
                string hud = _hudInstance != null ? "开" : "关";
                if (_localeText != null) _localeText.text = locale;
                if (_statusText != null) _statusText.text = BuildStatus(locale, hud);
                if (_toolsText != null) _toolsText.text = BuildCompactStatus(hud);   // 工具条窄——只给最近操作（全量读数在面板）
            }
        }

        private void TryBecomeReady()
        {
            var entry = FindAnyObjectByType<GameEntry>();
            if (entry == null)
            {
                // 本场景是"纯叠加 UI 场景"（无引导件）——直接 Play 不再支持：入口＝Boot + 工作台模式/开发页
                if (_frame > 120 && _lastOp == "—") _lastOp = "无宿主（GameEntry）——请从 Boot 场景用工作台模式/开发页进入";
                return;
            }
            try { _container = entry.TakeContainer(); }
            catch (InvalidOperationException) { return; }              // 未装配——下帧再试
            try
            {
                _nav = _container.Resolve<UINavigationController>();
                _ui = _container.Resolve<UIService>();
                _feedback = _container.Resolve<FeedbackService>();
                _content = _container.Resolve<IContentService>();      // W2：HUD 预览租约（缺失 = 预览报错，不阻断其余面）
                _lua = _container.Resolve<LuaComponent>();             // 菜单导航数据表（对局退场 UI 复用）
                var config = _container.Resolve<IConfigService>();
                if (!config.Loaded) { _lastOp = "配置未发布（Preload 中）"; return; }
                _forms = config.Tables.Tbuiform;
                _fsm = _container.Resolve<StageMachine<ProcedureId, ProcedureArgs>>();
            }
            catch (Exception ex)
            {
                _lastOp = "服务解析失败:" + ex.Message;
                return;
            }

            if (_fsm.Current != ProcedureId.Main)
            {
                if (!_gated) SetGated(true);
                return;                                                // Patch/Preload 中——Main 后开闸
            }

            if (!BuildRows()) return;                                  // 行模板断线 = 未就绪（状态行已提示）
            BindMenuBack();                                            // ESC → 对局菜单（设置页）：退出＝退出工作台
            _ready = true;
            if (_gated) SetGated(false);
            Log.Info("[Workbench] 就绪：目录行已按 Tbuiform 生成", LogTag.Workbench);
        }

        private bool BuildRows()
        {
            if (_rowTemplate == null || _rowsParent == null)
            {
                _lastOp = "面板未接线（Clone_FormRow 模板缺失）——重跑 AgentScripts/BuildWorkbenchPanel.cs";
                return false;
            }
            int y = 0;
            foreach (var row in _forms.DataList)
            {
                bool system = row.Id is 201 or 202 or 203;             // 系统反馈面——不走目录开关（演示组承担）
                var inst = Instantiate(_rowTemplate, _rowsParent);
                inst.SetActive(true);
                var rt = (RectTransform)inst.transform;
                rt.anchoredPosition = new Vector2(0f, -y * 44f);
                y++;

                var idText = inst.transform.Find("Txt_FormId")?.GetComponent<TMPro.TMP_Text>();
                if (idText != null) idText.text = $"{row.Id}  {row.LuaPath}{(system ? "（系统面）" : "")}";

                var open = inst.transform.Find("Button_Open")?.GetComponent<Button>();
                var close = inst.transform.Find("Button_Close")?.GetComponent<Button>();
                if (system)
                {
                    if (open != null) open.interactable = false;
                    if (close != null) close.interactable = false;
                    continue;
                }
                int id = row.Id;
                if (open != null) open.onClick.AddListener(() => SafeGo(id).Forget());
                if (close != null) close.onClick.AddListener(() => SafeClose(id).Forget());
            }
            return true;
        }

        private async UniTaskVoid SafeGo(int id)
        {
            try
            {
                _lastForm = await _nav.GoAsync(id);                    // W2：记注入目标（目录打开即取用口）
                _lastOp = $"开 {id} 完成";
            }
            catch (Exception ex) { _lastOp = $"开 {id} 失败:{ex.Message}"; }
        }

        private async UniTaskVoid SafeClose(int id)
        {
            try
            {
                await _ui.CloseAsync(id);
                if (_lastForm != null && _lastForm.Id == id) _lastForm = null;
                _lastOp = $"关 {id} 完成";
            }
            catch (Exception ex) { _lastOp = $"关 {id} 失败:{ex.Message}"; }
        }

        private void OnLoading()
        {
            _loadingScope = _feedback.BeginLoading("工作台阻断演示");
            _lastOp = "Loading 开（EndLoading 收场）";
        }

        private void OnProgress()
        {
            if (!_loadingScope.HasValue) { _lastOp = "先点 Loading 开——进度写作用域"; return; }
            _demoStep++;
            _loadingScope.Value.Report($"工作台演示进度 {_demoStep}/4", Mathf.Clamp01(_demoStep / 4f));
            _lastOp = $"进度上报 {_demoStep}/4";
        }

        private void OnEndLoading()
        {
            if (_loadingScope.HasValue) _loadingScope.Value.Dispose();
            _loadingScope = null;
            _lastOp = "Loading 收场";
        }

        private async UniTaskVoid OnError()
        {
            try { await _feedback.ShowErrorAsync("工作台", "错误弹窗演示", retryable: false); _lastOp = "Error 弹窗关闭"; }
            catch (Exception ex) { _lastOp = "Error 演示失败:" + ex.Message; }
        }

        private void OnToast()
        {
            _feedback.ShowToast("工作台 Toast 演示");
            _lastOp = "Toast 已发";
        }

        // ---- IV：落点退出（复用对局退场 UI：ESC → 设置页 → 退出工作台 → 回大厅页）----

        /// <summary>装订平台返回：先走原链（关弹窗/出栈），无可返回目标才开菜单（对局内同款语义）。</summary>
        private void BindMenuBack()
        {
            if (_ui == null || _ui.Focus.PlatformBack == MenuBack) return;
            _savedBack = _ui.Focus.PlatformBack;                   // 幂等：重复装订不叠加包装
            _ui.Focus.PlatformBack = MenuBack;
        }

        private async UniTask<bool> MenuBack()
        {
            Func<UniTask<bool>> original = _savedBack;
            if (original != null && await original()) return true;
            OpenMenu();
            return true;
        }

        /// <summary>打开对局菜单（设置页）——导航数据带 battleExit（显退出钮）＋ exitKey（落点文案）。</summary>
        private void OpenMenu()
        {
            if (_ui == null || _nav == null) return;
            if (_ui.IsOpen(MenuFormId)) return;
            if (_lua == null || _lua.Env == null) { _lastOp = "菜单不可用（Lua 环境未就绪）"; return; }
            var table = _lua.Env.NewTable();
            table.Set("battleExit", true);
            table.Set("exitKey", ExitWorkbenchKey);
            MenuOpenAsync(new LuaUIData(table)).Forget();
        }

        private async UniTaskVoid MenuOpenAsync(IUIData data)
        {
            try
            {
                _menuForm = await _nav.GoAsync(MenuFormId, data);
                _lastOp = "菜单已开（退出＝退出工作台）";
            }
            catch (Exception ex) { _lastOp = "菜单打开失败:" + ex.Message; }
        }

        /// <summary>工作台 UI 自我收尾（场景不可卸时的落点收尾）：还原平台返回装订 → 收 HUD 预览 →
        /// 隐藏本件根（工作台从此不在，画面回到大厅页）。</summary>
        private void ShutdownWorkbenchUi()
        {
            if (_ui != null && _ui.Focus.PlatformBack == MenuBack) _ui.Focus.PlatformBack = _savedBack;
            CloseHudPreview();
            Log.Info("工作台模式：工作台 UI 已收尾（场景不可卸——隐藏形态；回大厅页）", LogTag.Workbench);
            gameObject.SetActive(false);
        }

        /// <summary>退出识别：本件开出的菜单已关 **且大厅页在场**（退出链的 `Replace(Lobby)` 是唯一来源）→
        /// 请求落点收尾（关工作台场景，由流程层执行——读即清请求桥）。</summary>
        private void WatchMenuExit()
        {
            if (_menuForm == null || _menuForm.IsOpen) return;
            if (_lobbyFormId < 0) _lobbyFormId = Bridge.Data.ResolveUIFormId(LuaKeys.UI.Page_Lobby);
            // 菜单已关但大厅还在转场（Loading 态 IsOpen=false）——**不能在此清标记**，否则条件永不成立；
            // 留待后续帧复查，直到大厅真正打开（退出链的 Replace(Lobby) 是唯一来源）才算退出完成。
            if (_lobbyFormId < 0 || _ui == null || !_ui.IsOpen(_lobbyFormId)) return;
            _menuForm = null;
            DebugTuner.RequestWorkbenchExit();
            _lastOp = "工作台退出（落点收尾 → 回大厅页）";
        }

        // ---- W2：语言切换行（真语言服务经 LuaBridge 门面——设置页同一条链）----

        private void SetLocale(string locale)
        {
            try
            {
                Bridge.Text.SetLocale(locale);                          // 页面索引订阅 OnLocaleChanged 自动重刷（ILocaleBound）
                _lastOp = $"语言切换:{locale}";
            }
            catch (Exception ex) { _lastOp = "语言切换失败:" + ex.Message; }
        }

        private string CurrentLocale()
        {
            try { return Bridge.Text.Locale; }
            catch { return "—"; }                                       // 未装配/未 BindText——状态行不炸
        }

        // ---- W2：HUD 预览（真装配取件：内容服务租约 + 裸实例化；零数据源 = 静态默认画面）----

        private void OnHudPreview()
        {
            if (_hudInstance != null)
            {
                CloseHudPreview();
                _lastOp = "HUD 预览已关";
                return;
            }
            PreviewHudAsync().Forget();
        }

        private async UniTaskVoid PreviewHudAsync()
        {
            try
            {
                _hudLease = await _content.AcquireAsync<GameObject>(BattleHudView.PrefabLocation, ct: destroyCancellationToken);
                _hudInstance = Instantiate(_hudLease.Asset);
                _hudInstance.name = "Hud_Battle(工作台预览)";
                if (_hudPreviewLabel != null) _hudPreviewLabel.text = "HUD 预览:开";
                _lastOp = "HUD 预览已开（无对局数据源——数值/准心为默认画面）";
            }
            catch (Exception ex)
            {
                _hudLease = null;
                _hudInstance = null;
                _lastOp = "HUD 预览失败:" + ex.Message;
            }
        }

        private void CloseHudPreview()
        {
            if (_hudInstance != null) { Destroy(_hudInstance); _hudInstance = null; }
            _hudLease?.Dispose();
            _hudLease = null;
            if (_hudPreviewLabel != null) _hudPreviewLabel.text = "HUD 预览:关";
        }

        // ---- W2：状态注入（预览性质；目标 = 目录打开的最近页，见类注释作用域）----

        private UIForm TargetForm()
        {
            if (_lastForm != null && _lastForm.IsOpen) return _lastForm;
            _lastForm = null;
            return null;
        }

        private void OnInjectEmpty()
        {
            var form = TargetForm();
            if (form == null) { _lastOp = "状态注入需先经目录打开一个页面"; return; }
            int cleared = 0;
            foreach (var list in form.Root.GetComponentsInChildren<VirtualList>(true)) { list.SetSource(EmptySource); cleared++; }
            foreach (var list in form.Root.GetComponentsInChildren<SimpleListView>(true)) { list.SetSource(EmptySource); cleared++; }
            _listsCleared |= cleared > 0;
            _lastOp = cleared > 0 ? $"空列表注入:{cleared} 个列表（{form.Id}）" : $"本页无列表内核（{form.Id}）";
        }

        private void OnInjectLong()
        {
            var form = TargetForm();
            if (form == null) { _lastOp = "状态注入需先经目录打开一个页面"; return; }
            if (_inflated.Count == 0)
            {
                foreach (var text in form.Root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (string.IsNullOrEmpty(text.text)) continue;     // 空文本注入不可见——跳过
                    _inflated.Add(new KeyValuePair<TMP_Text, string>(text, text.text));
                    text.text += LongTextSample;
                }
            }
            _lastOp = $"长文本注入:{_inflated.Count} 处（{form.Id}；「复位注入」还原）";
        }

        private void OnInjectReset()
        {
            int restored = 0;
            foreach (var pair in _inflated)
            {
                if (pair.Key == null) continue;
                pair.Key.text = pair.Value;                            // 只还原注入时点记录的原文
                restored++;
            }
            _inflated.Clear();
            var form = TargetForm();
            if (_listsCleared && form != null)
            {
                ReopenAsync(form.Id).Forget();                         // 列表复位走真链路（关+开）——注入无从"反推"原数据源
                _listsCleared = false;
                _lastOp = $"注入复位:文本 {restored} 处；列表经重开 {form.Id} 复位";
                return;
            }
            _listsCleared = false;
            _lastOp = $"注入复位:文本 {restored} 处";
        }

        private async UniTaskVoid ReopenAsync(int id)
        {
            try
            {
                await _ui.CloseAsync(id);
                _lastForm = await _nav.GoAsync(id);
                _lastOp = $"页面重开 {id} 完成（列表已复位）";
            }
            catch (Exception ex) { _lastOp = $"页面重开 {id} 失败:{ex.Message}"; }
        }

        // ---- W2：prefab 回写工具入口（编辑器窗口；两程序集无引用关系，经菜单路径跳转）----

        private void OnWriteBack()
        {
#if UNITY_EDITOR
            bool opened = UnityEditor.EditorApplication.ExecuteMenuItem(WriteBackMenuPath);
            _lastOp = opened ? "回写工具已开（选中 prefab 实例后「取当前选择」）" : "回写菜单未找到（LiteGame.Editor 未编译？）";
#else
            _lastOp = "回写工具仅编辑器可用";
#endif
        }

        private string BuildStatus(string locale, string hud)
        {
            string stage = _fsm != null ? _fsm.Current.ToString() : "未解析";
            string loading = _feedback != null ? _feedback.IsLoading.ToString() : "?";
            string loadMsg = _feedback != null ? _feedback.CurrentLoadingMessage : "?";   // W1：加载文案回读
            return $"阶段:{stage}  就绪:{_ready}  语言:{locale}  HUD:{hud}  Loading:{loading}({loadMsg})  最近:{_lastOp}";
        }

        /// <summary>工具条窄幅读数（HUD 关/开 + 最近操作；超过 40 字截断——全量读数在面板状态行）。</summary>
        private string BuildCompactStatus(string hud)
        {
            string op = _lastOp.Length <= 40 ? _lastOp : _lastOp.Substring(0, 40) + "…";
            return $"HUD:{hud}  最近:{op}";
        }

        private void SetGated(bool gated)
        {
            _gated = gated;
            foreach (var b in _gatedButtons) b.interactable = !gated;
        }
    }
}
#endif
