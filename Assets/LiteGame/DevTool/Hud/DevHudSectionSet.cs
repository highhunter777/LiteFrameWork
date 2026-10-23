#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
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

namespace LiteGame
{
    /// <summary>段构建所需的开关/过滤参数（DevHUD 每次采样前刷新，避免段件反向读宿主 public 字段）。</summary>
    internal sealed class DevHudSectionOptions
    {
        public bool ShowErrors;
        public bool ShowNet;
        public bool ShowPerf;
        public bool ShowStats;
        public bool ShowLogRecent;
        public int LogRecentLines;
        public int LogFilter;
        public bool OnlyCurrentMatch;
        public Func<string, bool> ShowStat;
    }

    /// <summary>
    /// 七段构建器：只把 Stats/性能/联机/日志/Diag/sink 读成“行 + 色调 + 标签”，
    /// 不碰 UI Toolkit、输入、持久化或生命周期；宿主只取 <see cref="Data"/> 交给渲染后端。
    /// </summary>
    internal sealed class DevHudSectionSet
    {
        private readonly DebugKernel _kernel;
        private readonly DevHudPerfSampler _perf;
        private readonly DevHudFilter _filter;
        private readonly DevHudSectionOptions _options;
        private readonly DevHudNetHistory _net = new DevHudNetHistory();

        private readonly List<IDevHudSection> _sections = new List<IDevHudSection>(8);
        private readonly List<DevHudSectionData> _data = new List<DevHudSectionData>(8);
        private readonly List<LogEntry> _filteredLogs = new List<LogEntry>(32);
        private readonly List<HudLine> _statsCache = new List<HudLine>(128);
        private readonly Dictionary<string, string> _buffer = new Dictionary<string, string>(32);
        private readonly HudSink _sink = new HudSink();
        private readonly StringBuilder _sb = new StringBuilder(1024);

        private string _netMatchId = "";
        private float _statsCacheAt;

        public IReadOnlyList<DevHudSectionData> Data => _data;

        public DevHudSectionSet(DebugKernel kernel, DevHudPerfSampler perf,
            DevHudFilter filter, DevHudSectionOptions options)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _perf = perf;
            _filter = filter;
            _options = options;

            AddSection("错误", 10, AppendErrors);
            AddSection("联机诊断", 20, AppendNet);
            AddSection("性能", 30, AppendPerf);
            AddSection("模块 Stats", 40, AppendStats);
            AddSection("Log Recent", 50, AppendLogRecent);
            AddSection("DiagTrace", 60, AppendDiagTrace);
            AddSection("日志 sink", 70, AppendLogSink);
        }

        public void InvalidateStats() => _statsCacheAt = 0f;

