using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;

namespace RoomServer
{
    /// <summary>
    /// 房间服务器配置源（《商业级通用服务端框架总设计》§429"端口、Worker 数、**房间容量**、
    /// tick/snapshot 频率、队列上限和安全策略**均有范围校验**"；§520/§600"**不把估算值写死为事实**"
    /// "不在设计阶段虚构固定房间数"）。
    ///
    /// **分区语义**（rooms 按模板逐房取值；玩法数值不在此文件）：
    /// - <c>rooms</c>：房间**模板**。动态建房时按模板取值——每个 roomId 一份（§6"一个 roomId
    ///   只能映射一个独立 RoomActor"）。模板缺失/字段缺失 → **拒绝建房**，不静默兜底。
    /// - 玩法数值**不在本文件**：服务端与客户端同读 `Assets/GameData/Config/*.bytes`
    ///   （<see cref="ServerTableLoader"/>——数值走单源表格式）。
    /// - <c>rate_limit</c>：**进程级共享**分层限流参（可选，缺省见
    ///   <see cref="RateLimitSettings.Default"/>）。
    ///
    /// **不可变性**（§188"房间创建时固定不可变玩法配置…已有数据发布只供新房间采用"）：
    /// 本类在启动时**快照** json 文本，此后不再读文件——运行中改盘上文件不影响已建房间，
    /// 也不影响将来的房间（要生效必须重启）。这条同时消除"建房时文件正被改写"的竞态。
    ///
    /// **fail-fast**：坏文件/坏字段/越界一律抛（同 <see cref="ServerTableLoader"/>"数值错了必然分叉——
    /// 宁可起不来"）。静默兜底会让配置错误变成线上的隐形分叉。
    /// </summary>
    public sealed class RoomServerConfig
    {
        /// <summary>默认模板名（配置未给 <c>defaults</c> 时使用）。</summary>
        public const string DefaultTemplateId = "default";

        private readonly Dictionary<string, RoomTemplate> _rooms;
        private readonly string _defaultTemplateId;

        /// <summary>监听端口（宿主自身参数，不随房间变）。</summary>
        private int _port;

        public int Port
        {
            get { return _port; }
        }

        /// <summary>
        /// 覆盖监听端口（命令行 `--port` 用；取值范围同装载期校验）。
        /// 端口是**宿主级**参数——所有房间共用，不是每房间一份。
        /// <c>0</c> 合法 = 系统分配空闲端口（实际端口经 <c>ServerHost.BoundPort</c> 回读）。
        /// </summary>
        public void OverridePort(int port)
        {
            if (port < 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), port, "端口越界（允许 0..65535；0 = 系统分配）");
            _port = port;
        }

        /// <summary>房间容量上限（§429：可配，**范围校验**；达上限拒绝新建房）。</summary>
        public readonly int MaxRooms;

        /// <summary>
        /// 指标出口端口（R4 第一批：/metrics 零依赖文本出口——Prometheus 子集，计数单源在
        /// <c>Ops.FormatMetrics</c>）。<c>0</c> = **关闭**（缺省，行为零变化）；&gt;0 时 Program
        /// 在该端口起 HttpListener。宿主级参数（同 <see cref="Port"/> 语义，不随房间变）。
        /// Windows 非管理员绑定 <c>+</c> 前缀需 <c>netsh http urlacl</c>——启动失败走**告警降级**
        /// （宿主照常运行，无 /metrics），不给可观测面打断服务可用性。
        /// </summary>
        public readonly int MetricsPort;

        /// <summary>
        /// 运维（GM）端点端口（《云端GM通道专项设计》§1/§4）：<c>0</c> = **关闭**（缺省，行为零变化）。
        /// 与 <see cref="MetricsPort"/> 的关键差别是**这是写面**，故三重门禁：仅 POST、共享密钥
        /// （环境变量 <c>LITENET_GM_KEY</c>，空 ⇒ 装配方不启用）、绑定面不得为通配地址。
        /// </summary>
        public readonly int GmPort;

        /// <summary>
        /// 运维端点绑定面（缺省 <c>127.0.0.1</c>）。**通配地址（<c>0.0.0.0</c> / <c>::</c> / <c>+</c> / <c>*</c>）装载期即拒**——
        /// 公网可达的写端点不是"配置失误"而是安全事故，fail-fast 不给运行期踩；云上取隧道地址 <c>10.8.0.1</c>。
        /// </summary>
        public readonly string GmBind;

