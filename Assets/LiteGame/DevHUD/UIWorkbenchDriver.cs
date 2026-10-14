using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;
using LiteGame.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LiteGame
{
    /// <summary>
    /// UI 工作台驱动（《UI工作台设计》W0——dev-only，DevHUD 族"不担产品职责"）：
    /// 场景面板目录一键开/关任意已登记表单（真导航单写者）+ 反馈面演示组（Loading/错误/Toast）+ 状态读数。
    ///
    /// **不旁路装配**：Update 轮询 `GameEntry.TakeContainer()`（未装配下帧再试），服务经容器 Resolve；
    /// 就绪门控 = `ConfigService.Loaded`（表已发布）∧ `StageMachine.Current == ProcedureId.Main`
    /// （内容初始化完成、开页安全）——未就绪按钮置灰 + 状态行提示。**真链路照跑**：Patch 走本地通道、
    /// Main 阶段照常加载玩法场景（面板不阻止）。Play 内直开，无编译参与（非编辑器工具）。
    /// Button_Progress 依赖 `LoadingScope.Report`（加载页批A 接口）——未接线，占位记状态。
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

        private ServiceContainer _container;
        private UINavigationController _nav;
        private UIService _ui;
        private FeedbackService _feedback;
        private cfg.Tbuiform _forms;
        private StageMachine<ProcedureId, ProcedureArgs> _fsm;
        private FeedbackService.LoadingScope? _loadingScope;
        private bool _ready;
        private bool _gated;                       // 置灰已应用（避免每帧重复写）
        private int _frame;
        private string _lastOp = "—";
        private int _demoStep;                       // 进度演示步进（0.25/步——四步到 1.0 后回卷）

        private void Awake()
        {
            _btnLoading.onClick.AddListener(OnLoading);
            _btnProgress.onClick.AddListener(OnProgress);
            _btnEndLoading.onClick.AddListener(OnEndLoading);
            _btnError.onClick.AddListener(() => OnError().Forget());
            _btnToast.onClick.AddListener(OnToast);
        }

        private void Update()
        {
            if (!_ready) TryBecomeReady();
            if ((_frame++ & 31) == 0) _statusText.text = BuildStatus();   // dev 面板节流：约半秒一刷
        }

        private void TryBecomeReady()
        {
            var entry = FindAnyObjectByType<GameEntry>();
            if (entry == null) return;                                 // 引导件不在场——下帧再试
            try { _container = entry.TakeContainer(); }
            catch (InvalidOperationException) { return; }              // 未装配——下帧再试
            try
            {
                _nav = _container.Resolve<UINavigationController>();
                _ui = _container.Resolve<UIService>();
                _feedback = _container.Resolve<FeedbackService>();
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

            BuildRows();
            _ready = true;
            if (_gated) SetGated(false);
            Log.Info("[Workbench] 就绪：目录行已按 Tbuiform 生成", "Workbench");
        }

        private void BuildRows()
        {
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
        }

        private async UniTaskVoid SafeGo(int id)
        {
            try { await _nav.GoAsync(id); _lastOp = $"开 {id} 完成"; }
            catch (Exception ex) { _lastOp = $"开 {id} 失败:{ex.Message}"; }
        }

        private async UniTaskVoid SafeClose(int id)
        {
            try { await _ui.CloseAsync(id); _lastOp = $"关 {id} 完成"; }
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

        private string BuildStatus()
        {
            string stage = _fsm != null ? _fsm.Current.ToString() : "未解析";
            string loading = _feedback != null ? _feedback.IsLoading.ToString() : "?";
            string loadMsg = _feedback != null ? _feedback.CurrentLoadingMessage : "?";   // W1：加载文案回读
            return $"阶段:{stage}  就绪:{_ready}  Loading:{loading}({loadMsg})  最近:{_lastOp}";
        }

        private void SetGated(bool gated)
        {
            _gated = gated;
            foreach (var b in new[] { _btnLoading, _btnProgress, _btnEndLoading, _btnError, _btnToast })
                b.interactable = !gated;
        }
    }
}
