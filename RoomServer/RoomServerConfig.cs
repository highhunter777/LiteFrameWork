using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using LiteSim;
using RoomServer.Runtime;

namespace RoomServer
{
    /// <summary>
    /// 房间服务器配置源（《商业级通用服务端框架总设计》§429"端口、Worker 数、**房间容量**、
    /// tick/snapshot 频率、队列上限和安全策略**均有范围校验**"；§520/§600"**不把估算值写死为事实**"
    /// "不在设计阶段虚构固定房间数"）。
    ///
    /// **两个分区，两种语义**：
    /// - <c>rooms</c>：房间**模板**。动态建房时按模板取值——每个 roomId 一份（§6"一个 roomId
    ///   只能映射一个独立 RoomActor"）。模板缺失/字段缺失 → **拒绝建房**，不静默兜底。
    /// - <c>combat</c>：**进程级共享**玩法数值。所有房间共用同一份（Sim 系统静态读
    ///   <see cref="CombatConfig"/>，参数化迁移归 G1/热更批）。
    ///
    /// **不可变性**（§188"房间创建时固定不可变玩法配置…已有数据发布只供新房间采用"）：
    /// 本类在启动时**快照** json 文本，此后不再读文件——运行中改盘上文件不影响已建房间，
    /// 也不影响将来的房间（要生效必须重启）。这条同时消除"建房时文件正被改写"的竞态。
    ///
    /// **fail-fast**：坏文件/坏字段/越界一律抛（同 <see cref="CombatNumbers"/>"数值错了必然分叉——
    /// 宁可起不来"）。静默兜底会让配置错误变成线上的隐形分叉。
    /// </summary>
    public sealed class RoomServerConfig
    {
        /// <summary>默认模板名（配置未给 <c>defaults</c> 时使用）。</summary>
        public const string DefaultTemplateId = "default";

        private readonly Dictionary<string, RoomTemplate> _rooms;
        private readonly string _defaultTemplateId;
        private readonly CombatNumValues _combat;

        /// <summary>监听端口（宿主自身参数，不随房间变）。</summary>
        private int _port;

        public int Port
        {
            get { return _port; }
        }

