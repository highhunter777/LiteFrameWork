#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LiteClient;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteNet.Diagnostics;

namespace LiteGame
{
    /// <summary>
    /// 调试内核（《GM 与调试工具分层专项设计》§7）：读模型、动作模型与发现协议的唯一门面。
    /// 投影（编辑器 Hub 页 / DevHUD / 运行期控制台）只做渲染与输入，不另拼读数、不另写动作。
    /// 实例在装配期由容器注入；不引用任何投影类型，不新增静态可变状态。
    /// </summary>
    public sealed class DebugKernel
    {
        private static readonly string[] Sections =
        {
            "commands", "options", "stats", "tunables", "log", "diag", "battle", "audit",
        };

        private readonly CommandCenter _commands;
        private readonly IInputService _input;
        private readonly DebugStatsDiscovery _stats = new DebugStatsDiscovery();
        private readonly DebugTunableDiscovery _tunables;
        private readonly Dictionary<Type, MethodInfo> _sendMethods = new Dictionary<Type, MethodInfo>(16);
        private bool _statsInitialized;
        private bool _tunablesInitialized;

        public DebugKernel(CommandCenter commands, IInputService input,
            IEnumerable<IDebugTunableProvider> tunableProviders = null)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _input = input;
            _tunables = new DebugTunableDiscovery(tunableProviders);
        }

        public IReadOnlyList<IModuleStats> Stats
        {
            get
            {
                if (!_statsInitialized) RebuildStats();
                return _stats.Stats;
            }
        }

        public IReadOnlyList<CommandInfo> Commands
        {
            get
            {
                var list = new List<CommandInfo>(_commands.GetRegisteredInfos());
                list.Sort((a, b) => a.RegisteredAt.CompareTo(b.RegisteredAt));
                return list;
            }
        }

        public IReadOnlyList<DebugTunable> Tunables
        {
            get
            {
                if (!_tunablesInitialized) RebuildTunables();
                return _tunables.Tunables;
            }
        }

        /// <summary>运行期快捷调参白名单：Sandbox/Presentation 且描述符显式标 RuntimeVisible；Product 不进运行期。</summary>
        public IReadOnlyList<DebugTunable> RuntimeTunables
        {
            get
            {
                var list = new List<DebugTunable>(8);
                IReadOnlyList<DebugTunable> all = Tunables;
                for (int i = 0; i < all.Count; i++)
                {
                    DebugTunable tunable = all[i];
                    if (!tunable.RuntimeVisible) continue;
                    if (tunable.Scope == DebugTunableScope.Product) continue;
                    list.Add(tunable);
                }
                return list;
            }
        }

        public IReadOnlyList<CommandAuditEntry> Audit => _commands.GetAuditLog();
        public IReadOnlyList<TestModeOptions.Info> Options => TestModeOptions.All;
        public IReadOnlyList<LogEntry> RecentLogs => Log.Recent;
        public int ErrorCount => Log.ErrorCount;
        public IReadOnlyList<DiagEvent> RecentDiag => DiagTrace.Recent;
        public long DiagTotal => DiagTrace.Total;
        public StructuredLogSink LogSink => StructuredLogInstaller.Current;
        public bool HasBattle => BattleDiagnostics.Current != null;

        public static IReadOnlyList<string> ReadSections => Sections;

        public void RebuildStats()
        {
            _stats.Rebuild();
            _statsInitialized = true;
        }

        public void PruneStats()
        {
            _stats.Prune();
            _tunables.Prune();
        }

        public void RebuildTunables()
        {
            _tunables.Rebuild();
            _tunablesInitialized = true;
        }

        public void PruneTunables() => _tunables.Prune();

        public bool TrySetTunable(string id, float value, out string error)
            => _tunables.TrySet(id, value, out error);

        public bool ResetTunable(string id, out string error)
            => _tunables.Reset(id, out error);

