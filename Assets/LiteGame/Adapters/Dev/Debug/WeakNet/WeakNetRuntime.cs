#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using LiteNet.Diagnostics;
using LiteNet.Transport;

namespace LiteGame
{
    /// <summary>
    /// 弱网注入运行实例：持有当前 profile、包装真传输、提供只读快照与数值调参描述。
    /// 不新增 public static 可变状态；装配点只在 ContainerModule 的开发宏内。
    /// </summary>
    public sealed class WeakNetRuntime : IDebugTunableProvider
    {
        private readonly Func<long> _nowMs;
        private WeakNetProfile _profile = WeakNetProfile.None;
        private string _preset = "none";
        private WeakNetClientTransport _active;

        public WeakNetRuntime(Func<long> nowMs = null)
        {
            _nowMs = nowMs ?? DefaultClock;
        }

        public WeakNetProfile Profile => _profile;

        public WeakNetClientTransport Active => _active;

        public void Detach() => _active = null;

        public IClientTransport Wrap(IClientTransport inner)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            _active = new WeakNetClientTransport(inner, () => _profile, _nowMs);
            return _active;
        }

        public bool TrySetPreset(string name, out string error)
        {
            if (!WeakNetPresets.TryGet(name, out WeakNetProfile profile))
            {
                error = $"未知弱网预设：{name}（可选 none/lan/mobile/lossy）";
                return false;
            }
            _profile = profile;
            _preset = name;
            error = null;
            return true;
        }

        public bool TrySetNumeric(string id, float value, out string error)
        {
            int rounded = (int)Math.Round(value);
            WeakNetProfile p = _profile;
            switch (id)
            {
                case "latency": rounded = Math.Max(0, rounded); break;
                case "jitter": rounded = Math.Max(0, rounded); break;
                case "loss": rounded = Clamp(rounded, 0, 1000); break;
                case "dup": rounded = Clamp(rounded, 0, 1000); break;
                case "reorder": rounded = Clamp(rounded, 0, 1000); break;
                default:
                    error = $"未知弱网参数：{id}（latency/jitter/loss/dup/reorder）";
                    return false;
            }
            bool enabled = p.Enabled || rounded > 0;
            _profile = new WeakNetProfile(enabled,
                id == "latency" ? rounded : p.LatencyMs,
                id == "jitter" ? rounded : p.JitterMs,
                id == "loss" ? rounded : p.LossPermille,
                id == "dup" ? rounded : p.DuplicatePermille,
                id == "reorder" ? rounded : p.ReorderPermille,
                p.Seed);
            _preset = WeakNetPresets.NameOf(_profile);
            error = null;
            return true;
        }

        public WeakNetSnapshot TakeSnapshot()
        {
            WeakNetStats stats = _active != null ? _active.Stats : WeakNetStats.Empty;
            return new WeakNetSnapshot(_profile.Enabled && _active != null, _preset, _profile, stats);
        }

        public void CollectDebugTunables(ICollection<DebugTunable> into)
        {
            Add(into, "net.latency", "网络延迟", "ms", 0, 1000, () => _profile.LatencyMs, v => TrySetNumeric("latency", v, out _));
            Add(into, "net.jitter", "网络抖动", "ms", 0, 500, () => _profile.JitterMs, v => TrySetNumeric("jitter", v, out _));
            Add(into, "net.loss", "丢包率", "‰", 0, 1000, () => _profile.LossPermille, v => TrySetNumeric("loss", v, out _));
            Add(into, "net.dup", "重复率", "‰", 0, 1000, () => _profile.DuplicatePermille, v => TrySetNumeric("dup", v, out _));
            Add(into, "net.reorder", "乱序率", "‰", 0, 1000, () => _profile.ReorderPermille, v => TrySetNumeric("reorder", v, out _));
        }

        private static void Add(ICollection<DebugTunable> into, string id, string label, string unit,
            float min, float max, Func<float> get, Action<float> set)
        {
            into.Add(new DebugTunable
            {
                Id = id,
                Group = "弱网",
                Label = $"{label}（{unit}）",
                Tags = "net weak",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Sandbox,
                Default = get(),
                Min = min,
                Max = max,
                Decimals = 0,
                RuntimeVisible = true,
                Get = get,
                Set = set,
            });
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static long DefaultClock()
        {
            return (long)(System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }
    }
}
#endif
