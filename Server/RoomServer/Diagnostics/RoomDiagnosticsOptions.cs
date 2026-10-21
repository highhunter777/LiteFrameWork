using System;
using System.IO;
using System.Text.Json;
using RoomServer.Runtime.Diagnostics;

namespace RoomServer.Diagnostics
{
    /// <summary>
    /// 服务端房间诊断录制配置（可选分区 <c>diagnostics</c>）。
    /// 缺省关闭 = 行为零变化；启用时目录必须是绝对路径（避免工作目录漂移）。
    /// </summary>
    public sealed class RoomDiagnosticsOptions
    {
        public const int DefaultMaxFiles = RoomRunStore.DefaultMaxFiles;
        public const long DefaultMaxTotalMb = 512;
        public const int DefaultLogTailLines = 200;

        /// <summary>世界环缺省容量（帧）：约 2s @60Hz——覆盖 mismatch 上报延迟 + 余量；
        /// 内存代价 = 容量 × 单世界大小（数十 KB 级），信封体积随之增长，按需调小/调大。</summary>
        public const int DefaultWorldRingFrames = 120;

        public bool Enabled { get; }
        public string Directory { get; }
        public int MaxFiles { get; }
        public long MaxTotalBytes { get; }
        public int MaxCommands { get; }
        public int MaxSteps { get; }
        public int LogTailLines { get; }
        public int WorldRingFrames { get; }

        public static readonly RoomDiagnosticsOptions Disabled = new RoomDiagnosticsOptions(
            enabled: false, directory: string.Empty, maxFiles: DefaultMaxFiles,
            maxTotalMb: DefaultMaxTotalMb, maxCommands: RoomRunRecorder.DefaultMaxCommands,
            maxSteps: RoomRunRecorder.DefaultMaxSteps, logTailLines: DefaultLogTailLines,
            worldRingFrames: DefaultWorldRingFrames);

        public RoomDiagnosticsOptions(bool enabled, string directory, int maxFiles, long maxTotalMb,
            int maxCommands, int maxSteps, int logTailLines, int worldRingFrames = DefaultWorldRingFrames)
        {
            if (maxFiles <= 0 || maxFiles > 100_000)
                throw new ArgumentOutOfRangeException(nameof(maxFiles), maxFiles, "maxFiles 越界（1..100000）");
            if (maxTotalMb <= 0 || maxTotalMb > 1_000_000)
                throw new ArgumentOutOfRangeException(nameof(maxTotalMb), maxTotalMb, "maxTotalMb 越界（1..1000000）");
            if (maxCommands <= 0 || maxCommands > 10_000_000)
                throw new ArgumentOutOfRangeException(nameof(maxCommands), maxCommands, "maxCommands 越界（1..10000000）");
            if (maxSteps <= 0 || maxSteps > 10_000_000)
                throw new ArgumentOutOfRangeException(nameof(maxSteps), maxSteps, "maxSteps 越界（1..10000000）");
            if (logTailLines <= 0 || logTailLines > 10_000)
                throw new ArgumentOutOfRangeException(nameof(logTailLines), logTailLines, "logTailLines 越界（1..10000）");
            if (worldRingFrames < 0 || worldRingFrames > 10_000)
                throw new ArgumentOutOfRangeException(nameof(worldRingFrames), worldRingFrames, "world_ring_frames 越界（0..10000）");
            if (enabled)
            {
                if (string.IsNullOrWhiteSpace(directory))
                    throw new ArgumentException("diagnostics.directory 启用时必填", nameof(directory));
                if (!Path.IsPathRooted(directory))
                    throw new ArgumentException("diagnostics.directory 必须是绝对路径（避免工作目录漂移）", nameof(directory));
            }

            Enabled = enabled;
            Directory = directory ?? string.Empty;
            MaxFiles = maxFiles;
            MaxTotalBytes = maxTotalMb * 1024L * 1024L;
            MaxCommands = maxCommands;
            MaxSteps = maxSteps;
            LogTailLines = logTailLines;
            WorldRingFrames = worldRingFrames;
        }

        public static RoomDiagnosticsOptions Parse(JsonElement root, string sourcePath)
        {
            if (!root.TryGetProperty("diagnostics", out JsonElement el))
                return Disabled;
            if (el.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"diagnostics 分区必须是对象：{sourcePath}");

            bool enabled = true;
            if (el.TryGetProperty("enabled", out JsonElement enabledEl))
            {
                if (enabledEl.ValueKind != JsonValueKind.True && enabledEl.ValueKind != JsonValueKind.False)
                    throw new InvalidDataException($"diagnostics.enabled 必须是布尔值：{sourcePath}");
                enabled = enabledEl.GetBoolean();
            }

            string directory = OptionalString(el, "directory");
            if (!enabled) return Disabled;

            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidDataException($"diagnostics.enabled=true 时 diagnostics.directory 必填：{sourcePath}");
            if (!Path.IsPathRooted(directory))
                throw new InvalidDataException($"diagnostics.directory 必须是绝对路径：{directory}：{sourcePath}");

            int maxFiles = OptionalInt(el, "max_files", DefaultMaxFiles);
            long maxTotalMb = OptionalLong(el, "max_total_mb", DefaultMaxTotalMb);
            int maxCommands = OptionalInt(el, "max_commands", RoomRunRecorder.DefaultMaxCommands);
            int maxSteps = OptionalInt(el, "max_steps", RoomRunRecorder.DefaultMaxSteps);
            int logTailLines = OptionalInt(el, "log_tail_lines", DefaultLogTailLines);
            int worldRingFrames = OptionalInt(el, "world_ring_frames", DefaultWorldRingFrames);

            try
            {
                return new RoomDiagnosticsOptions(true, directory, maxFiles, maxTotalMb,
                    maxCommands, maxSteps, logTailLines, worldRingFrames);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException($"diagnostics 配置非法：{ex.Message}：{sourcePath}", ex);
            }
        }

        private static string OptionalString(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement e)) return null;
            if (e.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"diagnostics.{name} 必须是字符串");
            return e.GetString();
        }

        private static int OptionalInt(JsonElement parent, string name, int fallback)
        {
            if (!parent.TryGetProperty(name, out JsonElement e)) return fallback;
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int v))
                throw new InvalidDataException($"diagnostics.{name} 必须是整数");
            return v;
        }

        private static long OptionalLong(JsonElement parent, string name, long fallback)
        {
            if (!parent.TryGetProperty(name, out JsonElement e)) return fallback;
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out long v))
                throw new InvalidDataException($"diagnostics.{name} 必须是整数");
            return v;
        }
    }
}
