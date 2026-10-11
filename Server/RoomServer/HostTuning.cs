using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using RoomServer.Application;

namespace RoomServer
{
    /// <summary>
    /// 宿主运行期调参（P2 批二：审计点名的一批**魔法超时/容量进配置**）。
    ///
    /// **为什么要有它**：这些值此前是散在 <c>Program</c>/<c>ServerHost</c>/<c>SessionManager</c>/
    /// <c>ReconnectService</c>/<c>SettlementOutbox</c> 里的字面量常量——部署形态下"排空给多久""会话多久
    /// 算僵尸""重连窗口多长""收口压实多勤"全都是**改代码才能改**的值，与 §429/§520"参数可配 + 范围校验、
    /// 不把估算值写死为事实"直接冲突。归一处后：默认值 = 原常量（**行为零变化**），要调只动配置文件。
    ///
    /// **边界**：只收"部署/运维面可调"的量。**协议面**（如 <c>MaxInboundPacketBytes</c> 之外的信封/帧格式）
    /// 与**玩法数值**（走 .bytes 表）不在此——那两类改动不是调参，是改契约。
    /// </summary>
    public sealed class HostTuning
    {
        // ---- 默认值 = 迁移前的字面量常量（行为零变化）----

        /// <summary>排空宽限：收到停止信号后等对局自然收敛的时长（§12 第 3 步"在配置时限内完成对局"）。</summary>
        public const int DefaultDrainGraceMs = 5_000;

        /// <summary>排空看门狗宽限：排空循环在 <c>DrainGraceMs</c> 之上额外等待的时长。</summary>
        public const int DefaultDrainWatchdogExtraMs = 2_000;

        /// <summary>Ops 周期行间隔（毫秒）。</summary>
        public const int DefaultOpsIntervalMs = 5_000;

        /// <summary>入包长度上限（字节；protobuf 解析**之前**的硬边界，《服务端总设计》§5 P0-3）。</summary>
        public const int DefaultMaxInboundPacketBytes = 4096;

        /// <summary>会话周期清理节拍（tick；600 ≈ 10s @60Hz）。</summary>
        public const int DefaultSessionCleanupIntervalTicks = 600;

        /// <summary>未进房连接的静默上限（毫秒）：连上但从不 Join/发包的会话（探测/半开）到期清理。</summary>
        public const int DefaultSessionUnseatedIdleTtlMs = 60 * 1000;

        /// <summary>Lobby 注册/心跳单次请求超时（毫秒）。</summary>
        public const int DefaultLobbyRequestTimeoutMs = 5_000;

        /// <summary>重连票据有效期（毫秒）：5 分钟（《联机Demo设计》断线宽容窗口）。</summary>
        public const long DefaultReconnectTicketTtlMs = ReconnectService.TicketTtlMs;

        /// <summary>重连票据表容量上限（超限淘汰最老——票据本是短期凭证）。</summary>
        public const int DefaultReconnectCapacity = ReconnectService.DefaultCapacity;

        /// <summary>结算 Outbox 自动压实阈值（连续完成标记达该数即重写日志）。</summary>
        public const int DefaultOutboxCompactThresholdMarks = FileSettlementOutbox.DefaultCompactThresholdMarks;

        public readonly int DrainGraceMs;
        public readonly int DrainWatchdogExtraMs;
        public readonly int OpsIntervalMs;
        public readonly int MaxInboundPacketBytes;
        public readonly int SessionCleanupIntervalTicks;
        public readonly int SessionUnseatedIdleTtlMs;
        public readonly int LobbyRequestTimeoutMs;
        public readonly long ReconnectTicketTtlMs;
        public readonly int ReconnectCapacity;
        public readonly int OutboxCompactThresholdMarks;

        /// <summary>全缺省形态（配置文件无 <c>host_tuning</c> 分区时用；= 迁移前的字面量）。</summary>
        public static readonly HostTuning Default = new HostTuning(
            DefaultDrainGraceMs, DefaultDrainWatchdogExtraMs, DefaultOpsIntervalMs, DefaultMaxInboundPacketBytes,
            DefaultSessionCleanupIntervalTicks, DefaultSessionUnseatedIdleTtlMs, DefaultLobbyRequestTimeoutMs,
            DefaultReconnectTicketTtlMs, DefaultReconnectCapacity, DefaultOutboxCompactThresholdMarks);