        /// <summary>
        /// 所有模板里最大的 <c>expected_players</c>（装载期算好）。
        /// 用途：会话表容量必须按**全服潜在连接数**算，不能按单个房间算——
        /// 多房间下"按首房间 ×4"会让第二个房间的连接被会话上限拒掉。
        /// </summary>
        public readonly int MaxExpectedPlayers;

        /// <summary>本进程允许的 audience（票据比对；空 = 不校验）。</summary>
        public readonly string Audience;

        /// <summary>
        /// 固定 Worker 数（§8.2）。生产入口以 workerExecution 形态装配：房间命令与快照广播
        /// 在 <c>hash(roomId) % worker_count</c> 归属的 Worker 上执行；缺省 1 保持与宿主
        /// owner 直驱相同的执行形态（嵌入式默认形态不受影响）。
        /// </summary>
        public readonly int WorkerCount;

        /// <summary>每个固定 Worker 的有界 Mailbox 容量。</summary>
        public readonly int MailboxCapacity;

        /// <summary>
        /// 结算 Outbox 日志路径（"排空第 4 步"；§11.3 本地持久 Outbox）。
        /// 缺省 Outbox/settlements.journal。**强制 .journal 后缀**：RoomServer/Data 的
        /// .json/.bytes 参与 buildHash 哈希闭包——日志若配成那里的 .json，会随每局结算漂移、
        /// 两端握手全拒（这是一条红线，装载期直接拒，不给运行期踩）。
        /// </summary>
        public readonly string SettlementJournalPath;

        /// <summary>结算 Outbox 容量上限（§6"有界 Outbox"；满则入盒显式拒绝并计数）。</summary>
        public readonly int SettlementOutboxCapacity;

        /// <summary>
        /// 分层限流参（§343"限流桶必须有容量上限和周期清理"、§595"限流可按 IP/账号/Session 生效"）。
        /// <c>rate_limit</c> 分区可选：缺失 → <see cref="RateLimitSettings.Default"/>；
        /// 给了 → 逐字段范围校验（不静默兜底半段配置）。
        /// </summary>
        public readonly RateLimitSettings RateLimit;

        /// <summary>
        /// Lobby 注册端点**完整 URL**（《Meta 服务专项设计》§7 实例注册；空 = 不注册——
        /// 单机/离线形态）。示例：<c>https://meta.example.com/lobby/instances/register</c>。
        /// 实例密钥**不在此**（密钥走环境变量/命令行注入，见 Program）。
        /// </summary>
        public readonly string LobbyUrl;

        /// <summary>实例唯一标识（lobby.url 配置时必填；重启保持稳定）。</summary>
        public readonly string LobbyInstanceId;

        /// <summary>对客户端通告的 host（端口用宿主实际监听端口回读——配置端口 0 时不可自证）。</summary>
        public readonly string LobbyAdvertiseHost;

        /// <summary>心跳间隔（毫秒；应显著小于 Meta 回告的存活时限）。</summary>
        public readonly long LobbyHeartbeatIntervalMs;

        /// <summary>是否启用 Lobby 注册（配置了端点即启用）。</summary>
        public bool LobbyEnabled
        {
            get { return !string.IsNullOrEmpty(LobbyUrl); }
        }

        /// <summary>
        /// 结算提交端点**完整 URL**（《上云测试专项设计》§2"后台服务读本地 Outbox → Meta 幂等端点"；
        /// 空 = 提交管道关闭）。示例：<c>https://meta.example.com/matches/result</c>。
        /// 实例密钥同 Lobby 注册（环境变量注入，见 Program）。
        /// </summary>
        public readonly string SettlementSubmitUrl;

        /// <summary>提交管道空闲轮询间隔（毫秒）。</summary>
        public readonly long SettlementIntervalMs;

        /// <summary>连续失败退避上限（毫秒）。</summary>
        public readonly long SettlementMaxBackoffMs;

        /// <summary>单次提交请求超时（毫秒）。</summary>
        public readonly long SettlementTimeoutMs;

        /// <summary>是否启用结算提交管道（配置了端点即启用）。</summary>
        public bool SettlementEnabled
        {
            get { return !string.IsNullOrEmpty(SettlementSubmitUrl); }
        }

        /// <summary>
        /// 宿主运行期调参（排空宽限 / Ops 间隔 / 入包上限 / 会话清理与静默 / 重连票据 / 压实阈值）。
        /// 分区 <c>host_tuning</c> 可选：缺省 = <see cref="HostTuning.Default"/>（= 迁移前的字面量常量）。
        /// **这些值原先改代码才能改**（P2 批二）——部署形态下调参不该动代码。
        /// </summary>
        public readonly HostTuning HostTuning;