        /// <summary>
        /// 覆盖监听端口（命令行 `--port` 用；取值范围同装载期校验）。
        /// 端口是**宿主级**参数——所有房间共用，不是每房间一份。
        /// </summary>
        public void OverridePort(int port)
        {
            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), port, "端口越界（允许 1..65535）");
            _port = port;
        }

        /// <summary>房间容量上限（§429：可配，**范围校验**；达上限拒绝新建房）。</summary>
        public readonly int MaxRooms;

        /// <summary>
        /// 所有模板里最大的 <c>expected_players</c>（装载期算好）。
        /// 用途：会话表容量必须按**全服潜在连接数**算，不能按单个房间算——
        /// 多房间下"按首房间 ×4"会让第二个房间的连接被会话上限拒掉（实测踩过）。
        /// </summary>
        public readonly int MaxExpectedPlayers;

        /// <summary>本进程允许的 audience（票据比对；空 = 不校验）。</summary>
        public readonly string Audience;

        /// <summary>固定 Worker 数（R2 Worker Pool；当前默认 1，运行时路由切换前保持单循环行为兼容）。</summary>
        public readonly int WorkerCount;

        /// <summary>每个固定 Worker 的有界 Mailbox 容量（R2；核心与宿主生命周期已接线，运行时消息路由后续生效）。</summary>
        public readonly int MailboxCapacity;

        /// <summary>
        /// 结算 Outbox 日志路径（M0-c 后续批"排空第 4 步"；§11.3 本地持久 Outbox）。
        /// 缺省 Outbox/settlements.journal。**强制 .journal 后缀**：RoomServer/Data 的
        /// .json/.bytes 参与 buildHash 哈希闭包——日志若配成那里的 .json，会随每局结算漂移、
        /// 两端握手全拒（这是一条实测级红线，装载期直接拒，不给运行期踩）。
        /// </summary>
        public readonly string SettlementJournalPath;

        /// <summary>结算 Outbox 容量上限（§6"有界 Outbox"；满则入盒显式拒绝并计数）。</summary>
        public readonly int SettlementOutboxCapacity;

        private readonly string _sourcePath;

        private RoomServerConfig(string sourcePath, int port, int maxRooms, string audience,
            Dictionary<string, RoomTemplate> rooms, string defaultTemplateId,
            CombatNumValues combat, int workerCount, int mailboxCapacity,
            string settlementJournalPath, int settlementOutboxCapacity)
        {
            _sourcePath = sourcePath;
            _port = port;
            MaxRooms = maxRooms;
            Audience = audience ?? string.Empty;
            WorkerCount = workerCount;
            MailboxCapacity = mailboxCapacity;
            _rooms = rooms;
            _defaultTemplateId = defaultTemplateId;
            _combat = combat;
            SettlementJournalPath = settlementJournalPath;
            SettlementOutboxCapacity = settlementOutboxCapacity;

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

        /// <summary>玩法数值（共享）。宿主把它装载进 <see cref="CombatConfig"/>。</summary>
        public CombatNumValues Combat
        {
            get { return _combat; }
        }

        /// <summary>
        /// 按模板名造一份房间配置。模板不存在或必填项缺失 → 抛（**不兜底**——
        /// 动态建房下"默认值"会让打错房号的请求静默建成一个配置不对的房间）。
        /// </summary>
        public RoomConfig BuildRoomConfig(string templateId, string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
                throw new ArgumentException("roomId 不能为空（房间号是路由键）", nameof(roomId));

            string id = string.IsNullOrEmpty(templateId) ? _defaultTemplateId : templateId;
            if (!_rooms.TryGetValue(id, out RoomTemplate t))
                throw new InvalidOperationException(
                    $"房间模板不存在：{id}（可用：{string.Join(",", _rooms.Keys)}）——配置：{_sourcePath}");

            return new RoomConfig
            {
                RoomId = roomId,
                Port = Port,
                ExpectedPlayers = t.ExpectedPlayers,
                Seed = t.Seed,
                MatchTimeLimitMs = t.MatchTimeLimitMs,
                WaitingTimeoutMs = t.WaitingTimeoutMs,
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

            int port = RequireIntRange(root, "port", 1, 65535, sourcePath);

            // 房间容量：**必填**。§520/§600 明确不许把估算值写死；此处默认 0 = 未配置 → 直接拒。
            int maxRooms = RequireIntRange(root, "max_rooms", 1, 65535, sourcePath);

            string audience = OptionalString(root, "audience");

            // R2 Worker Pool 配置：默认 1 保持当前单循环行为；范围先钉住，避免线程数/队列
            // 上限被错误配置成资源放大器。核心与宿主生命周期已接线，运行时路由按批次推进。
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

            // combat：全进程共享（Sim 静态读）——必填，且**不允许按房间覆盖**。
            // 形状与 Luban 产物 tbcombatnum.json 一致（`[{...}]`），故配置既可直接内联，
            // 也可由部署把表产物整段嵌入——两条路产出的字节形状相同。
            if (!root.TryGetProperty("combat", out JsonElement combatEl) || combatEl.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"配置缺 combat 分区或形状不对（应为数组，同 tbcombatnum.json）：{sourcePath}");
            CombatNumValues combat = CombatNumbers.Parse(combatEl.GetRawText());

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

            // 显式拒绝"按房间覆盖玩法数值"：Sim 静态读全局，宿主侧覆盖造不出真隔离，
            // 只会让票据 ConfigHash 与实际计算值不符（见 RoomServerConfig 类注释）。
            foreach (string key in rooms.Keys)
            {
                if (string.Equals(key, "combat", StringComparison.Ordinal))
                    throw new InvalidDataException($"rooms 不得含名为 combat 的模板（与进程级分区同名）：{sourcePath}");
            }

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

            return new RoomServerConfig(sourcePath, port, maxRooms, audience, rooms, defaultId, combat,
                workerCount, mailboxCapacity, journal, outboxCapacity);
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

            public static RoomTemplate Parse(string name, JsonElement el, string sourcePath)
            {
                // ExpectedPlayers 必填且 ≥1（配置错误 vs 缺省：满员开局依赖它，缺省会让房间永远不开局）
                int players = RequireIntRange(el, "expected_players", 1, 64, $"{sourcePath} rooms.{name}");
                long seed = OptionalLong(el, "seed", 0);
                long timeLimit = OptionalLong(el, "match_time_limit_ms", 0);
                long waitTimeout = OptionalLong(el, "waiting_timeout_ms", 0);
                if (timeLimit < 0 || waitTimeout < 0)
                    throw new InvalidDataException($"rooms.{name} 的时限不可为负：{sourcePath}");
                return new RoomTemplate
                {
                    ExpectedPlayers = players,
                    Seed = seed,
                    MatchTimeLimitMs = timeLimit,
                    WaitingTimeoutMs = waitTimeout,
                };
            }
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
                "port={0} maxRooms={1} workerCount={2} mailboxCapacity={3} audience={4} rooms=[{5}] default={6} settlementJournal={7} settlementCapacity={8}",
                Port, MaxRooms, WorkerCount, MailboxCapacity,
                string.IsNullOrEmpty(Audience) ? "(不校验)" : Audience,
                string.Join(",", _rooms.Keys), _defaultTemplateId, SettlementJournalPath,
                SettlementOutboxCapacity);
        }
    }
}
