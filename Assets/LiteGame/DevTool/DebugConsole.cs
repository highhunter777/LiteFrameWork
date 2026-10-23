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
    /// 运行期操控投影（《GM 与调试工具分层专项设计》§7）：F12 文本控制台，开发页退役后的唯一运行期开发入口。
    /// 只做词法解析与输出格式化；命令执行/选项/请求桥/读数全部经 <see cref="DebugKernel"/>。
    /// 渲染走 UI Toolkit（<see cref="DebugConsoleUiToolkitHost"/>，无资产运行时面板）；输入仍经 DevHotkey 收口。
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
        private DebugConsoleUiToolkitHost _ui;
        private bool _open;
        private bool _textOwned;
        private float _nextBind;
        private bool _scrollToBottom;
        private bool _blockerAttempted;
        private bool _dirty = true;
        private bool _uiFailed;
        private int _screenW;
        private int _screenH;

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
            Log.Info("开发控制台已装配（F12 开关）", LogTag.UI);
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
            EnsureUi();

            if (!_textOwned)
            {
                _textOwned = DevHotkey.SetTextInputActive(this, true);
                if (_textOwned)
                {
                    Append("输入已接管（Esc/F12 关闭）");
                    _dirty = true;
                }
                else if (Time.frameCount % 60 == 0) Append("输入被 HUD 过滤占用：先按 F7 退出过滤");
            }

            if (_textOwned)
            {
                string typed = DevHotkey.TakeTypedText(this);
                if (typed.Length > 0)
                {
                    _input.Append(typed);
                    _dirty = true;
                }

                if (DevHotkey.Pressed(DevKey.Backspace) && _input.Length > 0)
                {
                    _input.Length--;
                    _dirty = true;
                }
                if (DevHotkey.Pressed(DevKey.Enter))
                {
                    string line = _input.ToString();
                    _input.Clear();
                    Execute(line);
                    _dirty = true;
                }
                else if (DevHotkey.Pressed(DevKey.Escape))
                {
                    Close();
                }
            }

            if (Screen.width != _screenW || Screen.height != _screenH)
            {
                _screenW = Screen.width;
                _screenH = Screen.height;
                _dirty = true;
            }
            RenderIfDirty();
        }

        private void Open()
        {
            _open = true;
            _input.Clear();
            _blockerAttempted = false;
            BindKernel();
            EnsureInputBlocker();
            EnsureUi();
            _ui?.SetVisible(true);
            _textOwned = DevHotkey.SetTextInputActive(this, true);
            if (!_textOwned) Append("输入被 HUD 过滤占用：先按 F7 退出过滤");
            _scrollToBottom = true;
            _dirty = true;
            RenderIfDirty();
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
            _ui?.SetVisible(false);
        }

        private void EnsureInputBlocker()
        {
            if (!_open || _kernel == null || _inputLease != null || _blockerAttempted) return;
            _blockerAttempted = true;
            try { _inputLease = _kernel.BlockInput(InputBlockerName, "开发控制台输入"); }
            catch (Exception ex) { AppendError("输入拦截注册失败：" + ex.Message); }
        }

        private void OnDestroy()
        {
            DevHotkey.SetTextInputActive(this, false);
            _textOwned = false;
            _inputLease?.Dispose();
            _inputLease = null;
            _ui?.Dispose();
            _ui = null;
        }

        private void EnsureUi()
        {
            if (_ui != null || _uiFailed) return;
            try
            {
                _ui = new DebugConsoleUiToolkitHost(transform);
                _ui.SetVisible(_open);
                _dirty = true;
            }
            catch (Exception ex)
            {
                _uiFailed = true;
                AppendError("UI Toolkit 渲染初始化失败：" + ex.Message);
                Log.Error("开发控制台 UI Toolkit 初始化失败：" + ex.Message, LogTag.UI);
            }
        }

        private void RenderIfDirty()
        {
            if (!_open || !_dirty || _ui == null) return;
            _dirty = false;
            string hint = _textOwned
                ? "F12/Esc 关闭 · Enter 执行 · help 查看命令"
                : "输入被 HUD 过滤占用：先按 F7 退出过滤";
            _ui.Render(_lines, _input.ToString(), hint, _textOwned, _scrollToBottom);
            _scrollToBottom = false;
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
                case "tune":
                    DispatchTune(args);
                    break;
                case "request":
                    if (!Require(args, 1, "request <enter|exit|teleport>")) break;
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
                    _dirty = true;
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
            Append("tune                       列出可调参数（或 tune list [scope]）");
            Append("tune <id>                  查看单项当前值/默认值/范围");
            Append("tune <id> <value>          设置数值或 on/off");
            Append("tune <id> reset            复位单项；tune resetall 复位全部");
            Append("request enter              进入本地测试模式（Room-Test，套 DevStartupConfig 快照）");
            Append("request exit               退出本地测试模式");
            Append("request teleport           传送到准心（本地测试模式）");
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
                case "enter": _kernel.RequestEnterTestMode(); Append("已请求进入本地测试模式"); break;
                case "exit": _kernel.RequestExitTestMode(); Append("已请求退出本地测试模式"); break;
                case "teleport": _kernel.RequestTeleport(); Append("已请求传送到准心"); break;
                default: AppendError("request 参数需为 enter/exit/teleport"); break;
            }
        }

        private void DispatchTune(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                AppendTunables(null);
                return;
            }

            string first = args[0];
            if (string.Equals(first, "list", StringComparison.OrdinalIgnoreCase))
            {
                AppendTunables(args.Length > 1 ? args[1] : null);
                return;
            }
            if (string.Equals(first, "resetall", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<DebugTunable> all = _kernel.Tunables;
                int ok = 0;
                for (int i = 0; i < all.Count; i++)
                    if (_kernel.ResetTunable(all[i].Id, out _)) ok++;
                Append($"调参已复位 {ok}/{all.Count} 项");
                return;
            }

            if (!TryFindTunable(first, out DebugTunable tunable))
            {
                AppendError("未找到调参项：" + first + "（tune 查看清单）");
                return;
            }
            if (args.Length == 1)
            {
                AppendTunableDetail(tunable);
                return;
            }
            if (string.Equals(args[1], "reset", StringComparison.OrdinalIgnoreCase))
            {
                if (_kernel.ResetTunable(tunable.Id, out string resetError))
                    Append($"{tunable.Id} 已复位 = {DebugTunableText.Format(tunable, tunable.Get())}");
                else
                    AppendError(resetError);
                return;
            }
            if (!DebugTunableText.TryParse(tunable, args[1], out float value, out string parseError))
            {
                AppendError(parseError);
                return;
            }
            if (_kernel.TrySetTunable(tunable.Id, value, out string setError))
                Append($"{tunable.Id} = {DebugTunableText.Format(tunable, tunable.Get())}");
            else
                AppendError(setError);
        }

        private void AppendTunables(string scopeText)
        {
            DebugTunableScope? scope = ParseScope(scopeText);
            if (scopeText != null && scope == null)
            {
                AppendError("scope 需为 sandbox/presentation/product");
                return;
            }

            IReadOnlyList<DebugTunable> all = _kernel.Tunables;
            int shown = 0;
            string group = null;
            for (int i = 0; i < all.Count; i++)
            {
                DebugTunable tunable = all[i];
                if (scope.HasValue && tunable.Scope != scope.Value) continue;
                if (!string.Equals(group, tunable.Group, StringComparison.Ordinal))
                {
                    group = tunable.Group;
                    Append("── " + group + " ──");
                }
                Append("  " + tunable.Id + " = " + DebugTunableText.Format(tunable, tunable.Get())
                    + "  [" + ScopeName(tunable.Scope) + "] " + tunable.Label);
                shown++;
            }
            if (shown == 0) Append(scope.HasValue ? "（该 scope 下无参数）" : "（无调参项）");
        }

        private void AppendTunableDetail(DebugTunable tunable)
        {
            Append("── " + tunable.Id + " ──");
            Append("  分组: " + tunable.Group + " / " + tunable.Label);
            Append("  当前: " + DebugTunableText.Format(tunable, tunable.Get())
                + "  默认: " + DebugTunableText.Format(tunable, tunable.Default));
            Append("  范围: " + DebugTunableText.Format(tunable, tunable.Min)
                + " .. " + DebugTunableText.Format(tunable, tunable.Max));
            Append("  scope: " + ScopeName(tunable.Scope)
                + (tunable.RuntimeVisible ? " / 运行期可见" : " / 仅编辑器"));
        }

        private bool TryFindTunable(string id, out DebugTunable tunable)
        {
            IReadOnlyList<DebugTunable> all = _kernel.Tunables;
            for (int i = 0; i < all.Count; i++)
                if (string.Equals(all[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    tunable = all[i];
                    return true;
                }
            tunable = null;
            return false;
        }

        private static DebugTunableScope? ParseScope(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            switch (text.Trim().ToLowerInvariant())
            {
                case "sandbox": return DebugTunableScope.Sandbox;
                case "presentation": return DebugTunableScope.Presentation;
                case "product": return DebugTunableScope.Product;
                default: return null;
            }
        }

        private static string ScopeName(DebugTunableScope scope)
        {
            switch (scope)
            {
                case DebugTunableScope.Presentation: return "表现";
                case DebugTunableScope.Product: return "产品";
                default: return "沙箱";
            }
        }

        private void Append(string line)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
            _scrollToBottom = true;
            _dirty = true;
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

    }
}
#endif