        private readonly string _sourcePath;

        private RoomServerConfig(string sourcePath, int port, int maxRooms, string audience,
            Dictionary<string, RoomTemplate> rooms, string defaultTemplateId,
            int workerCount, int mailboxCapacity,
            string settlementJournalPath, int settlementOutboxCapacity, RateLimitSettings rateLimit,
            string lobbyUrl, string lobbyInstanceId, string lobbyAdvertiseHost, long lobbyHeartbeatMs,
            string settlementSubmitUrl, long settlementIntervalMs, long settlementMaxBackoffMs, long settlementTimeoutMs,
            HostTuning hostTuning, int metricsPort, int gmPort, string gmBind)
        {
            _sourcePath = sourcePath;
            _port = port;
            MaxRooms = maxRooms;
            MetricsPort = metricsPort;
            GmPort = gmPort;
            GmBind = string.IsNullOrWhiteSpace(gmBind) ? "127.0.0.1" : gmBind;
            Audience = audience ?? string.Empty;
            WorkerCount = workerCount;
            MailboxCapacity = mailboxCapacity;
            _rooms = rooms;
            _defaultTemplateId = defaultTemplateId;
            SettlementJournalPath = settlementJournalPath;
            SettlementOutboxCapacity = settlementOutboxCapacity;
            RateLimit = rateLimit ?? RateLimitSettings.Default;
            LobbyUrl = lobbyUrl ?? string.Empty;
            LobbyInstanceId = lobbyInstanceId ?? string.Empty;
            LobbyAdvertiseHost = lobbyAdvertiseHost ?? string.Empty;
            LobbyHeartbeatIntervalMs = lobbyHeartbeatMs;
            SettlementSubmitUrl = settlementSubmitUrl ?? string.Empty;
            SettlementIntervalMs = settlementIntervalMs;
            SettlementMaxBackoffMs = settlementMaxBackoffMs;
            SettlementTimeoutMs = settlementTimeoutMs;
            HostTuning = hostTuning ?? HostTuning.Default;

            int widest = 0;
            foreach (RoomTemplate t in rooms.Values)
                if (t.ExpectedPlayers > widest) widest = t.ExpectedPlayers;
            MaxExpectedPlayers = widest;
        }

        /// <summary>配置来源（启动日志用；不参与逻辑）。</summary>
        public string SourcePath
        {
            get { return _sourcePath; }
        }

        /// <summary>可用模板名（诊断用）。</summary>
        public IReadOnlyCollection<string> TemplateIds
        {
            get { return _rooms.Keys; }
        }

        /// <summary>
        /// 按模板名造一份房间配置。模板不存在或必填项缺失 → 抛（**不兜底**——
        /// 动态建房下"默认值"会让打错房号的请求静默建成一个配置不对的房间）。
        /// </summary>
        /// <param name="requestedMapId">票据盖章的请求地图行键（多图批；0 = 未指定——按模板/宿主默认图）。
        /// 模板固定 <c>map_id</c> 与请求各持非 0 且不等 = 策略冲突 → 抛（调用方转"拒绝进房"，
        /// 不静默取其一）。</param>
        /// <param name="requestedExpectedPlayers">票据盖章的请求期望人数（开房参数批；0 = 未指定＝按模板）。
        /// 越界（&gt; 内核席位容量）→ 抛（调用方转"拒绝进房"——不静默钳制，钳出来的房不是玩家要的房）。</param>
        public RoomConfig BuildRoomConfig(string templateId, string roomId, int requestedMapId = 0,
            int requestedExpectedPlayers = 0)
        {
            if (string.IsNullOrEmpty(roomId))
                throw new ArgumentException("roomId 不能为空（房间号是路由键）", nameof(roomId));

            string id = string.IsNullOrEmpty(templateId) ? _defaultTemplateId : templateId;
            if (!_rooms.TryGetValue(id, out RoomTemplate t))
                throw new InvalidOperationException(
                    $"房间模板不存在：{id}（可用：{string.Join(",", _rooms.Keys)}）——配置：{_sourcePath}");

            if (requestedMapId < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedMapId), requestedMapId, "请求地图行键不可为负");
            if (t.MapId > 0 && requestedMapId > 0 && t.MapId != requestedMapId)
                throw new InvalidOperationException(
                    $"模板 {id} 固定 map_id={t.MapId}，票据请求 map_id={requestedMapId}——地图策略冲突，拒绝建房");