        public bool TrySnapshotStats(string statsName, Dictionary<string, string> into, out string error)
        {
            if (string.IsNullOrWhiteSpace(statsName))
            {
                error = "统计模块名不能为空";
                return false;
            }
            if (into == null)
            {
                error = "输出字典不能为空";
                return false;
            }

            IReadOnlyList<IModuleStats> stats = Stats;
            for (int i = 0; i < stats.Count; i++)
            {
                if (!string.Equals(stats[i].StatsName, statsName, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    stats[i].Snapshot(into);
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"{stats[i].StatsName} 读数异常：{ex.Message}";
                    return false;
                }
            }

            error = $"未找到统计模块：{statsName}";
            return false;
        }

        public bool TryTakeBattleSnapshot(out BattleDiagnostics.Snapshot snapshot)
        {
            BattleContext context = BattleDiagnostics.Current;
            if (context == null)
            {
                snapshot = default;
                return false;
            }
            snapshot = BattleDiagnostics.Take(context);
            return true;
        }

        public bool AppendExtendedBattle(StringBuilder into) => BattleDiagnostics.AppendExtendedCurrent(into);

        public string[] DescribeBattle()
            => BattleDiagnostics.DescribeCurrent() ?? Array.Empty<string>();

        public bool TryExecute(Type commandType, out CommandResult result, out string error)
        {
            result = default;
            if (commandType == null)
            {
                error = "命令类型不能为空";
                return false;
            }
            if (!typeof(ICommand).IsAssignableFrom(commandType))
            {
                error = $"{commandType.Name} 不是命令类型";
                return false;
            }
            if (commandType.GetConstructor(Type.EmptyTypes) == null)
            {
                error = $"{commandType.Name} 无无参构造（需专面板）";
                return false;
            }

            try
            {
                object command = Activator.CreateInstance(commandType);
                MethodInfo send = GetSendMethod(commandType);
                result = (CommandResult)send.Invoke(_commands, new[] { command });
                error = null;
                return true;
            }
            catch (TargetInvocationException ex)
            {
                error = $"{commandType.Name} 执行异常：{ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"{commandType.Name} 执行异常：{ex.Message}";
                return false;
            }
        }

        public bool TryExecute(string commandName, out CommandResult result, out string error)
        {
            result = default;
            string name = commandName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                error = "命令名不能为空";
                return false;
            }

            Type type = ResolveCommandName(name, out error);
            return type != null && TryExecute(type, out result, out error);
        }

        public bool Undo() => _commands.Undo();
        public bool Redo() => _commands.Redo();

        public bool GetOption(TestModeOptions.Id id) => TestModeOptions.Get(id);

        public bool TrySetOption(string option, bool value, out string error)
        {
            string name = option?.Trim();
            if (string.IsNullOrEmpty(name)
                || !Enum.TryParse(name, true, out TestModeOptions.Id id)
                || !Enum.IsDefined(typeof(TestModeOptions.Id), id))
            {
                error = $"未找到选项：{option}";
                return false;
            }

            try
            {
                TestModeOptions.Set(id, value);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"{id} 写入异常：{ex.Message}";
                return false;
            }
        }

        public void RequestEnterTestMode() => TestModeRuntime.RequestEnter();
        public void RequestExitTestMode() => TestModeRuntime.RequestExit();
        public void RequestTeleport() => TestModeRuntime.RequestTeleport();

        public IDisposable BlockInput(string name, string reason)
        {
            if (_input == null) return null;
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("拦截源名不能为空", nameof(name));

            _input.RegisterBlocker(new IntentGate.BlockerKey(name, reason ?? name), () => true);
            return new InputBlockerLease(_input, name);
        }

        /// <summary>按类型名解析：先精确匹配，再唯一后缀匹配；歧义/未命中给可用候选。</summary>
        private Type ResolveCommandName(string name, out string error)
        {
            IReadOnlyList<CommandInfo> commands = Commands;
            Type exact = null;
            var suffix = new List<Type>(2);
            for (int i = 0; i < commands.Count; i++)
            {
                Type type = commands[i].CommandType;
                if (string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    exact = type;
                    break;
                }
                if (type.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase)) suffix.Add(type);
            }

            if (exact != null)
            {
                error = null;
                return exact;
            }
            if (suffix.Count == 1)
            {
                error = null;
                return suffix[0];
            }
            if (suffix.Count > 1)
            {
                var names = new StringBuilder();
                for (int i = 0; i < suffix.Count; i++)
                {
                    if (i > 0) names.Append(", ");
                    names.Append(suffix[i].Name);
                }
                error = $"命令名不唯一：{names}";
                return null;
            }

            error = $"未找到命令：{name}";
            return null;
        }

        private MethodInfo GetSendMethod(Type commandType)
        {
            if (_sendMethods.TryGetValue(commandType, out MethodInfo send)) return send;
            MethodInfo definition = typeof(CommandCenter).GetMethod(
                nameof(CommandCenter.Send), BindingFlags.Public | BindingFlags.Instance);
            send = definition.MakeGenericMethod(commandType);
            _sendMethods[commandType] = send;
            return send;
        }

        private sealed class InputBlockerLease : IDisposable
        {
            private readonly IInputService _input;
            private readonly string _name;
            private bool _disposed;

            public InputBlockerLease(IInputService input, string name)
            {
                _input = input;
                _name = name;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _input.UnregisterBlocker(_name); }
                catch (Exception ex) { Log.Warning($"调试输入拦截释放失败：{ex.Message}", LogTag.Command); }
            }
        }
    }
}
#endif