        private HostTuning(int drainGraceMs, int drainWatchdogExtraMs, int opsIntervalMs,
            int maxInboundPacketBytes, int sessionCleanupIntervalTicks, int sessionUnseatedIdleTtlMs,
            int lobbyRequestTimeoutMs, long reconnectTicketTtlMs, int reconnectCapacity,
            int outboxCompactThresholdMarks)
        {
            DrainGraceMs = drainGraceMs;
            DrainWatchdogExtraMs = drainWatchdogExtraMs;
            OpsIntervalMs = opsIntervalMs;
            MaxInboundPacketBytes = maxInboundPacketBytes;
            SessionCleanupIntervalTicks = sessionCleanupIntervalTicks;
            SessionUnseatedIdleTtlMs = sessionUnseatedIdleTtlMs;
            LobbyRequestTimeoutMs = lobbyRequestTimeoutMs;
            ReconnectTicketTtlMs = reconnectTicketTtlMs;
            ReconnectCapacity = reconnectCapacity;
            OutboxCompactThresholdMarks = outboxCompactThresholdMarks;
        }

        /// <summary>
        /// 解析 <c>host_tuning</c> 分区（可选；逐字段范围校验，越界**装载期即拒**——
        /// 与 rooms/rate_limit 同款：半段配置与越界值不让它活到运行期）。
        /// </summary>
        public static HostTuning Parse(JsonElement el, string sourcePath)
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"host_tuning 分区必须是对象：{sourcePath}");

            return new HostTuning(
                Int(el, "drain_grace_ms", DefaultDrainGraceMs, 0, 600_000, sourcePath),
                Int(el, "drain_watchdog_extra_ms", DefaultDrainWatchdogExtraMs, 0, 600_000, sourcePath),
                Int(el, "ops_interval_ms", DefaultOpsIntervalMs, 1_000, 600_000, sourcePath),
                Int(el, "max_inbound_packet_bytes", DefaultMaxInboundPacketBytes, 64, 1_048_576, sourcePath),
                Int(el, "session_cleanup_interval_ticks", DefaultSessionCleanupIntervalTicks, 1, 1_000_000, sourcePath),
                Int(el, "session_unseated_idle_ttl_ms", DefaultSessionUnseatedIdleTtlMs, 1_000, 86_400_000, sourcePath),
                Int(el, "lobby_request_timeout_ms", DefaultLobbyRequestTimeoutMs, 100, 600_000, sourcePath),
                Long(el, "reconnect_ticket_ttl_ms", DefaultReconnectTicketTtlMs, 1_000, 86_400_000, sourcePath),
                Int(el, "reconnect_capacity", DefaultReconnectCapacity, 1, 1_000_000, sourcePath),
                Int(el, "outbox_compact_threshold_marks", DefaultOutboxCompactThresholdMarks, 1, 1_000_000, sourcePath));
        }

        private static int Int(JsonElement el, string name, int fallback, int min, int max, string where)
        {
            if (!el.TryGetProperty(name, out JsonElement e)) return fallback;
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int v))
                throw new InvalidDataException($"字段 host_tuning.{name} 必须是整数：{where}");
            if (v < min || v > max)
                throw new InvalidDataException($"字段 host_tuning.{name}={v} 越界（允许 {min}..{max}）：{where}");
            return v;
        }

        private static long Long(JsonElement el, string name, long fallback, long min, long max, string where)
        {
            if (!el.TryGetProperty(name, out JsonElement e)) return fallback;
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out long v))
                throw new InvalidDataException($"字段 host_tuning.{name} 必须是整数：{where}");
            if (v < min || v > max)
                throw new InvalidDataException($"字段 host_tuning.{name}={v} 越界（允许 {min}..{max}）：{where}");
            return v;
        }

        /// <summary>规范化文本（启动日志/诊断；无密钥）。</summary>
        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "drain={0}+{1}ms opsMs={2} maxPacket={3} cleanupTicks={4} idleTtlMs={5} lobbyTimeoutMs={6} "
                + "reconnect=[ttlMs={7} capacity={8}] outboxCompactMarks={9}",
                DrainGraceMs, DrainWatchdogExtraMs, OpsIntervalMs, MaxInboundPacketBytes,
                SessionCleanupIntervalTicks, SessionUnseatedIdleTtlMs, LobbyRequestTimeoutMs,
                ReconnectTicketTtlMs, ReconnectCapacity, OutboxCompactThresholdMarks);
        }
    }
}