            // 请求期望人数（开房参数批）：0 = 未指定（按模板）；上界 = 内核席位容量（与模板装载期同源常量）
            if (requestedExpectedPlayers < 0
                || requestedExpectedPlayers > RoomServer.Runtime.PendingInputRing.MaxPlayerCapacity)
                throw new InvalidOperationException(
                    $"请求期望人数越界：{requestedExpectedPlayers}（允许 0（未指定）或 1.."
                    + RoomServer.Runtime.PendingInputRing.MaxPlayerCapacity + "）——拒绝建房");

            return new RoomConfig
            {
                RoomId = roomId,
                Port = Port,
                Seed = t.Seed,
                MatchTimeLimitMs = t.MatchTimeLimitMs,
                WaitingTimeoutMs = t.WaitingTimeoutMs,
                Rules = t.Rules,
                MapId = requestedMapId > 0 ? requestedMapId : t.MapId,   // 0 = 未指定（宿主默认图，落图在 ServerHost）
                // 请求人数：正值优先（开房参数批——玩家选的房规模），0 = 按模板
                ExpectedPlayers = requestedExpectedPlayers > 0 ? requestedExpectedPlayers : t.ExpectedPlayers,
            };
        }

        /// <summary>模板是否存在（调用方要拒绝而非抛时用）。</summary>
        public bool HasTemplate(string templateId)
        {
            string id = string.IsNullOrEmpty(templateId) ? _defaultTemplateId : templateId;
            return _rooms.ContainsKey(id);
        }

        // ---- 装载 ----

        /// <summary>从文件装载（启动路径）。</summary>
        public static RoomServerConfig Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"房间服务器配置缺失：{path}", path);
            return Parse(File.ReadAllText(path), path);
        }

        /// <summary>纯解析（可测）：json 文本 → 配置。坏格式/坏字段/越界一律抛。</summary>
        public static RoomServerConfig Parse(string json, string sourcePath = "<inline>")
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException($"配置为空：{sourcePath}");

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"配置根必须是对象：{sourcePath}");

            // port = 0 合法：**系统分配空闲端口**（实际端口经 ServerHost.BoundPort 回读）。
            // 测试与同机多实例用 0 避免与常驻软件抢端口（实测本机 aTrustXtunnel 占 7777/7778）。
            int port = RequireIntRange(root, "port", 0, 65535, sourcePath);

            // 房间容量：**必填**。§520/§600 明确不许把估算值写死；此处默认 0 = 未配置 → 直接拒。
            int maxRooms = RequireIntRange(root, "max_rooms", 1, 65535, sourcePath);

            string audience = OptionalString(root, "audience");

            // Worker Pool 配置：默认 1 保持单循环行为；范围钉住，避免线程数/队列
            // 上限被错误配置成资源放大器。
            long workerCountValue = OptionalLong(root, "worker_count", 1);
            if (workerCountValue < 1 || workerCountValue > 256)
                throw new InvalidDataException(
                    $"worker_count 越界（允许 1..256）：{workerCountValue}：{sourcePath}");
            int workerCount = (int)workerCountValue;

            long mailboxCapacityValue = OptionalLong(root, "mailbox_capacity", 1024);
            if (mailboxCapacityValue < 1 || mailboxCapacityValue > 1_000_000)
                throw new InvalidDataException(
                    $"mailbox_capacity 越界（允许 1..1000000）：{mailboxCapacityValue}：{sourcePath}");
            int mailboxCapacity = (int)mailboxCapacityValue;

            // combat 内联分区不受支持：玩法数值走 .bytes 表（ServerTableLoader）。
            if (root.TryGetProperty("combat", out JsonElement legacyCombat))
                throw new InvalidDataException(
                    $"combat 分区不受支持（玩法数值走 Assets/GameData/Config/*.bytes 表——见 ServerTableLoader）：{sourcePath}");

            if (!root.TryGetProperty("rooms", out JsonElement roomsEl) || roomsEl.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"配置缺 rooms 分区（房间模板表）：{sourcePath}");

            var rooms = new Dictionary<string, RoomTemplate>(StringComparer.Ordinal);
            foreach (JsonProperty p in roomsEl.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"rooms.{p.Name} 必须是对象：{sourcePath}");
                rooms[p.Name] = RoomTemplate.Parse(p.Name, p.Value, sourcePath);
            }
            if (rooms.Count == 0)
                throw new InvalidDataException($"rooms 分区为空（至少一个模板）：{sourcePath}");

            string defaultId = OptionalString(root, "default_template");
            if (string.IsNullOrEmpty(defaultId)) defaultId = DefaultTemplateId;
            if (!rooms.ContainsKey(defaultId))
                throw new InvalidDataException(
                    $"default_template 指向不存在的模板：{defaultId}（可用：{string.Join(",", rooms.Keys)}）：{sourcePath}");

            // ---- 结算 Outbox（排空第 4 步；可选字段 + 范围校验 + 路径红线）----
            string journal = OptionalString(root, "settlement_journal");
            if (string.IsNullOrEmpty(journal)) journal = "Outbox/settlements.journal";
            if (!journal.EndsWith(".journal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"settlement_journal 必须以 .journal 结尾（RoomServer/Data 的 .json/.bytes 参与 buildHash 哈希闭包，日志落错处会让哈希随对局漂移、两端握手全拒）：{journal}：{sourcePath}");
            if (Path.IsPathRooted(journal) && !File.Exists(journal))
            {
                // 绝对路径允许（部署形态），但目录必须在（提前暴露错配置，不在首次结算时才炸）
                string dir = Path.GetDirectoryName(journal);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    throw new InvalidDataException($"settlement_journal 目录不存在：{dir}：{sourcePath}");
            }

            long outboxCapacityValue = OptionalLong(root, "settlement_outbox_capacity", 10_000);
            if (outboxCapacityValue < 1 || outboxCapacityValue > 1_000_000)
                throw new InvalidDataException(
                    $"settlement_outbox_capacity 越界（允许 1..1000000）：{outboxCapacityValue}：{sourcePath}");
            int outboxCapacity = (int)outboxCapacityValue;

            RateLimitSettings rateLimit = ParseRateLimit(root, sourcePath);

            // ---- Lobby 注册（可选分区；url 空 = 不注册）----
            string lobbyUrl = string.Empty;
            string lobbyInstanceId = string.Empty;
            string lobbyAdvertiseHost = string.Empty;
            long lobbyHeartbeatMs = 10_000;
            if (root.TryGetProperty("lobby", out JsonElement lobbyEl))
            {
                if (lobbyEl.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"lobby 分区必须是对象：{sourcePath}");
                lobbyUrl = OptionalString(lobbyEl, "url") ?? string.Empty;
                if (!string.IsNullOrEmpty(lobbyUrl))
                {
                    if (!Uri.TryCreate(lobbyUrl, UriKind.Absolute, out Uri lobbyUri)
                        || (lobbyUri.Scheme != Uri.UriSchemeHttp && lobbyUri.Scheme != Uri.UriSchemeHttps))
                        throw new InvalidDataException($"lobby.url 必须是绝对 http/https URL：{sourcePath}");

                    lobbyInstanceId = OptionalString(lobbyEl, "instance_id") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(lobbyInstanceId)
                        || Encoding.UTF8.GetByteCount(lobbyInstanceId) > 64)
                        throw new InvalidDataException(
                            $"lobby.url 配置时 lobby.instance_id 必填且不超过 64 UTF-8 字节：{sourcePath}");

                    lobbyAdvertiseHost = OptionalString(lobbyEl, "advertise_host") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(lobbyAdvertiseHost)
                        || Encoding.UTF8.GetByteCount(lobbyAdvertiseHost) > 128)
                        throw new InvalidDataException(
                            $"lobby.url 配置时 lobby.advertise_host 必填（客户端可达地址的 host，端口用实际监听端口）：{sourcePath}");

                    lobbyHeartbeatMs = OptionalLong(lobbyEl, "heartbeat_interval_ms", 10_000);
                    if (lobbyHeartbeatMs < 1_000 || lobbyHeartbeatMs > 300_000)
                        throw new InvalidDataException(
                            $"lobby.heartbeat_interval_ms 越界（允许 1000..300000）：{lobbyHeartbeatMs}：{sourcePath}");
                }
                else if (lobbyEl.TryGetProperty("instance_id", out _)
                    || lobbyEl.TryGetProperty("advertise_host", out _)
                    || lobbyEl.TryGetProperty("heartbeat_interval_ms", out _))
                {
                    // 半段配置：url 空时其余字段无意义——宁可起不来，不让"以为在上报"成为假象
                    throw new InvalidDataException($"lobby.url 为空时不应配置 lobby 其余字段（半段配置）：{sourcePath}");
                }
            }

            // ---- 结算提交管道（可选分区；submit_url 空 = 关闭）----
            string settlementUrl = string.Empty;
            long settlementIntervalMs = 5_000;
            long settlementMaxBackoffMs = 60_000;
            long settlementTimeoutMs = 5_000;
            if (root.TryGetProperty("settlement", out JsonElement settlementEl))
            {
                if (settlementEl.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"settlement 分区必须是对象：{sourcePath}");
                settlementUrl = OptionalString(settlementEl, "submit_url") ?? string.Empty;
                if (!string.IsNullOrEmpty(settlementUrl))
                {
                    if (!Uri.TryCreate(settlementUrl, UriKind.Absolute, out Uri settlementUri)
                        || (settlementUri.Scheme != Uri.UriSchemeHttp && settlementUri.Scheme != Uri.UriSchemeHttps))
                        throw new InvalidDataException($"settlement.submit_url 必须是绝对 http/https URL：{sourcePath}");

                    settlementIntervalMs = OptionalLong(settlementEl, "submit_interval_ms", 5_000);
                    if (settlementIntervalMs < 1_000 || settlementIntervalMs > 300_000)
                        throw new InvalidDataException(
                            $"settlement.submit_interval_ms 越界（允许 1000..300000）：{settlementIntervalMs}：{sourcePath}");

                    settlementMaxBackoffMs = OptionalLong(settlementEl, "submit_max_backoff_ms", 60_000);
                    if (settlementMaxBackoffMs < settlementIntervalMs || settlementMaxBackoffMs > 600_000)
                        throw new InvalidDataException(
                            $"settlement.submit_max_backoff_ms 越界（允许 {settlementIntervalMs}..600000）：{settlementMaxBackoffMs}：{sourcePath}");

                    settlementTimeoutMs = OptionalLong(settlementEl, "submit_timeout_ms", 5_000);
                    if (settlementTimeoutMs < 500 || settlementTimeoutMs > 60_000)
                        throw new InvalidDataException(
                            $"settlement.submit_timeout_ms 越界（允许 500..60000）：{settlementTimeoutMs}：{sourcePath}");
                }
                else if (settlementEl.TryGetProperty("submit_interval_ms", out _)
                    || settlementEl.TryGetProperty("submit_max_backoff_ms", out _)
                    || settlementEl.TryGetProperty("submit_timeout_ms", out _))
                {
                    // 半段配置：submit_url 空时其余字段无意义——宁可起不来
                    throw new InvalidDataException($"settlement.submit_url 为空时不应配置 settlement 其余字段（半段配置）：{sourcePath}");
                }
            }

            // ---- 宿主运行期调参（可选分区；缺省 = 迁移前的字面量常量，行为零变化）----
            HostTuning hostTuning = root.TryGetProperty("host_tuning", out JsonElement tuningEl)
                ? HostTuning.Parse(tuningEl, sourcePath)
                : HostTuning.Default;

            // ---- 指标出口端口（可选；0 = 关闭——缺省行为零变化；范围同 port）----
            long metricsPortValue = OptionalLong(root, "metrics_port", 0);
            if (metricsPortValue < 0 || metricsPortValue > 65535)
                throw new InvalidDataException(
                    $"metrics_port 越界（允许 0..65535；0 = 关闭）：{metricsPortValue}：{sourcePath}");

            // ---- 运维（GM）端点（可选；gm_port=0 关闭；启用时绑定面不得为通配地址）----
            long gmPortValue = OptionalLong(root, "gm_port", 0);
            if (gmPortValue < 0 || gmPortValue > 65535)
                throw new InvalidDataException(
                    $"gm_port 越界（允许 0..65535；0 = 关闭）：{gmPortValue}：{sourcePath}");
            string gmBind = OptionalString(root, "gm_bind");
            if (string.IsNullOrWhiteSpace(gmBind)) gmBind = "127.0.0.1";
            if (gmPortValue > 0 && IsWildcardBind(gmBind))
                throw new InvalidDataException(
                    $"gm_bind 不可为通配地址（{gmBind}）——运维写端点只对运维可达地址开放（云上＝隧道地址）：{sourcePath}");

            return new RoomServerConfig(sourcePath, port, maxRooms, audience, rooms, defaultId,
                workerCount, mailboxCapacity, journal, outboxCapacity, rateLimit,
                lobbyUrl, lobbyInstanceId, lobbyAdvertiseHost, lobbyHeartbeatMs,
                settlementUrl, settlementIntervalMs, settlementMaxBackoffMs, settlementTimeoutMs,
                hostTuning, (int)metricsPortValue, (int)gmPortValue, gmBind);
        }

        /// <summary>
        /// 分层限流分区（可选）。整段缺失 → 缺省；只要给了分区就**逐字段**校验
        /// （半段错配置会静默改变安全水位——宁可起不来）。
        /// </summary>
        private static RateLimitSettings ParseRateLimit(JsonElement root, string sourcePath)
        {
            if (!root.TryGetProperty("rate_limit", out JsonElement rl)) return RateLimitSettings.Default;
            if (rl.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"rate_limit 必须是对象：{sourcePath}");

            RateLimitSettings.Lane ipConnect = ParseLane(rl, "ip", 32, 4, sourcePath);
            RateLimitSettings.Lane ipEntry = ParseLane(rl, "entry", 32, 8, sourcePath);
            RateLimitSettings.Lane accountEntry = ParseLane(rl, "account", 16, 2, sourcePath);
            RateLimitSettings.Lane sessionPackets = ParseLane(rl, "session", 256, 128, sourcePath);

            // 键表容量上限（§343）：1 百万条 ~= 每桶 ~50B，封顶内存；下限 1 保证能限速。
            long buckets = OptionalLong(rl, "buckets", 8192);
            if (buckets < 1 || buckets > 1_000_000)
                throw new InvalidDataException($"rate_limit.buckets 越界（允许 1..1000000）：{buckets}：{sourcePath}");

            // 空闲桶回收阈值：过小会误清活跃桶（丢欠账），过大等于不清理——范围两头都钉住。
            long idleMs = OptionalLong(rl, "idle_ms", 120_000);
            if (idleMs < 1_000 || idleMs > 86_400_000)
                throw new InvalidDataException($"rate_limit.idle_ms 越界（允许 1000..86400000）：{idleMs}：{sourcePath}");

            return new RateLimitSettings(ipConnect, ipEntry, accountEntry, sessionPackets,
                (int)buckets, idleMs);
        }

        private static RateLimitSettings.Lane ParseLane(JsonElement parent, string prefix,
            int defaultBurst, double defaultPerSec, string sourcePath)
        {
            long burst = OptionalLong(parent, prefix + "_burst", defaultBurst);
            if (burst < 1 || burst > 65_535)
                throw new InvalidDataException(
                    $"rate_limit.{prefix}_burst 越界（允许 1..65535）：{burst}：{sourcePath}");

            double perSec = OptionalDouble(parent, prefix + "_per_sec", defaultPerSec);
            if (!(perSec > 0) || perSec > 100_000)   // 非 >0 同时挡 NaN/0/负数
                throw new InvalidDataException(
                    $"rate_limit.{prefix}_per_sec 越界（允许 (0,100000]）：{perSec.ToString(CultureInfo.InvariantCulture)}：{sourcePath}");

            return new RateLimitSettings.Lane((int)burst, perSec);
        }

        /// <summary>可调默认路径（相对工作目录）。</summary>
        public const string DefaultRelativePath = "Config/roomserver.json";

        /// <summary>房间模板：一个 roomId 的创建参数。</summary>
        private sealed class RoomTemplate
        {
            public int ExpectedPlayers;
            public long Seed;
            public long MatchTimeLimitMs;
            public long WaitingTimeoutMs;
            public LiteSim.MatchRules Rules;

            /// <summary>模板固定地图行键（多图批；0 = 不固定——按票据请求/宿主默认图）。</summary>
            public int MapId;

            public static RoomTemplate Parse(string name, JsonElement el, string sourcePath)
            {
                // ExpectedPlayers 必填且 ≥1（配置错误 vs 缺省：它定容席位表/实体表——缺省会让房间一进人就满）。
                // **上界 = 内核容量**（PendingInputRing.MaxPlayerCapacity，单 uint 有效位图宽度）：
                // 两侧各写死过一次（此处 64 / 内核 32），33+ 席位配置会在 RoomRuntime 构造抛异常
                // 并窜上权威循环线程（P0-2）——装载期即按内核真相拒绝，不留运行期触发面。
                int players = RequireIntRange(el, "expected_players", 1,
                    RoomServer.Runtime.PendingInputRing.MaxPlayerCapacity, $"{sourcePath} rooms.{name}");
                long seed = OptionalLong(el, "seed", 0);
                long timeLimit = OptionalLong(el, "match_time_limit_ms", 0);
                long waitTimeout = OptionalLong(el, "waiting_timeout_ms", 0);
                if (timeLimit < 0 || waitTimeout < 0)
                    throw new InvalidDataException($"rooms.{name} 的时限不可为负：{sourcePath}");
                // 地图行键（多图批；可选）：0 = 不固定；负值 = 配置错误（是否有此行归房间端建房时表校验）
                long mapId = OptionalLong(el, "map_id", 0);
                if (mapId < 0 || mapId > int.MaxValue)
                    throw new InvalidDataException($"rooms.{name} 的 map_id 越界（须 ≥0）：{sourcePath}");
                LiteSim.MatchRules defaults = LiteSim.MatchRules.Default;
                LiteSim.MatchRules rules;
                try
                {
                    rules = new LiteSim.MatchRules(
                        checked((int)OptionalLong(el, "match_duration_frames", defaults.DurationFrames)),
                        checked((int)OptionalLong(el, "kill_limit", defaults.KillLimit)),
                        checked((int)OptionalLong(el, "respawn_delay_frames", defaults.RespawnDelayFrames)),
                        checked((int)OptionalLong(el, "spawn_protection_frames", defaults.SpawnProtectionFrames)));
                }
                catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is OverflowException)
                {
                    throw new InvalidDataException($"rooms.{name} 的玩法规则越界：{sourcePath}", ex);
                }
                return new RoomTemplate
                {
                    ExpectedPlayers = players,
                    Seed = seed,
                    MatchTimeLimitMs = timeLimit,
                    WaitingTimeoutMs = waitTimeout,
                    Rules = rules,
                    MapId = (int)mapId,
                };
            }
        }

        /// <summary>是否通配绑定面（GM 写端点禁用：公网可达即安全事故）。</summary>
        private static bool IsWildcardBind(string host)
        {
            return string.Equals(host, "0.0.0.0", StringComparison.Ordinal)
                || string.Equals(host, "::", StringComparison.Ordinal)
                || string.Equals(host, "+", StringComparison.Ordinal)
                || string.Equals(host, "*", StringComparison.Ordinal);
        }

        private static int RequireIntRange(JsonElement parent, string name, int min, int max, string where)
        {
            if (!parent.TryGetProperty(name, out JsonElement e) || !e.TryGetInt32(out int v))
                throw new InvalidDataException($"配置缺整数字段 {name}：{where}");
            if (v < min || v > max)
                throw new InvalidDataException($"配置字段 {name}={v} 越界（允许 {min}..{max}）：{where}");
            return v;
        }

        private static long OptionalLong(JsonElement parent, string name, long fallback)
        {
            if (!parent.TryGetProperty(name, out JsonElement e)) return fallback;
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out long v))
                throw new InvalidDataException($"字段 {name} 必须是整数");
            return v;
        }

        private static double OptionalDouble(JsonElement parent, string name, double fallback)
        {
            if (!parent.TryGetProperty(name, out JsonElement e)) return fallback;
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out double v))
                throw new InvalidDataException($"字段 {name} 必须是数字");
            return v;
        }

        private static string OptionalString(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement e)) return null;
            if (e.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"字段 {name} 必须是字符串");
            return e.GetString();
        }

        /// <summary>规范化文本（日志/诊断；不含密钥）。</summary>
        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "port={0} maxRooms={1} workerCount={2} mailboxCapacity={3} audience={4} rooms=[{5}] default={6} settlementJournal={7} settlementCapacity={8} rateLimit=[ip={9} entry={10} account={11} session={12} buckets={13} idleMs={14}] lobby=[url={15} instance={16} advertise={17} heartbeatMs={18}] settlement=[url={19} intervalMs={20} backoffMs={21} timeoutMs={22}] metricsPort={23}",
                Port, MaxRooms, WorkerCount, MailboxCapacity,
                string.IsNullOrEmpty(Audience) ? "(不校验)" : Audience,
                string.Join(",", _rooms.Keys), _defaultTemplateId, SettlementJournalPath,
                SettlementOutboxCapacity,
                LaneText(RateLimit.IpConnect), LaneText(RateLimit.IpEntry),
                LaneText(RateLimit.AccountEntry), LaneText(RateLimit.SessionPackets),
                RateLimit.Buckets, RateLimit.IdleTtlMs,
                LobbyEnabled ? LobbyUrl : "(不注册)", LobbyInstanceId, LobbyAdvertiseHost, LobbyHeartbeatIntervalMs,
                SettlementEnabled ? SettlementSubmitUrl : "(关闭)", SettlementIntervalMs,
                SettlementMaxBackoffMs, SettlementTimeoutMs, MetricsPort);
        }

        private static string LaneText(RateLimitSettings.Lane lane)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}/{1}", lane.Burst, lane.RefillPerSec);
        }
    }
}
