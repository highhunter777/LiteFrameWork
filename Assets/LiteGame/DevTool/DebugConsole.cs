#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using LiteClient;
using LiteFramework;
using LiteNet.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LiteGame
{
    /// <summary>
    /// 运行期操控投影（《GM 与调试工具分层专项设计》§7）：F12 文本控制台。
    /// 只做词法解析与输出格式化；命令执行/选项/请求桥/读数全部经 <see cref="DebugKernel"/>。
    /// 打开期间注册输入拦截并取得文本输入所有权，关闭/销毁幂等释放；release 随三宏剥离。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugConsole : MonoBehaviour
    {
        private const int MaxLines = 200;
        private const string InputBlockerName = "debug.console";

        private readonly List<string> _lines = new List<string>(64);
        private readonly StringBuilder _input = new StringBuilder(64);
        private readonly Dictionary<string, string> _statsBuffer = new Dictionary<string, string>(32);

        private DebugKernel _kernel;
        private IDisposable _inputLease;
        private bool _open;
        private bool _textOwned;
        private Vector2 _scroll;
        private float _nextBind;
        private bool _scrollToBottom;
        private bool _blockerAttempted;

        private GUIStyle _lineStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _hintStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindAnyObjectByType<DebugConsole>() != null) return;
            var go = new GameObject("[DebugConsole]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<DebugConsole>();
        }

        private void Awake()
        {
            Log.Info("调试控制台已装配（F12 开关）", LogTag.None);
        }

        private void Update()
        {
            if (DevHotkey.Pressed(DevKey.F12))
            {
                if (_open) Close();
                else Open();
                return;
            }

            if (!_open) return;

            if (_kernel == null && Time.unscaledTime >= _nextBind)
            {
                _nextBind = Time.unscaledTime + 0.5f;
                BindKernel();
                EnsureInputBlocker();
            }

            if (!_textOwned)
            {
                _textOwned = DevHotkey.SetTextInputActive(this, true);
                if (_textOwned) Append("输入已接管（Esc/F12 关闭）");
                else if (Time.frameCount % 60 == 0) Append("输入被 HUD 过滤占用：先按 F7 退出过滤");
            }

            if (!_textOwned) return;

            string typed = DevHotkey.TakeTypedText(this);
            if (typed.Length > 0) _input.Append(typed);

            if (DevHotkey.Pressed(DevKey.Backspace) && _input.Length > 0)
                _input.Length--;
            if (DevHotkey.Pressed(DevKey.Enter))
            {
                string line = _input.ToString();
                _input.Clear();
                Execute(line);
            }
            else if (DevHotkey.Pressed(DevKey.Escape))
            {
                Close();
            }
        }

        private void Open()
        {
            _open = true;
            _input.Clear();
            _blockerAttempted = false;
            BindKernel();
            EnsureInputBlocker();
            _textOwned = DevHotkey.SetTextInputActive(this, true);
            if (!_textOwned) Append("输入被 HUD 过滤占用：先按 F7 退出过滤");
            _scrollToBottom = true;
        }

        private void Close()
        {
            _open = false;
            _input.Clear();
            DevHotkey.SetTextInputActive(this, false);
            _textOwned = false;
            _inputLease?.Dispose();
            _inputLease = null;
            _blockerAttempted = false;
        }

        private void EnsureInputBlocker()
        {
            if (!_open || _kernel == null || _inputLease != null || _blockerAttempted) return;
            _blockerAttempted = true;
            try { _inputLease = _kernel.BlockInput(InputBlockerName, "调试控制台输入"); }
            catch (Exception ex) { AppendError("输入拦截注册失败：" + ex.Message); }
        }

        private void OnDestroy()
        {
            DevHotkey.SetTextInputActive(this, false);
            _textOwned = false;
            _inputLease?.Dispose();
            _inputLease = null;
        }

        private void BindKernel()
        {
            if (_kernel != null) return;
            GameEntry entry = Object.FindAnyObjectByType<GameEntry>();
            _kernel = entry != null ? entry.DebugKernel : null;
            if (_kernel != null) Append("调试内核已连接。输入 help 查看命令。");
        }

        private void Execute(string line)
        {
            if (!DebugConsoleCommand.TryParse(line, out string verb, out string[] args)) return;
            Append("> " + line);

            if (_kernel == null)
            {
                AppendError("调试内核未装配（非开发包或装配失败）");
                return;
            }

            try
            {
                Dispatch(verb, args);
            }
            catch (Exception ex)
            {
                AppendError(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Dispatch(string verb, string[] args)
        {
            switch (verb)
            {
                case "help":
                    AppendHelp();
                    break;
                case "commands":
                    AppendCommands();
                    break;
                case "run":
                    if (!Require(args, 1, "run <命令名>")) break;
                    if (_kernel.TryExecute(args[0], out CommandResult result, out string executeError))
                        Append(result.Ok ? $"OK：{result.Detail}" : $"拒绝（{result.Reason}）：{result.Detail}");
                    else
                        AppendError(executeError);
                    break;
                case "audit":
                    AppendAudit(TakeCount(args, 8));
                    break;
                case "options":
                    AppendOptions();
                    break;
                case "option":
                    if (!Require(args, 2, "option <Id> <on|off>")) break;
                    if (!TryParseBool(args[1], out bool value)) { AppendError("值需为 on/off/true/false/1/0"); break; }
                    if (_kernel.TrySetOption(args[0], value, out string optionError))
                        Append($"{args[0]} = {(value ? "开" : "关")}");
                    else
                        AppendError(optionError);
                    break;
                case "stats":
                    AppendStats(args.Length > 0 ? args[0] : null);
                    break;
                case "log":
                    AppendLogs(TakeCount(args, 10));
                    break;
                case "diag":
                    AppendDiag(TakeCount(args, 8));
                    break;
                case "battle":
                    AppendBattle();
                    break;
                case "request":
                    if (!Require(args, 1, "request <enter|exit|teleport|direct>")) break;
                    Request(args[0]);
                    break;
                case "undo":
                    Append(_kernel.Undo() ? "Undo 已执行" : "无可撤销命令");
                    break;
                case "redo":
                    Append(_kernel.Redo() ? "Redo 已执行" : "无可重做命令");
                    break;
                case "clear":
                    _lines.Clear();
                    break;
                default:
                    AppendError($"未知命令：{verb}（help 查看）");
                    break;
            }
        }

        private void AppendHelp()
        {
            Append("help                       本帮助");
            Append("commands                   列出已注册命令");
            Append("run <命令名>               执行无参命令（类型名或唯一后缀）");
            Append("audit [n]                  最近审计（默认 8）");
            Append("options                    测试模式开关与当前值");
            Append("option <Id> <on|off>       设置测试模式开关");
            Append("stats [模块名]             模块统计列表/单模块读数");
            Append("log [n]                    最近日志（默认 10）");
            Append("diag [n]                   DiagTrace（默认 8）");
            Append("battle                     对局诊断描述");
            Append("request <enter|exit|teleport|direct>  一次性请求桥");
            Append("undo / redo                命令撤销/重做");
            Append("clear                      清空输出");
            Append("F12 / Esc                   关闭控制台");
        }

        private void AppendCommands()
        {
            IReadOnlyList<CommandInfo> commands = _kernel.Commands;
            if (commands.Count == 0) { Append("(无注册命令)"); return; }
            for (int i = 0; i < commands.Count; i++)
            {
                CommandInfo info = commands[i];
                bool parameterless = info.CommandType.GetConstructor(Type.EmptyTypes) != null;
                string prefix = info.GmOnly ? "[GM] " : "";
                string hint = parameterless ? "" : " [需专面板]";
                Append($"{prefix}{info.CommandType.Name}{hint}  {info.Description}");
            }
        }

        private void AppendAudit(int count)
        {
            IReadOnlyList<CommandAuditEntry> audit = _kernel.Audit;
            if (audit.Count == 0) { Append("(无审计)"); return; }
            int from = Mathf.Max(0, audit.Count - count);
            for (int i = from; i < audit.Count; i++)
            {
                CommandAuditEntry entry = audit[i];
                Append($"#{entry.Seq} {entry.CommandType?.Name} {(entry.Ok ? "OK" : "拒:" + entry.Reason)} {entry.Detail}");
            }
        }

        private void AppendOptions()
        {
            IReadOnlyList<TestModeOptions.Info> options = _kernel.Options;
            for (int i = 0; i < options.Count; i++)
            {
                TestModeOptions.Info option = options[i];
                Append($"{option.Id} = {(_kernel.GetOption(option.Id) ? "开" : "关")}  [{option.Group}] {option.Label}");
            }
        }

        private void AppendStats(string statsName)
        {
            if (string.IsNullOrEmpty(statsName))
            {
                IReadOnlyList<IModuleStats> stats = _kernel.Stats;
                if (stats.Count == 0) { Append("(无统计模块)"); return; }
                for (int i = 0; i < stats.Count; i++) Append(stats[i].StatsName);
                return;
            }

            if (!_kernel.TrySnapshotStats(statsName, _statsBuffer, out string error))
            {
                AppendError(error);
                return;
            }
            Append("── " + statsName + " ──");
            foreach (KeyValuePair<string, string> pair in _statsBuffer)
                Append("  " + pair.Key + ": " + pair.Value);
        }

        private void AppendLogs(int count)
        {
            IReadOnlyList<LogEntry> logs = _kernel.RecentLogs;
            if (logs.Count == 0) { Append("(无日志)"); return; }
            int from = Mathf.Max(0, logs.Count - Mathf.Clamp(count, 1, 32));
            for (int i = from; i < logs.Count; i++)
            {
                LogEntry entry = logs[i];
                Append($"[{entry.Level}][{entry.Tag}] {entry.Message}");
            }
        }

        private void AppendDiag(int count)
        {
            IReadOnlyList<DiagEvent> diag = _kernel.RecentDiag;
            if (diag.Count == 0) { Append("(无 DiagTrace)"); return; }
            int from = Mathf.Max(0, diag.Count - count);
            for (int i = from; i < diag.Count; i++) Append(DiagTrace.Line(diag[i]));
        }

        private void AppendBattle()
        {
            string[] lines = _kernel.DescribeBattle();
            if (lines.Length == 0) { Append("(无对局)"); return; }
            for (int i = 0; i < lines.Length; i++) Append(lines[i]);
        }

        private void Request(string action)
        {
            switch (action)
            {
                case "enter": _kernel.RequestEnterTestMode(); Append("已请求进入测试模式"); break;
                case "exit": _kernel.RequestExitTestMode(); Append("已请求退出测试模式"); break;
                case "teleport": _kernel.RequestTeleport(); Append("已请求传送到准心"); break;
                case "direct": _kernel.RequestDirect(); Append("已请求直连对局"); break;
                default: AppendError("request 参数需为 enter/exit/teleport/direct"); break;
            }
        }

        private void Append(string line)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
            _scrollToBottom = true;
        }

        private void AppendError(string line) => Append("[!] " + line);

        private bool Require(string[] args, int count, string usage)
        {
            if (args != null && args.Length >= count) return true;
            AppendError("用法：" + usage);
            return false;
        }

        private static int TakeCount(string[] args, int fallback)
            => args != null && args.Length > 0 && int.TryParse(args[0], out int value)
                ? Mathf.Clamp(value, 1, 32)
                : fallback;

        private static bool TryParseBool(string text, out bool value)
        {
            value = false;
            if (string.Equals(text, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || text == "1")
            {
                value = true;
                return true;
            }
            if (string.Equals(text, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
                || text == "0")
            {
                return true;
            }
            return false;
        }

        private void OnGUI()
        {
            if (!_open) return;

            EnsureStyles();
            float height = Mathf.Clamp(Screen.height * 0.45f, 220f, Mathf.Max(220f, Screen.height - 40f));
            var panel = new Rect(0f, Screen.height - height, Screen.width, height);
            GUI.Box(panel, GUIContent.none);

            float lineHeight = 16f;
            var view = new Rect(panel.x + 8f, panel.y + 8f, panel.width - 16f, panel.height - 56f);
            var content = new Rect(0f, 0f, view.width - 20f, Mathf.Max(view.height, _lines.Count * lineHeight));
            if (_scrollToBottom)
            {
                _scroll.y = float.MaxValue;
                _scrollToBottom = false;
            }
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            for (int i = 0; i < _lines.Count; i++)
                GUI.Label(new Rect(0f, i * lineHeight, content.width, lineHeight), _lines[i], _lineStyle);
            GUI.EndScrollView();

            GUI.Label(new Rect(panel.x + 8f, panel.y + panel.height - 44f, panel.width - 16f, 18f),
                _textOwned ? "F12/Esc 关闭 · Enter 执行 · help 查看命令" : "输入被 HUD 过滤占用：先按 F7 退出过滤",
                _hintStyle);
            GUI.Label(new Rect(panel.x + 8f, panel.y + panel.height - 24f, panel.width - 16f, 20f),
                "> " + _input + (_textOwned ? "_" : ""), _inputStyle);
        }

        private void EnsureStyles()
        {
            if (_lineStyle != null) return;
            _lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = false };
            _lineStyle.normal.textColor = Color.white;
            _inputStyle = new GUIStyle(_lineStyle) { fontStyle = FontStyle.Bold };
            _inputStyle.normal.textColor = new Color(0.75f, 0.95f, 1f);
            _hintStyle = new GUIStyle(_lineStyle) { fontSize = 11 };
            _hintStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        }
    }
}
#endif
