using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LiteClient;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteNet.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteGame
{
    /// <summary>开发 HUD（只读快照展示）。**自创建形态**：[RuntimeInitializeOnLoadMethod] 在场景加载后
    /// 自建 GameObject（场景**不挂**组件——避免三宏 #if 剥离时场景序列化组件报
    /// "not derived from MonoBehaviour"；同 DevStartupConfig 手法）。
    ///
    /// **不要把本目录移进任何 `Editor/` 下**：`Assets/.../Editor/` 是特殊文件夹，其中的
    /// MonoBehaviour **不能 AddComponent**——运行时报 "it is an editor script"，组件加不上、
    /// HUD 彻底不出现（本件为**代码创建**形态，直接命中）——故本件住开发装配层
    /// `Assets/LiteGame/Dev/Projection`（asmdef 三宏 defineConstraints + 文件内 `#if` 双重剥离）。
    /// Player 里的剥离由三宏负责，不靠程序集边界。
    /// 自拉取模式：LiteGame.Dev → LiteClient.Runtime 单向引用，HUD 在 Start 经
    /// FindAnyObjectByType 拉 `GameEntry.Stats`（只读统计访问器，非解析入口）+ 场景组件型 IModuleStats 合并；
    /// **场景加载后重扫**（对局场景里的 Animation/Lua 组件才进得来），每轮轮询剔除已销毁件。
    /// **各段渲染开关 = public 字段**（Inspector 可配 / 代码可改）：showStats /
    /// statToggles（单模块段 bool 开关）/ showNetDiag（联机诊断段——读口 <see cref="BattleDiagnostics"/>，
    /// 无对局整段不显示）/ showPerf（性能段——本机帧样本自算）/ showLogRecent / logRecentLines / showErrorsLine。
    /// 首次运行（本机无折叠记忆）的展开集读 `DevStartupConfig.DevHudDefaultExpanded`；此后以持久化折叠态为准。
    /// 0.25s 节流轮询：聚合单串，交 UI Toolkit `Label` 渲染（`DevHudUiToolkitHost`；字体不可用或
    /// `renderWithUiToolkit=false` 时自动回退 IMGUI）；**帧采样与可见性解耦**（隐藏期间性能窗不冻结）。
    /// 热键：F1 总开关 / F4 复制文本 / F5 日志级别过滤 / F6 仅本局 / F7 落现场包 / F8 导出 JSON / F9 过滤模式
    /// （F2 = 开发面板、F3 = 开发控制台）。
    /// 日志行带关联上下文（会话/对局/帧/席位，见 <see cref="LogEntry"/>）；只读，不写运行态。</summary>
    public sealed class DevHUD : MonoBehaviour
    {
        /// <summary>单模块 stats 段开关（按 StatsName 一段一 bool，Inspector 直改；未列出的模块默认隐藏＝白名单）。</summary>
        [Serializable]
        public class StatToggle
        {
            public string statsName;
            public bool show = true;
        }

        [Header("渲染开关（Inspector / 代码均可改）")]
        [Tooltip("各模块 stats 段总开关")]
        public bool showStats = true;
        [Tooltip("单模块段 bool 开关（按 StatsName 匹配；未列出的默认显示）")]
        public List<StatToggle> statToggles = new List<StatToggle>
        {
            new StatToggle { statsName = "MainThreadDispatcher" },
            new StatToggle { statsName = "Clock.World" },
            new StatToggle { statsName = "Clock.UI" },
            new StatToggle { statsName = "EventCenter" },
            new StatToggle { statsName = "Game" },
            new StatToggle { statsName = "Lua" },
        };
        [Tooltip("最近日志尾巴（Log.Recent 环缓冲渲染）")]
        public bool showLogRecent = true;
        [Tooltip("Recent 渲染条数（环缓冲容量 32）")]
        [Range(1, 32)] public int logRecentLines = 10;
        [Tooltip("错误计数行（仅 ErrorCount > 0 时渲染）")]
        public bool showErrorsLine = true;
        [Tooltip("联机诊断段（相位/RTT/丢包/和解——BattleDiagnostics 读口；无对局整段不显示）")]
        public bool showNetDiag = true;
        [Tooltip("性能段（帧率/最差帧/长帧计数——内核性能读模型，非对局数据）")]
        public bool showPerf = true;

        [Header("外观")]
        [Tooltip("HUD 宽度（像素；运行时按屏幕宽夹取）")]
        public int hudWidth = 480;
        [Tooltip("字号（像素；8–28 夹取）")]
        public int hudFontSize = 12;
        [Tooltip("顶部偏移（像素；默认让开左上「开发」入口）")]
        public int hudTop = 88;
        [Tooltip("左偏移（像素；拖动状态行可改，持久化）")]
        public int hudLeft = 8;
        [Tooltip("DPI/字号缩放（0.5–3.0；只缩放字号）")]
        [Range(0.5f, 3f)] public float hudScale = 1f;
        [Tooltip("用 UI Toolkit 渲染（默认）；关闭则回退 IMGUI")]
        public bool renderWithUiToolkit = true;

        private static readonly string[] LevelFilterNames = { "全部", "警告+", "错误+" };

        private DebugKernel _kernel;
        private readonly DevHudSectionOptions _sectionOptions = new DevHudSectionOptions();
        private DevHudSectionSet _sectionSet;
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);
        private bool _hasSavedLayout;
        private readonly DevHudFilter _filter = new DevHudFilter();
        private bool _filterMode;
        private string _statusLine = "";
        private readonly StringBuilder _sb = new StringBuilder(1024);                              // 复用，避免每轮 new
        private string _cache = "";
        private bool _cacheDirty = true;
        private float _nextPoll;
        private bool _visible = true;
        private int _logFilter;              // 0 全部 / 1 警告+ / 2 错误+
        private bool _onlyCurrentMatch;
        private string _toast = "";
        private float _toastUntil;

        private GUIStyle _style;
        private Vector2 _scroll;
        private readonly GUIContent _content = new GUIContent();
        private string _layoutCache;
        private int _layoutWidth = -1;
        private float _layoutHeight;

        private DiagnosticsBundleWriter _diagWriter;
        private DevHudUiToolkitHost _ui;          // null = IMGUI 回退

        /// <summary>总开关（编辑器 GM 面板投影位；F1 等效翻转——开发包 Player 无 GM 面板时 F1 即唯一入口）。</summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (!value) ExitFilterMode(false);
                if (_ui != null) _ui.Visible = value;
            }
        }

        /// <summary>模块 Stats 段白名单：只显示 <see cref="statToggles"/> 里勾选的模块，未登记默认隐藏
        /// （按需显隐——新模块不再一注册就冒出来；勾选状态经 <see cref="DevHudPrefs"/> 持久化）。</summary>
        private bool ShowSection(string statsName)
        {
            for (int i = 0; i < statToggles.Count; i++)
                if (string.Equals(statToggles[i].statsName, statsName, StringComparison.Ordinal))
                    return statToggles[i].show;
            return false;
        }

        /// <summary>当前发现的模块名（容器件 + 场景件；GM 页/F3 控制台生成勾选清单用）。</summary>
        public IReadOnlyList<string> StatsSectionNames
        {
            get
            {
                if (_kernel == null) return Array.Empty<string>();
                IReadOnlyList<IModuleStats> stats = _kernel.Stats;
                var names = new List<string>(stats.Count);
                for (int i = 0; i < stats.Count; i++) names.Add(stats[i].StatsName);
                return names;
            }
        }

        public bool IsStatsSectionVisible(string statsName) => ShowSection(statsName);

        /// <summary>按需显隐单个模块段（GM 页/F3 控制台入口；未登记的模块首次设置时补登记）。</summary>
        public void SetStatsSectionVisible(string statsName, bool visible)
        {
            if (string.IsNullOrEmpty(statsName)) return;
            for (int i = 0; i < statToggles.Count; i++)
            {
                if (!string.Equals(statToggles[i].statsName, statsName, StringComparison.Ordinal)) continue;
                statToggles[i].show = visible;
                SaveStatsVisibility();
                return;
            }
            statToggles.Add(new StatToggle { statsName = statsName, show = visible });
            SaveStatsVisibility();
        }

        /// <summary>批量显隐（F3 <c>hud stats all|none</c>）。</summary>
        public void SetAllStatsSectionsVisible(bool visible)
        {
            for (int i = 0; i < statToggles.Count; i++) statToggles[i].show = visible;
            SaveStatsVisibility();
        }

        private void SaveStatsVisibility()
        {
            var sb = new StringBuilder(128);
            for (int i = 0; i < statToggles.Count; i++)
            {
                if (!statToggles[i].show || string.IsNullOrEmpty(statToggles[i].statsName)) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(statToggles[i].statsName);
            }
            DevHudPrefs.SetString("statsOn", sb.ToString());
            _cacheDirty = true;
            _sectionSet?.InvalidateStats();
        }

        /// <summary>会话间恢复白名单（有记忆时覆盖 Inspector 预置：与折叠态同口径「保存态优先」）。</summary>
        private void LoadStatsVisibility()
        {
            if (!DevHudPrefs.Has("statsOn")) return;
            var enabled = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in DevHudPrefs.GetString("statsOn", "").Split('|'))
                if (name.Length > 0) enabled.Add(name);
            for (int i = 0; i < statToggles.Count; i++)
                statToggles[i].show = enabled.Contains(statToggles[i].statsName);
            foreach (string name in enabled)
            {
                bool known = false;
                for (int i = 0; i < statToggles.Count; i++)
                    if (string.Equals(statToggles[i].statsName, name, StringComparison.Ordinal)) { known = true; break; }
                if (!known) statToggles.Add(new StatToggle { statsName = name, show = true });
            }
        }

        private void Awake()
        {
            LoadPrefs();
            _sectionOptions.ShowStat = ShowSection;
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void Start()
        {
            TryBindKernel();
            if (!renderWithUiToolkit) return;
            try
            {
                _ui = new DevHudUiToolkitHost(transform);
                _ui.Visible = _visible;
            }
            catch (Exception ex)
            {
                _ui = null;   // 失败回退 IMGUI（不能因为渲染后端装不上就没有 HUD）
                Log.Warning("DevHUD UI Toolkit 初始化失败，回退 IMGUI: " + ex.Message, LogTag.None);
            }
        }

        private void OnDestroy()
        {
            SavePrefs();
            DevHotkey.SetTextInputActive(this, false);
            _ui?.Dispose();
            _ui = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _kernel?.RebuildStats();
            _sectionSet?.InvalidateStats();
        }

        /// <summary>延迟绑定内核：GameEntry 引导完成前 HUD 先空态，装配完成后自动接管段集。</summary>
        private void TryBindKernel()
        {
            if (_kernel != null) return;
            GameEntry entry = FindAnyObjectByType<GameEntry>();
            _kernel = entry != null ? entry.DebugKernel : null;
            if (_kernel == null) return;
            _kernel.RebuildStats();
            _sectionSet = new DevHudSectionSet(_kernel, _filter, _sectionOptions);
            if (!_hasSavedLayout) ApplyStartupSectionLayout();
        }

        private void Update()
        {
            if (_sectionSet == null) TryBindKernel();
            if (_sectionSet == null) return;               // 内核尚未装配：空态，不阻塞游戏
            _kernel.SampleFrame(Time.unscaledDeltaTime);   // 采样与可见性解耦：隐藏期间性能窗不冻结
            _sectionSet.SampleNet();
            if (!_visible || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.25f;
            _kernel?.PruneStats();

            float started = Time.realtimeSinceStartup;
            BuildSections();
            float sampleMs = (float)((Time.realtimeSinceStartupAsDouble - started) * 1000.0);
            _kernel.RecordHudBuildMs(sampleMs);
        }

        private void BuildSections()
        {
            _statusLine = BuildStatusLine();
            _sectionOptions.ShowErrors = showErrorsLine;
            _sectionOptions.ShowNet = showNetDiag;
            _sectionOptions.ShowPerf = showPerf;
            _sectionOptions.ShowStats = showStats;
            _sectionOptions.ShowLogRecent = showLogRecent;
            _sectionOptions.LogRecentLines = logRecentLines;
            _sectionOptions.LogFilter = _logFilter;
            _sectionOptions.OnlyCurrentMatch = _onlyCurrentMatch;
            _sectionSet.Build();

            _cacheDirty = true;
            if (_ui == null)
            {
                _cache = BuildPlainText(_statusLine);      // IMGUI 回退每轮要整串
                _cacheDirty = false;
            }
            if (_ui != null)
                _ui.Render(_statusLine, _sectionSet.Data, hudWidth, hudFontSize, hudLeft, hudTop, hudScale,
                    _collapsed, OnSectionToggle, OnHudMoved);
        }

        /// <summary>F2 复制需要整串；UITK 后端下按需构建，避免每 0.25s 白拼一份大字符串。</summary>
        private string CacheText()
        {
            if (_sectionSet == null) return string.Empty;
            if (_cacheDirty)
            {
                _cache = BuildPlainText(_statusLine);
                _cacheDirty = false;
            }
            return _cache;
        }

        private string BuildStatusLine()
        {
            _sb.Clear();
            if (_filterMode)
                _sb.Append("过滤模式：输入文本或 @tag · Enter 应用 · Esc 清除 · F9 退出");
            else
                _sb.Append("F1 隐藏 · F2 面板 · F3 控制台 · F4 复制 · F5 级别 · F6 本局 · F7 现场包 · F8 JSON · F9 过滤 · 拖状态行移动");
            if (_filter.Raw.Length > 0) _sb.Append(" · 过滤=\"").Append(_filter.Raw).Append('"');
            if (_logFilter != 0) _sb.Append(" · 级别=").Append(LevelFilterNames[_logFilter]);
            if (_onlyCurrentMatch) _sb.Append(" · 仅本局");
            return _sb.ToString();
        }

        private string BuildPlainText(string status)
        {
            _sb.Clear();
            _sb.Append(status).Append('\n');
            for (int i = 0; i < _sectionSet.Data.Count; i++)
            {
                DevHudSectionData data = _sectionSet.Data[i];
                if (!data.Visible) continue;
                _sb.Append("── ").Append(data.Title).Append(" ──\n");
                for (int j = 0; j < data.Lines.Count; j++)
                    _sb.Append(data.Lines[j].Text).Append('\n');
            }
            return _sb.ToString();
        }

        private void ToggleFilterMode()
        {
            bool enable = !_filterMode;
            if (enable && !DevHotkey.SetTextInputActive(this, true))
            {
                Toast("输入被其他调试面占用（F3 控制台）");
                return;
            }
            _filterMode = enable;
            if (!enable) DevHotkey.SetTextInputActive(this, false);
            Toast(_filterMode ? "过滤模式：输入文本或 @tag" : "退出过滤模式");
        }

        private void ExitFilterMode(bool clear)
        {
            _filterMode = false;
            DevHotkey.SetTextInputActive(this, false);
            if (clear) _filter.Clear();
            SavePrefs();
        }

        private void HandleFilterInput()
        {
            string typed = DevHotkey.TakeTypedText(this);
            if (typed.Length > 0) _filter.Append(typed);

            if (DevHotkey.Pressed(DevKey.Backspace)) _filter.Backspace();

            if (DevHotkey.Pressed(DevKey.Enter))
            {
                ExitFilterMode(false);
                Toast("过滤: " + (_filter.Raw.Length == 0 ? "(无)" : _filter.Raw));
            }
            else if (DevHotkey.Pressed(DevKey.Escape))
            {
                ExitFilterMode(true);
                Toast("过滤已清除");
            }
        }

        private void LoadPrefs()
        {
            if (DevHudPrefs.Has("width")) hudWidth = DevHudPrefs.GetInt("width", hudWidth);
            if (DevHudPrefs.Has("font")) hudFontSize = DevHudPrefs.GetInt("font", hudFontSize);
            if (DevHudPrefs.Has("left")) hudLeft = DevHudPrefs.GetInt("left", hudLeft);
            if (DevHudPrefs.Has("top")) hudTop = DevHudPrefs.GetInt("top", hudTop);
            if (DevHudPrefs.Has("scale")) hudScale = DevHudPrefs.GetFloat("scale", hudScale);
            if (DevHudPrefs.Has("logFilter")) _logFilter = DevHudPrefs.GetInt("logFilter", _logFilter);
            if (DevHudPrefs.Has("onlyMatch")) _onlyCurrentMatch = DevHudPrefs.GetInt("onlyMatch", 0) != 0;
            if (DevHudPrefs.Has("filter"))
                _filter.Set(DevHudPrefs.GetString("filter", ""));
            _hasSavedLayout = DevHudPrefs.Has("collapsed");
            if (_hasSavedLayout)
            {
                foreach (string title in DevHudPrefs.GetString("collapsed", "").Split('|'))
                    if (title.Length > 0) _collapsed.Add(title);
            }
            LoadStatsVisibility();
        }

        private void SavePrefs()
        {
            DevHudPrefs.SetInt("width", hudWidth);
            DevHudPrefs.SetInt("font", hudFontSize);
            DevHudPrefs.SetInt("left", hudLeft);
            DevHudPrefs.SetInt("top", hudTop);
            DevHudPrefs.SetFloat("scale", hudScale);
            DevHudPrefs.SetInt("logFilter", _logFilter);
            DevHudPrefs.SetInt("onlyMatch", _onlyCurrentMatch ? 1 : 0);
            DevHudPrefs.SetString("filter", _filter.Raw);
            DevHudPrefs.SetString("collapsed", string.Join("|", _collapsed));
        }

        private void OnSectionToggle(string title)
        {
            if (!_collapsed.Add(title)) _collapsed.Remove(title);
            SavePrefs();
        }

        private void OnHudMoved(int left, int top)
        {
            hudLeft = left;
            hudTop = top;
            SavePrefs();
        }

        /// <summary>按 `DevStartupConfig.DevHudDefaultExpanded` 重排展开集（覆盖已持久化的折叠记忆；GM 页入口）。</summary>
        public void ApplyDefaultSectionLayout()
        {
            if (_sectionSet == null) TryBindKernel();
            if (_sectionSet == null) { Toast("HUD 未就绪"); return; }
            _hasSavedLayout = true;
            ApplyStartupSectionLayout();
            SavePrefs();
            _cacheDirty = true;
            Toast("已按启动配置重排段布局");
        }

        private void ApplyStartupSectionLayout()
        {
            DevHudSectionFlags expanded = DevStartupConfig.ResolveDevHudDefaultExpanded();
            _collapsed.Clear();
            for (int i = 0; i < _sectionSet.Data.Count; i++)
            {
                DevHudSectionData data = _sectionSet.Data[i];
                if ((expanded & data.Flag) == 0) _collapsed.Add(data.Title);
            }
        }

        /// <summary>导出当前读数（含段/行/色调/过滤/后端）为 JSON；脚本可直接消费。</summary>
        private void ExportJson()
        {
            if (_sectionSet == null) { Toast("HUD 未就绪"); return; }
            try
            {
                var ctx = new DevHudExportContext
                {
                    Session = _kernel.CurrentSessionId,
                    Backend = _ui != null ? "uitk" : "imgui",
                    Visible = _visible,
                    Left = hudLeft,
                    Top = hudTop,
                    Width = hudWidth,
                    Font = hudFontSize,
                    Scale = hudScale,
                    LogFilter = _logFilter,
                    OnlyMatch = _onlyCurrentMatch,
                    FilterRaw = _filter.Raw,
                    FilterNeedle = _filter.Needle,
                    FilterTags = _filter.Tags,
                    Sections = _sectionSet.Data
                };
                string path = DevHudJsonExporter.Export(ctx);
                Toast("已导出 JSON: " + path);
            }
            catch (Exception ex)
            {
                Toast("导出失败: " + ex.Message);
            }
        }

        private void CopyToClipboard()
        {
            GUIUtility.systemCopyBuffer = CacheText();
            Toast("已复制 HUD 文本");
        }

        /// <summary>把当前可见读数落成一份现场包（与 mismatch/异常同目录、同信封；reason=hud-snapshot）。</summary>
        private void DumpBundle()
        {
            if (_kernel == null || !_kernel.TryTakeBattleSnapshot(out BattleDiagnostics.Snapshot snapshot))
            { Toast("无对局上下文，未落盘"); return; }
            try
            {
                _diagWriter ??= new DiagnosticsBundleWriter(
                    Path.Combine(Application.persistentDataPath, "diagnostics"));
                string path = ClientDiagnosticsBundle.Capture(_diagWriter, "hud-snapshot", snapshot.MatchId,
                    snapshot.Frame, snapshot.LocalEntityId, (int)Math.Min(snapshot.ReconcileCount, int.MaxValue),
                    snapshot.ConfigDigest);
                Toast(path != null ? "已落盘: " + path : "未写出（限频/IO）");
            }
            catch (Exception ex) { Toast("落盘失败: " + ex.Message); }
        }

        private void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 2.5f;
            if (_ui != null) _ui.ShowToast(text);
        }

        private void OnGUI()
        {
            if (_ui != null) return;                    // UI Toolkit 后端接管绘制
            if (!_visible || string.IsNullOrEmpty(_cache)) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            _style.fontSize = Mathf.Clamp(hudFontSize, 8, 28);
            _style.normal.textColor = Color.white;
            _content.text = _cache;

            float width = Mathf.Clamp(hudWidth, 240, Mathf.Max(240, Screen.width - 16));
            float left = Mathf.Clamp(hudLeft, 0, Mathf.Max(0, Screen.width - width));
            float top = Mathf.Clamp(hudTop, 0, Mathf.Max(0, Screen.height - 80));
            if (!ReferenceEquals(_layoutCache, _cache) || _layoutWidth != (int)width)
            {
                _layoutCache = _cache;
                _layoutWidth = (int)width;
                _layoutHeight = _style.CalcHeight(_content, width);
            }
            float viewHeight = Mathf.Min(_layoutHeight + 8f, Screen.height - top - 8f);
            var view = new Rect(left, top, width, viewHeight);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0f, 0f, width - 20f, _layoutHeight));
            GUI.Label(new Rect(0f, 0f, width - 20f, _layoutHeight), _content, _style);
            GUI.EndScrollView();

            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil)
                GUI.Label(new Rect(left + 4f, top + viewHeight + 4f, width, 24f), _toast, _style);
        }

        private void LateUpdate()
        {
            if (DevHotkey.Pressed(DevKey.F1))
            {
                Visible = !Visible;
                if (!_visible) return;
            }
            if (!_visible) return;

            if (DevHotkey.Pressed(DevKey.F9)) ToggleFilterMode();
            if (_filterMode)
            {
                HandleFilterInput();
                return;
            }

            if (DevHotkey.Pressed(DevKey.F4)) CopyToClipboard();
            else if (DevHotkey.Pressed(DevKey.F5)) { _logFilter = (_logFilter + 1) % LevelFilterNames.Length; SavePrefs(); }
            else if (DevHotkey.Pressed(DevKey.F6)) { _onlyCurrentMatch = !_onlyCurrentMatch; SavePrefs(); }
            else if (DevHotkey.Pressed(DevKey.F7)) DumpBundle();
            else if (DevHotkey.Pressed(DevKey.F8)) ExportJson();
        }
    }

    /// <summary>HUD 自创建入口（场景加载后；幂等）。release 构建随外层 #if 整体剥离。</summary>
    internal static class DevHUDBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (UnityEngine.Object.FindAnyObjectByType<DevHUD>() != null) return;
            var go = new GameObject("[DevHUD]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<DevHUD>();
        }
    }
}
#endif