        public void Build()
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                IDevHudSection section = _sections[i];
                DevHudSectionData data = _data[i];
                data.Lines.Clear();
                data.Visible = section.Visible;
                if (data.Visible)
                {
                    _sink.Bind(data.Lines);
                    try { section.Append(_sink, default); }
                    catch (Exception ex) { data.Lines.Add(new HudLine("[!!] 段异常: " + ex.GetType().Name, HudTone.Error)); }
                    if (_filter.Active) _filter.Apply(data.Lines);
                }
                if (data.Lines.Count == 0) data.Visible = false;
            }
        }

        public void SampleNet()
        {
            if (!_kernel.TryTakeBattleSnapshot(out BattleDiagnostics.Snapshot snapshot))
            {
                if (_netMatchId.Length > 0) { _netMatchId = ""; _net.Reset(); }
                return;
            }

            if (!string.Equals(_netMatchId, snapshot.MatchId, StringComparison.Ordinal))
            {
                _netMatchId = snapshot.MatchId;
                _net.Reset();
            }
            if (snapshot.Quality.HasSample)
                _net.Sample(snapshot.Quality.RttMs, snapshot.Quality.LossPermille);
        }

        private void AddSection(string title, int order, Action<IHudTextSink> append)
        {
            _sections.Add(new DelegateSection(title, order, append));
            _data.Add(new DevHudSectionData(title));
        }

        private void AppendErrors(IHudTextSink sink)
        {
            if (!_options.ShowErrors || _kernel.ErrorCount <= 0) return;
            sink.Line("⚠ 错误计数: " + _kernel.ErrorCount, HudTone.Error, "Error");
        }

        private void AppendNet(IHudTextSink sink)
        {
            if (!_options.ShowNet) return;
            _sb.Clear();
            if (!_kernel.AppendExtendedBattle(_sb)) return;
            EmitLines(_sb.ToString(), sink, HudTone.Normal, "Net");

            if (_net.TryFormat(40, out string rttLine, out string lossLine))
            {
                sink.Line(rttLine, HudTone.Muted, "Net");
                sink.Line(lossLine, HudTone.Muted, "Net");
            }
        }

        private void AppendPerf(IHudTextSink sink)
        {
            if (!_options.ShowPerf) return;
            _perf.UpdateRecorders();
            if (!_perf.TryGet(out PerfSnapshot p)) return;

            HudTone frameTone = p.P99Ms > 33f || p.Fps < 15 ? HudTone.Error
                : p.P95Ms > 16.6f || p.Fps < 30 ? HudTone.Warn : HudTone.Normal;
            sink.Line("FPS: " + p.Fps + "  p50: " + p.P50Ms.ToString("0.0", CultureInfo.InvariantCulture)
                + "  p95: " + p.P95Ms.ToString("0.0", CultureInfo.InvariantCulture)
                + "  p99: " + p.P99Ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms", frameTone, "Perf");

            string gc = _perf.GcAvailable ? FormatBytes(_perf.GcBytes) : "n/a";
            HudTone gcTone = !_perf.GcAvailable ? HudTone.Muted
                : _perf.GcBytes > 512 * 1024 ? HudTone.Error
                : _perf.GcBytes > 64 * 1024 ? HudTone.Warn : HudTone.Normal;
            string draw = _perf.DrawCallsAvailable ? _perf.DrawCalls.ToString(CultureInfo.InvariantCulture) : "n/a";
            string batches = _perf.BatchesAvailable ? _perf.Batches.ToString(CultureInfo.InvariantCulture) : "n/a";
            sink.Line("最差: " + Mathf.RoundToInt(p.WorstMs) + " ms  长帧: " + p.LongFrames
                + "  GC: " + gc + "/帧  Draw: " + draw + "  Batches: " + batches
                + "  HUD: " + _perf.HudBuildMs.ToString("0.00", CultureInfo.InvariantCulture) + " ms", gcTone, "Perf");
        }

        private void AppendStats(IHudTextSink sink)
        {
            if (!_options.ShowStats) return;
            if (Time.unscaledTime >= _statsCacheAt || _statsCache.Count == 0)
            {
                _statsCacheAt = Time.unscaledTime + 0.5f;
                RebuildStatsCache();
            }
            for (int i = 0; i < _statsCache.Count; i++)
            {
                HudLine line = _statsCache[i];
                sink.Line(line.Text, line.Tone, line.Tag);
            }
        }

        private void RebuildStatsCache()
        {
            _statsCache.Clear();
            IReadOnlyList<IModuleStats> stats = _kernel.Stats;
            for (int i = 0; i < stats.Count; i++)
            {
                IModuleStats stat = stats[i];
                if (_options.ShowStat != null && !_options.ShowStat(stat.StatsName)) continue;
                _sb.Clear();
                _sb.Append("── ").Append(stat.StatsName).Append(" ──");
                _statsCache.Add(new HudLine(_sb.ToString(), HudTone.Muted, stat.StatsName));
                try { stat.Snapshot(_buffer); }
                catch (Exception ex)
                {
                    _sb.Clear();
                    _sb.Append("[!!] Snapshot 异常: ").Append(ex.GetType().Name);
                    _statsCache.Add(new HudLine(_sb.ToString(), HudTone.Error, stat.StatsName));
                    continue;
                }
                foreach (KeyValuePair<string, string> kv in _buffer)
                {
                    _sb.Clear();
                    _sb.Append("  ").Append(kv.Key).Append(": ").Append(kv.Value);
                    _statsCache.Add(new HudLine(_sb.ToString(), StatsTone(kv.Key, kv.Value), stat.StatsName));
                }
            }
        }

        private void AppendLogRecent(IHudTextSink sink)
        {
            if (!_options.ShowLogRecent) return;
            IReadOnlyList<LogEntry> recent = _kernel.RecentLogs;
            if (recent.Count == 0) return;

            _filteredLogs.Clear();
            for (int i = 0; i < recent.Count; i++)
                if (PassLogFilter(recent[i])) _filteredLogs.Add(recent[i]);
            if (_filteredLogs.Count == 0) return;

            int take = Mathf.Clamp(_options.LogRecentLines, 1, 32);
            int from = _filteredLogs.Count > take ? _filteredLogs.Count - take : 0;
            for (int i = from; i < _filteredLogs.Count; i++) AppendLogLine(_filteredLogs[i], sink);
        }

        private bool PassLogFilter(in LogEntry entry)
        {
            if (_options.LogFilter == 1 && entry.Level < LogLevel.Warning) return false;
            if (_options.LogFilter == 2 && entry.Level < LogLevel.Error) return false;
            if (_options.OnlyCurrentMatch && Log.CurrentMatchId.Length > 0 &&
                !string.Equals(entry.MatchId, Log.CurrentMatchId)) return false;
            return true;
        }

        private void AppendLogLine(in LogEntry entry, IHudTextSink sink)
        {
            _sb.Clear();
            _sb.Append(entry.Level == LogLevel.Error ? '✗' : entry.Level == LogLevel.Warning ? '⚠' : '·');
            if (entry.Tag != LogTag.None) _sb.Append('[').Append(Log.TagName(entry.Tag)).Append(']');
            AppendLogContext(entry);
            _sb.Append(' ').Append(entry.Message);

            HudTone tone = entry.Level >= LogLevel.Error ? HudTone.Error
                : entry.Level == LogLevel.Warning ? HudTone.Warn : HudTone.Normal;
            string tag = entry.Tag != LogTag.None ? Log.TagName(entry.Tag) : "Log";
            sink.Line(_sb.ToString(), tone, tag);
        }

        /// <summary>关联上下文：`[m:对局尾6位 f:帧 p:席位]`；三项都无（未开局日志）时整段省略。</summary>
        private void AppendLogContext(in LogEntry entry)
        {
            if (entry.MatchId.Length == 0 && entry.Frame == 0 && entry.PlayerId < 0) return;
            _sb.Append('[');
            if (entry.MatchId.Length > 0)
            {
                _sb.Append("m:");
                int start = entry.MatchId.Length > 6 ? entry.MatchId.Length - 6 : 0;
                _sb.Append(entry.MatchId, start, entry.MatchId.Length - start).Append(' ');
            }
            _sb.Append('f').Append(entry.Frame);
            if (entry.PlayerId >= 0) _sb.Append(" p").Append(entry.PlayerId);
            _sb.Append(']');
        }

        private void AppendDiagTrace(IHudTextSink sink)
        {
            IReadOnlyList<DiagEvent> recent = _kernel.RecentDiag;
            if (recent.Count == 0) return;

            sink.Line("total=" + _kernel.DiagTotal, HudTone.Muted, "Diag");
            int take = Mathf.Min(8, recent.Count);
            for (int i = recent.Count - take; i < recent.Count; i++)
            {
                DiagEvent e = recent[i];
                HudTone tone = ContainsFail(e.Code) ? HudTone.Error : HudTone.Normal;
                sink.Line(DiagTrace.Line(e), tone, "Diag");
            }
        }

        private void AppendLogSink(IHudTextSink sink)
        {
            StructuredLogSink state = _kernel.LogSink;
            if (state == null)
            {
                sink.Line("未安装（release/未初始化）", HudTone.Muted, "LogSink");
                return;
            }

            sink.Line("written=" + state.Written + " dropped=" + state.Dropped,
                state.Dropped > 0 ? HudTone.Warn : HudTone.Normal, "LogSink");
            if (!string.IsNullOrEmpty(state.CurrentPath))
                sink.Line(Path.GetFileName(state.CurrentPath), HudTone.Muted, "LogSink");
            if (!string.IsNullOrEmpty(state.LastError))
                sink.Line("lastError: " + state.LastError, HudTone.Error, "LogSink");
        }

        private static void EmitLines(string text, IHudTextSink sink, HudTone tone, string tag)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                if (i > start) sink.Line(text.Substring(start, i - start), tone, tag);
                start = i + 1;
            }
            if (start < text.Length) sink.Line(text.Substring(start), tone, tag);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + "B";
            if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.0", CultureInfo.InvariantCulture) + "KB";
            return (bytes / (1024f * 1024f)).ToString("0.0", CultureInfo.InvariantCulture) + "MB";
        }

        private static HudTone StatsTone(string key, string value)
        {
            if (string.IsNullOrEmpty(value) || value == "0" || value == "0.0") return HudTone.Normal;
            if (ContainsIgnoreCase(key, "Error") || ContainsIgnoreCase(key, "Failed")) return HudTone.Error;
            if (ContainsIgnoreCase(key, "Dropped") || ContainsIgnoreCase(key, "Warning") ||
                ContainsIgnoreCase(key, "Pending") || ContainsIgnoreCase(key, "Overflow") ||
                ContainsIgnoreCase(key, "Rejected")) return HudTone.Warn;
            return HudTone.Normal;
        }

        private static bool ContainsIgnoreCase(string text, string value)
            => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool ContainsFail(string code)
            => !string.IsNullOrEmpty(code) && code.IndexOf("Fail", StringComparison.OrdinalIgnoreCase) >= 0;

        private sealed class HudSink : IHudTextSink
        {
            private List<HudLine> _lines;

            public void Bind(List<HudLine> lines) => _lines = lines;

            public void Line(string text, HudTone tone = HudTone.Normal, string tag = null)
            {
                if (!string.IsNullOrEmpty(text)) _lines.Add(new HudLine(text, tone, tag));
            }
        }

        private sealed class DelegateSection : IDevHudSection
        {
            private readonly Action<IHudTextSink> _append;

            public string Title { get; }
            public int Order { get; }
            public bool Visible => true;

            public DelegateSection(string title, int order, Action<IHudTextSink> append)
            {
                Title = title;
                Order = order;
                _append = append;
            }

            public void Append(IHudTextSink sink, in HudContext ctx) => _append(sink);
        }
    }
}
#endif
