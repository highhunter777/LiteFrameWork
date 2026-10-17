using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Google.Protobuf;
using LiteNet;
using LiteNet.Diagnostics;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 服务器宿主（《状态同步实施方案》§4.5 RoomServer 结构图顶层组装 + 《商业级通用服务端框架总设计》§7/§8）。
    ///
    /// 分层（现为 Application+Host 合体）：
    /// - **协议边界**：解码 <c>PacketCodec</c> → 校验 → 构造 <see cref="RoomCommand"/>；反向把
    ///   <see cref="RoomOutput"/> 映射为 proto 发送。RoomRuntime 完全不见 proto/Transport。
    /// - **会话**：<see cref="Session"/>（连接级记账/ACK 水位/背压）+ 席位（Runtime <see cref="PlayerSession"/>）
    ///   映射表 <c>_seatSessions</c>（playerId → 连接）。
    /// - **快照**：<see cref="SnapshotPipeline"/> 在每次权威步进后拉取 AuthSim 构建下发；
    ///   重连席位在恢复完成 ACK 前停在 Restoring（管线抑制其增量广播），恢复完成时整帧全量重锚广播链（§9.3 步骤 6）。
    ///
    /// 单循环（§10.2 不拆 I/O 线程）：
    /// tick 顺序 TickIncoming → Host owner 排空每房间 Mailbox（Control → Input → Outbound）
    /// → 权威 Tick → 快照广播 → TickOutgoing。
    ///
    /// 装配参数由 <see cref="RoomConfig"/> 提供；**buildHash = <see cref="BuildHash.Value"/>**
    /// （源码内容哈希，两端不一 = 逻辑/协议版本不同 → 拒绝进房）。
    ///
    /// **传输解耦**：构造参数是窄端口 <see cref="IRoomTransport"/>——换传输框架只需新写适配器 + 装配一行。
    /// **所有权**：宿主**接管**传入的传输（构造即 `Start`、`Dispose` 即释放）。
    /// </summary>
    public sealed class ServerHost : IDisposable, IRoomWorkerHost
    {
        /// <summary>服务器版本锚点 = 源码内容哈希（Sim 或协议一改，握手即拒）。</summary>
        public const string ServerBuildHash = BuildHash.Value;

        /// <summary>Ops 周期行间隔**缺省值**（毫秒）——可配，见 <see cref="HostTuning.OpsIntervalMs"/>（P2 批二）。</summary>
        public const long OpsIntervalMs = HostTuning.DefaultOpsIntervalMs;

        /// <summary>入包长度上限**缺省值**（字节）：《服务端总设计》§5 P0-3——protobuf 解析**之前**的硬边界。
        /// 入站合法包 = 冗余输入（4 帧 × ~50B）或 Join/重连信令，远小于此；超限按恶意包丢弃计数。
        /// 可配，见 <see cref="HostTuning.MaxInboundPacketBytes"/>（P2 批二）。</summary>
        public const int MaxInboundPacketBytes = HostTuning.DefaultMaxInboundPacketBytes;

        /// <summary>
        /// Join 字段 UTF-8 字节上限（P0-3：所有字符串限制 UTF-8 字节数——防超长串打爆日志/内存）。
        /// **权威已迁至 <see cref="JoinAdmissionGate"/>**（同值）——此处保留为公开常量仅供既有调用方/测试
        /// 引用；新代码读准入门那份，避免两处漂移。
        /// </summary>
        public const int MaxRoomIdBytes = JoinAdmissionGate.MaxRoomIdBytes;
        public const int MaxTokenBytes = JoinAdmissionGate.MaxTokenBytes;
        public const int MaxBuildHashBytes = JoinAdmissionGate.MaxBuildHashBytes;

        private readonly IRoomTransport _transport;   // 窄端口（可替换性）
        private readonly SessionManager _sessions;
        private readonly ReconnectService _reconnects;

        /// <summary>宿主运行期调参（P2 批二）：Ops 间隔 / 入包上限 / 会话清理节拍与静默 / 重连票据 /
        /// 压实阈值。来源 = 配置的 <c>host_tuning</c> 分区（缺省 <see cref="HostTuning.Default"/>）。</summary>
        private readonly HostTuning _tuning;

        /// <summary>
        /// 分层限流：IP 维度（连接/入场）+ 账号维度（入场）+ Session 维度（包速率）。
        /// 配置装载 <see cref="RoomServerConfig.RateLimit"/> 或构造注入；两者皆无时用
        /// <see cref="RateLimitSettings.Default"/>（§520/§600：缺省是可用形态，不是"实测值"）。
        /// 探测不到的远端地址（<see cref="IRoomTransport.GetRemoteAddress"/> 为 null）**跳过 IP 维度**——
        /// 限不了看不见的地址，跳过而不是全拒（见端口契约）。
        /// </summary>
        private readonly RateLimiter _rateLimit;

        // Ops 的构造移入构造体（批3）：容器装配口注入时采用注入实例，缺省才自建
        private readonly Ops _ops;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>
        /// Join 票据验证器（《服务端总设计》§P0-6；《框架先行》§5-4 必建接缝）。**null = 未装配**：
        /// 此时 token 只作原型级非空校验；
        /// 装配后**逐一验签**，失败即拒绝——<see cref="JoinTicketRejection"/> 分类进 Ops。
        /// </summary>
        private readonly IJoinTicketValidator _tickets;

        /// <summary>本服受众标识（票据 audience 比对；空 = 不做受众校验）。由装配方传入——本层不依赖 Meta 配置。</summary>
        private readonly string _audience;

        /// <summary>
        /// 入站准入门（§P0-6 判定链：频率/字段边界/token/版本/票据/账号频率）。
        /// 独立于宿主——它是"消耗权威资源之前"的纯判定，抽出后可独立测（不必经建房路径）。
        /// 排空红线**不在**门内：那是宿主状态（§12 第 2 步），由 <see cref="InboundJoinHandlers"/> 自行裁决。
        /// </summary>
        private readonly JoinAdmissionGate _admission;

        /// <summary>会话周期清理节拍**缺省值**（600 tick ≈ 10s @60Hz）——可配，见
        /// <see cref="HostTuning.SessionCleanupIntervalTicks"/>（P2 批二）。</summary>
        public const int SessionCleanupIntervalTicks = HostTuning.DefaultSessionCleanupIntervalTicks;
        private int _ticksSinceCleanup;

        // ---- 房间表（§6"一个 roomId 只能映射一个独立 RoomActor"）----
        //
        // **为什么是表而不是字段**：Runtime / 快照管线 / 席位表若并列在宿主上，
        // "每房间一份"在类型上无处表达；收成 RoomInstance 之后，多房间 = 这张表，"每房间一份"由类型保证。
        //
        // **容量**：§429 要求房间容量**可配 + 范围校验**，§520/§600 明确"不把估算值写死为事实"
        // "不在设计阶段虚构固定房间数"——故上限来自配置，达上限**拒绝新建**而非静默拒绝进房。
        // 表本体（双结构/身份校验增删/快照读）收在 <see cref="RoomRegistry"/>；
        // 房间构造与销毁清理仍归宿主——清完场经 registry 落表。
        private readonly RoomRegistry _registry;

        /// <summary>建房时把选定的 roomId 交给 ApplySignal 用（提交到输出应用之间无重入）。</summary>
        private RoomInstance _currentRoom;

        /// <summary>输出应用的定位序：优先当前提交房间（ExecuteCommandTo/ApplyOutboundEnvelope 流转），
        /// 退回插入序首房（历史直驱用例兼容）。</summary>
        private RoomInstance CurrentOrFirstRoom => _currentRoom ?? _registry.First;

        /// <summary>是否已进入排空（§12 第 1–2 步：不再接受新 Join／新房间）。</summary>
        private bool _draining;

        /// <summary>排空截止时刻（单调毫秒；-1 = 未排空）。</summary>
        private long _drainDeadlineMs = -1;

        /// <summary>
        /// 结算 Outbox（§11.3"本地持久 Outbox"）。**null = 未装配**：
        /// SettlementReady 只计数＋日志；装配后入盒（幂等/有界/失败
        /// 全部显式计数，不抛进权威循环），排空第 4 步经 <see cref="FlushSettlementOutbox"/> 收口。
        /// 宿主**接管**其生命周期（Dispose 释放）。
        /// </summary>
        private readonly ISettlementOutbox _settlementOutbox;

        /// <summary>
        /// Worker Pool 生命周期接缝 + Worker 执行形态。固定池已装配并启动。
        /// 入站 Control/Input 先进入每房间 Mailbox：
        /// - **宿主 owner 形态**（默认）：由宿主主线程消费；或
        /// - **Worker 执行形态**（<see cref="_workerExecution"/>）：由 hash 归属的 Worker 消费并执行
        ///   （Runtime + 快照广播），产出经 Outbound lane 回传宿主应用/发送。
        /// 配置形态（<see cref="RoomServerConfig"/>）自动创建，测试/嵌入式形态可显式注入。
        /// </summary>
        private readonly RoomWorkerPool _workerPool;

        /// <summary>
        /// Worker 驱动体（P4 外提件，见 <see cref="RoomWorkerDriver"/>）：本类只保留**生产侧**
        /// （<see cref="ScheduleRoomPump"/>/<see cref="SubmitWorkerTick"/>）与**回传应用侧**
        /// （<c>ApplyOutboundEnvelope</c> 族）——驱动体在归属 Worker 线程上跑，两者线程归属不同。
        /// </summary>
        private readonly RoomWorkerDriver _workerDriver;

        private long _workerFailures;
        private long _workerCommands;
        private long _workerPumps;

        /// <summary>
        /// 是否把入站命令先放入每房间 Mailbox。由执行 owner 消费；
        /// 默认关闭以保留嵌入式用例的直投语义，生产入口显式开启。
        /// </summary>
        private readonly bool _mailboxRouting;
        private readonly bool _drainMailboxesImmediately;

        /// <summary>
        /// Worker 执行形态（《商业级通用服务端框架总设计》§8.2）：房间命令与快照广播在
        /// <c>hash(roomId) % workerCount</c> 归属的 Worker 线程上执行，宿主只做 Transport IO、
        /// 准入与回传应用。默认关闭——嵌入式/历史用例保留宿主 owner 直驱语义，生产入口显式开启。
        /// </summary>
        private readonly bool _workerExecution;

        private long _mailboxRejectedFull;
        private long _mailboxClosed;
        private long _mailboxStale;

        /// <summary>
        /// Control lane 满载时暂存断线事实，待 owner 消费已有控制命令后再按 FIFO 入盒
        /// （判据切分批外提 <see cref="PendingDisconnectQueue"/>——语义与线程契约零变化，语义注释归该类）。
        /// </summary>
        private readonly PendingDisconnectQueue _pendingDisconnects;

        /// <summary>Join 入站族（票据拒绝计数随其持有；宿主公开读口转发）。</summary>
        private readonly InboundJoinHandlers _inboundJoin;

        /// <summary>入站分发与短处理器（Join/Reconnect 两族为其中协作者）。</summary>
        private readonly InboundHandlers _inbound;

        private readonly RoomServerConfig _serverConfig;

        /// <summary>
        /// 玩法数值实例（技术债 #1：装配方显式传入——生产链 `HostAssembly`（必填）→ 本宿主 → 各房间
        /// `RoomRuntime`；缺省 = <see cref="CombatValues.Default"/> 供嵌入式/测试直构形态。
        /// 房间创建时捕获进 <c>FixedCombatConfig</c>，此后进程任何变化不改已建房间。
        /// </summary>
        private readonly CombatValues _combat;

        /// <summary>武器表实例（同链显式传入；缺省 = <see cref="WeaponTable.Default"/> 兜底）。</summary>
        private readonly WeaponTable _weapons;

        /// <summary>
        /// 判定地图注入（生产链 `HostAssembly.Inputs.Map` ← `ServerMapLoader` 装载产物）。房间建立时写入
        /// `RoomConfig.Map`（配置自带者优先——**多图批后**：票据/模板带行键时经 <see cref="_mapCatalog"/>
        /// 按行解析）；**无缺省兜底**（D22）：宿主参数与房间配置两处皆缺由房间构造 fail-fast。
        /// </summary>
        private readonly SimMapData _map;

        /// <summary>
        /// 多图目录（多图批；生产链 `HostAssembly.Inputs.MapCatalog` ← Program 的
        /// <see cref="ServerMapCatalog"/>，缺省 null = 单图形态）。建房时按房间行键解析判定图：
        /// **0 = 宿主默认图（<see cref="_map"/>）**；&gt;0 且目录缺失 = 配置缺失显式拒（不静默回落默认图——
        /// 那会让"请求训练场"变成"静默进首图"）；缺行由目录实现 fail-closed 抛（建房路径转拒绝进房）。
        /// </summary>
        private readonly Func<int, SimMapData> _mapCatalog;

        /// <summary>
        /// 判定面表字节**基摘要**（生产链 `HostAssembly.Inputs.GameplayDigestBase` ← `ServerTableLoad.Digest`；
        /// 缺省 0 = 无表身份形态，供嵌入式/测试直构）。房间创建时随数值/武器同刻捕获进 `FixedCombatConfig`
        /// ——客户端按本地装载字节复算比对（批 E），忘传的表现是**首局即被客户端拒**（显性，不静默）。
        /// </summary>
        private readonly uint _gameplayDigestBase;

        /// <summary>房间容量上限（配置项；§429 范围校验在装载期完成）。</summary>
        public int MaxRooms
        {
            get { return _serverConfig?.MaxRooms ?? 1; }
        }

        /// <summary>
        /// 实际监听端口（<see cref="Start"/> 之后有效；未启动 -1）。
        /// 配置 <c>Port = 0</c> 时由系统分配空闲端口，客户端据此连接——
        /// **测试与同机多实例必须走这条**：固定端口会与同机其他软件冲突
        /// （实测本机 aTrustXtunnel 占 7777/7778，曾致传输用例偶发 Bind 失败）。
        /// </summary>
        public int BoundPort => _transport.BoundPort;
        /// <summary>命令输出复用缓冲（热路径零分配）。</summary>
        private readonly List<RoomOutput> _outputs = new List<RoomOutput>();

        private long _nowMs;
        private bool _disposed;

        public SessionManager Sessions => _sessions;

        /// <summary>首房间配置（兼容只读投影；多房间下"哪个房间"见 <see cref="Rooms"/> / <see cref="TryGetRoom"/>）。</summary>
        public RoomConfig Config { get; }

        /// <summary>首房间运行时（兼容投影；按房号取用 <see cref="TryGetRoom"/>）。
        /// 终态房间会被销毁（<see cref="TryDestroyTerminalRoom"/>），此后本投影为 null——
        /// 需要跨越终态的引用请提前持有或改用 <see cref="TryGetRoom"/>。</summary>
        public RoomRuntime Room => _registry.First?.Runtime;

        /// <summary>首房间快照管线（兼容投影）。</summary>
        public SnapshotPipeline Pipeline => _registry.First?.Pipeline;

        /// <summary>当前装配的 Worker Pool；未使用配置或显式注入时为 null。</summary>
        public RoomWorkerPool WorkerPool => _workerPool;

        /// <summary>Worker Pool 是否已启动。</summary>
        public bool WorkerPoolStarted => _workerPool != null && _workerPool.IsStarted;

        /// <summary>Worker Pool 是否已停止接收并完成停止。</summary>
        public bool WorkerPoolStopped => _workerPool != null && _workerPool.IsStopped;

        /// <summary>Worker Pool 当前待执行工作项数。</summary>
        public int WorkerPoolPending => _workerPool?.PendingCount ?? 0;

        /// <summary>Worker 工作项异常数；异常由池内回调吸收，不杀死 Worker 线程。</summary>
        public long WorkerFailures => Interlocked.Read(ref _workerFailures);

        /// <summary>Worker 执行形态下已执行的房间命令数（Control + Input；观测用）。</summary>
        public long WorkerCommands => Interlocked.Read(ref _workerCommands);

        /// <summary>Worker 执行形态下已完成的房间驱动轮次（观测用）。</summary>
        public long WorkerPumps => Interlocked.Read(ref _workerPumps);

        // ---- IRoomWorkerHost（P4：向 RoomWorkerDriver 提供的**窄端口**）----
        // 显式实现：驱动体够用即可，不把"内部计数怎么加"这类细节摊到本类公共面上。

        RoomWorkerPool IRoomWorkerHost.Pool => _workerPool;

        Ops IRoomWorkerHost.Ops => _ops;

        long IRoomWorkerHost.NowMs => _nowMs;

        string IRoomWorkerHost.BuildHash => ServerBuildHash;

        void IRoomWorkerHost.NoteWorkerCommand() => Interlocked.Increment(ref _workerCommands);

        void IRoomWorkerHost.NoteWorkerPump() => Interlocked.Increment(ref _workerPumps);

        void IRoomWorkerHost.NoteWorkerFailure() => Interlocked.Increment(ref _workerFailures);

        void IRoomWorkerHost.NoteMailboxStale() => Interlocked.Increment(ref _mailboxStale);

        /// <summary>是否启用了每房间 Mailbox 入站路由。</summary>
        public bool MailboxRoutingEnabled => _mailboxRouting;

        /// <summary>是否为 Worker 执行形态（房间命令与广播在归属 Worker 上执行）。</summary>
        public bool WorkerExecutionEnabled => _workerExecution;

        /// <summary>Mailbox 满载拒绝总数（各房间三 lane 合计）。</summary>
        public long MailboxRejectedFull => Interlocked.Read(ref _mailboxRejectedFull);

        /// <summary>Mailbox 已关闭后的入队拒绝总数。</summary>
        public long MailboxClosed => Interlocked.Read(ref _mailboxClosed);

        /// <summary>因连接会话已替换/失效而丢弃的迟到消息数。</summary>
        public long MailboxStale => Interlocked.Read(ref _mailboxStale);

        /// <summary>当前在册房间数（Ops/容量观测）。</summary>
        public int RoomCount
        {
            get { return _registry.Count; }
        }

        /// <summary>
        /// 房间明细快照（房间列表批——Lobby 心跳上报的"房间号/阶段/在座/期望"投影）。
        /// **近似快照**（与 OccupiedPlayerCount 同口径）：心跳线程读、权威线程写——字段级引用/int 读，
        /// 房间表 List 扩容瞬间的撕裂以 catch 兜底（下跳再报，投影本就是"上报事实"非权威）。
        /// **终态房间不报**（契约：LobbyContracts.RoomReport"终态房间（已销毁）不报"）——房间终态到
        /// <see cref="TryDestroyTerminalRoom"/> 销毁之间仍在册，不过滤会把 phase=6/7 送上上报面
        /// （Meta 相位闸对整次注册拒绝——一行终态房间毒死整条心跳）。
        /// O(在册房间)；低频心跳调用，不在权威循环热路径。
        /// </summary>
        public RoomSummary[] RoomSummaries()
        {
            RoomInstance[] rooms = _registry.SnapshotArray();   // 心跳线程读面：竞态兜底收在 registry 单点

            var list = new RoomSummary[rooms.Length];
            int n = 0;
            for (int i = 0; i < rooms.Length; i++)
            {
                RoomInstance room = rooms[i];
                if (room?.Runtime == null || room.Runtime.Closed) continue;   // 终态不报（判据 = 内核 IsTerminal；销毁在随后的 Pump）
                list[n++] = new RoomSummary
                {
                    RoomId = room.RoomId,
                    Phase = (int)room.Runtime.Match.Phase,
                    Players = room.Runtime.AllocatedSeats,   // 在册席位数（未开局离场即释放、不虚报；此前用高水位 NextPlayerId 会把已离场者计入）
                    ExpectedPlayers = room.Runtime.ExpectedPlayers,
                    MapId = room.Runtime.MapId,           // 多图批：房间列表可见性（上报事实，非判定来源）
                };
            }
            if (n != list.Length) Array.Resize(ref list, n);
            return list;
        }

        /// <summary>房间明细投影单元（Lobby 房间列表——上报事实，随心跳）。</summary>
        public struct RoomSummary
        {
            public string RoomId;
            public int Phase;
            public int Players;
            public int ExpectedPlayers;
            public int MapId;
        }

        /// <summary>
        /// 当前已进房玩家席位占用数（Lobby 容量上报面——未进房的连接不计入"占用"）。
        /// O(会话表)；低频心跳调用，不在权威循环热路径。
        ///
        /// **近似快照**（与 <see cref="RoomSummaries"/> 同族口径）：心跳线程读、权威线程增删会话——
        /// 枚举撞上会话表变更（InvalidOperationException）时**沿用上跳值**而非抛给心跳线程：
        /// 旧形态下该异常被 Lobby 客户端的 catch 吞掉，代价是**整跳心跳丢失**（连接风暴期恰最需要
        /// Lobby 可见性）。上跳值只由本读面写读（心跳线程），无并发写。
        /// </summary>
        public int OccupiedPlayerCount
        {
            get
            {
                try
                {
                    int count = 0;
                    foreach (Session session in _sessions.All())
                        if (session.PlayerId >= 0) count++;
                    _lastOccupiedPlayerCount = count;
                }
                catch (InvalidOperationException)
                {
                    // 会话表并发变更（连接/断线/清理与本读面同拍）：本跳沿用上跳值，下跳再刷新
                }
                return _lastOccupiedPlayerCount;
            }
        }

        private int _lastOccupiedPlayerCount;

        /// <summary>当前房间号快照（诊断用；顺序 = 插入序）。</summary>
        public string[] RoomIds
        {
            get
            {
                var ids = new string[_registry.Count];
                for (int i = 0; i < _registry.Count; i++) ids[i] = _registry.Order[i].RoomId;
                return ids;
            }
        }

        public Ops Ops => _ops;

        /// <summary>分层限流器（观测计数与测试断言入口）。</summary>
        public RateLimiter RateLimit => _rateLimit;

        /// <summary>入站准入门（观测/测试断言入口；批3 装配对账面：容器单例 ↔ 宿主持有同一实例）。</summary>
        public JoinAdmissionGate Admission => _admission;

        /// <summary>重连票据服务（观测/测试断言入口，同上）。</summary>
        public ReconnectService Reconnects => _reconnects;

        /// <summary>
        /// 建宿主。<paramref name="roomServerConfig"/> 给出房间容量上限、动态建房模板与
        /// Worker Pool 默认装配参数；传入 <paramref name="workerPool"/> 可在纯 .NET 用例中显式注入。
        ///
        /// **不传 <paramref name="roomServerConfig"/> 时**退回"单房间预置"形态：只按
        /// <paramref name="config"/> 建一个房间、容量 1、拒绝任何别的 roomId——这是嵌入式用法的
        /// 兼容通道，**不是**生产形态（生产走配置文件 + 动态建房）。
        /// 传入配置但未传 Worker Pool 时，宿主按配置创建并启动固定池；未传配置且未显式注入
        /// 时保持单循环形态（<see cref="WorkerPool"/> 为 null）。
        ///
        /// **Worker 执行形态**（<paramref name="workerExecution"/> = true，生产入口显式开启）：
        /// 房间命令与快照广播在归属 Worker 上执行，产出经 Outbound lane 回传宿主应用/发送。
        /// 该形态要求 <paramref name="mailboxRouting"/> = true（路由是 Worker 消费的前提）且
        /// 存在 Worker Pool；<paramref name="drainMailboxesImmediately"/> 必须为 false——
        /// 立即排空 = 宿主同栈执行房间命令，与"执行 owner 是 Worker"直接冲突，装载期显式拒绝。
        ///
        /// **协作对象装配口（批3，《服务端宿主装配收敛专项设计》§5）**：<paramref name="ops"/>/
        /// <paramref name="admission"/>/<paramref name="sessions"/>/<paramref name="reconnects"/>（与既有
        /// <paramref name="rateLimiter"/> 同款）是容器装配通道——生产经 <see cref="HostAssembly"/> 注册
        /// 并注入**同一单例**；不传（null）时按本类**单源构造公式**（internal static Create* 族）自建
        /// ——嵌入式/测试直接 new 的既有契约与行为不变。
        ///
        /// <paramref name="mapCatalog"/>（多图批，可空）：行键 → 判定图解析（生产链
        /// `HostAssembly.Inputs.MapCatalog`）；null = 单图形态（房间行键 &gt;0 时拒绝建房，见 <see cref="ResolveMap"/>）。
        /// </summary>
        public ServerHost(IRoomTransport transport, RoomConfig config = null,
            IJoinTicketValidator ticketValidator = null, string audience = null,
            RoomServerConfig roomServerConfig = null, ISettlementOutbox settlementOutbox = null,
            RoomWorkerPool workerPool = null, bool mailboxRouting = false,
            bool drainMailboxesImmediately = true, RateLimiter rateLimiter = null,
            bool workerExecution = false, CombatValues? combatValues = null, WeaponTable weapons = null,
            uint gameplayDigestBase = 0,
            Ops ops = null, JoinAdmissionGate admission = null, SessionManager sessions = null,
            ReconnectService reconnects = null, SimMapData map = null,
            Func<int, SimMapData> mapCatalog = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _serverConfig = roomServerConfig;
            _registry = new RoomRegistry(roomServerConfig?.MaxRooms ?? 1);   // 首个 AddRoom 之前就位
            _combat = combatValues ?? CombatValues.Default;
            _weapons = weapons ?? WeaponTable.Default;
            _map = map;
            _mapCatalog = mapCatalog;
            _gameplayDigestBase = gameplayDigestBase;
            Config = config ?? RoomConfig.Default();
            if (_map == null && Config.Map == null)
                throw new ArgumentException(
                    "地图必填（D22）：宿主 map 参数或 RoomConfig.Map 须注入其一——生产链经 ServerMapLoader 装载（无回退地图）",
                    nameof(map));
            _tickets = ticketValidator;
            _audience = ResolveAudience(audience, roomServerConfig);
            _settlementOutbox = settlementOutbox;
            // 运行期调参（P2 批二）：缺省 = HostTuning.Default = 迁移前的字面量常量。
            // 批3 上移到协作件构造之前（会话/重连的缺省构造读它）——此前位于首房间建立后，此前无读者。
            _tuning = ResolveTuning(roomServerConfig);
            _ops = ops ?? new Ops();
            _rateLimit = rateLimiter ?? CreateRateLimiter(roomServerConfig);
            // 准入门在限流器就位**之后**构造（它持限流器引用）——顺序不能颠倒；
            // 装配口（批3）：注入时直接采用容器单例，缺省通道经单源构造公式自建。
            _admission = admission ?? CreateAdmissionGate(_tickets, _rateLimit, transport, audience, roomServerConfig);
            _workerPool = workerPool ?? (_serverConfig != null
                ? new RoomWorkerPool(_serverConfig.WorkerCount, _serverConfig.MailboxCapacity, OnWorkerFailure)
                : null);
            // 驱动体在首个房间建立（AddRoom 会绑定 DriveAction）之前就位——它只在被调用时读端口
            _workerDriver = new RoomWorkerDriver(this);
            // 断线暂存队列：提交端口 = 控制命令是否成功入盒（满载暂存、FIFO、Mailbox 关闭丢弃——见该类注释）
            _pendingDisconnects = new PendingDisconnectQueue(
                (room, connectionId, session) => TrySubmitCommandTo(room, RoomCommand.Disconnect(connectionId), session));
            _mailboxRouting = mailboxRouting;
            _drainMailboxesImmediately = mailboxRouting && drainMailboxesImmediately;

            // Worker 执行形态的三条前置在装载期显式拒绝（不静默降级成宿主 owner 形态）：
            // 没有路由 = Worker 拿不到命令；没有池 = 没有执行线程；立即排空 = 要求宿主同栈执行。
            if (workerExecution && !_mailboxRouting)
                throw new ArgumentException("Worker 执行形态要求 mailboxRouting: true（入站命令需经每房间 Mailbox）",
                    nameof(workerExecution));
            if (workerExecution && _workerPool == null)
                throw new ArgumentException("Worker 执行形态需要 Worker Pool（配置形态自动创建，或显式注入）",
                    nameof(workerExecution));
            if (workerExecution && _drainMailboxesImmediately)
                throw new ArgumentException("Worker 执行形态下立即排空语义不成立（执行 owner 是 Worker）——请传 drainMailboxesImmediately: false",
                    nameof(drainMailboxesImmediately));
            _workerExecution = workerExecution;

            // 预置房间：
            // - 显式给了 config 且未给 roomServerConfig → 单房间兼容形态，预置该房间；
            // - 两者都没给 → 完全默认的单房间；
            // - 给了 roomServerConfig 且 config 为 null → **不预置**：所有房间首次进房时按模板创建。
            //   （动态建房下预置一个房间会白占一格容量，且"哪个 roomId 该被预置"本身没有依据。）
            RoomInstance first = null;
            if (config != null) first = AddRoom(config);
            else if (_serverConfig == null) first = AddRoom(RoomConfig.Default());

            // 会话/重连缺省构造：公式单源（ComputeSessionCapacity/Create* 族，批3 前置）——
            // 容器工厂与宿主缺省通道两处调用同一方法，防双源漂移。
            _sessions = sessions ?? CreateSessionManager(roomServerConfig, first);
            // 重连票据：TTL 与会话的"断线保留"同源（重连窗口内不清，窗口外必清）
            _reconnects = reconnects ?? CreateReconnectService(roomServerConfig);

            // 入站处理面：私有适配器把宿主的房间/诊断/时钟面收窄为端口；处理器在 Start 前就位。
            var inboundHost = new InboundHostAdapter(this);
            _inboundJoin = new InboundJoinHandlers(inboundHost);
            _inbound = new InboundHandlers(inboundHost, _inboundJoin, new InboundReconnectHandlers(inboundHost));

            _transport.OnConnected += OnTransportConnected;
            _transport.OnData += _inbound.OnData;
            _transport.OnDisconnected += OnTransportDisconnected;
            try
            {
                _workerPool?.Start();
                _transport.Start(_serverConfig != null ? _serverConfig.Port : Config.Port);   // Start 必须显式调用——不调用则服务器不监听
            }
            catch
            {
                // Transport 启动失败时回收已启动的池，避免 composition root 在构造异常后遗留后台线程。
                // 保留原始启动异常；清理本身失败不得遮蔽它。
                try { _workerPool?.Stop(drain: true); } catch { }
                try { _transport.Dispose(); } catch { }
                try { (_settlementOutbox as IDisposable)?.Dispose(); } catch { }
                throw;
            }
        }

        // ---- 协作对象单源构造公式（批3，《服务端宿主装配收敛专项设计》§5）----
        //
        // 宿主缺省通道（直接 new、可选口不传）与 HostAssembly 容器工厂共用同一组公式——
        // "工厂建的必须与宿主自建的等价"不靠注释约定，而是同一方法（修改指南 §1.2 单源红线）。

        private static HostTuning ResolveTuning(RoomServerConfig serverConfig) =>
            serverConfig?.HostTuning ?? HostTuning.Default;

        private static string ResolveAudience(string audience, RoomServerConfig serverConfig) =>
            audience ?? serverConfig?.Audience ?? string.Empty;

        /// <summary>
        /// 会话表容量按**全服潜在连接数**算（§9.2 会话容量上限）：
        /// 配了配置 → 房间上限 × 最大房间人数 ×4（按单房间算会让第二个房间的连接被会话上限拒掉）；
        /// 未配 → 首房间 ×4。
        /// **单一静态计算点**（批3 前置）：容器工厂与宿主缺省通道两处调用，防双源漂移。
        /// </summary>
        private static int ComputeSessionCapacity(RoomServerConfig serverConfig, RoomInstance firstRoom)
        {
            return serverConfig != null
                ? 4 * serverConfig.MaxRooms * serverConfig.MaxExpectedPlayers
                : 4 * (firstRoom != null ? firstRoom.Runtime.ExpectedPlayers : 2);
        }

        /// <summary>缺省限流器：配置 <c>rate_limit</c> 分区，缺则 <see cref="RateLimitSettings.Default"/>。</summary>
        internal static RateLimiter CreateRateLimiter(RoomServerConfig serverConfig) =>
            new RateLimiter(serverConfig?.RateLimit ?? RateLimitSettings.Default);

        /// <summary>缺省会话表：容量 = <see cref="ComputeSessionCapacity"/>；断线保留/静默 TTL 走调参。</summary>
        internal static SessionManager CreateSessionManager(RoomServerConfig serverConfig, RoomInstance firstRoom)
        {
            HostTuning tuning = ResolveTuning(serverConfig);
            return new SessionManager(ComputeSessionCapacity(serverConfig, firstRoom),
                disconnectedTtlMs: tuning.ReconnectTicketTtlMs,
                unseatedIdleTtlMs: tuning.SessionUnseatedIdleTtlMs);
        }

        /// <summary>缺省重连票据服务：容量与 TTL 走调参（重连窗口与会话"断线保留"同源）。</summary>
        internal static ReconnectService CreateReconnectService(RoomServerConfig serverConfig)
        {
            HostTuning tuning = ResolveTuning(serverConfig);
            return new ReconnectService(tuning.ReconnectCapacity, ttlMs: tuning.ReconnectTicketTtlMs);
        }

        /// <summary>
        /// 缺省入站准入门。远端地址适配（<see cref="TransportRemoteAddress"/>）在本方法内建——
        /// 它是装配期适配件，不进容器、不落运行期状态。
        /// </summary>
        internal static JoinAdmissionGate CreateAdmissionGate(IJoinTicketValidator tickets,
            RateLimiter rateLimit, IRoomTransport transport, string audience, RoomServerConfig serverConfig)
        {
            return new JoinAdmissionGate(tickets, rateLimit, new TransportRemoteAddress(transport),
                ServerBuildHash, ResolveAudience(audience, serverConfig));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_workerExecution)
            {
                // Worker 执行形态：先停 Worker（排空已接受的工作项——Worker 把房间 Mailbox 中
                // 已接受的 Control/Input 执行到空并入 Outbound），再由宿主统一收口：
                // Worker 停止后宿主重新是唯一执行者，Complete 并排空残留（含 Outbound 回传应用）
                // 与宿主 owner 形态的编号（§12 第 5 步：Worker → Transport → Outbox）一致。
                try
                {
                    _workerPool?.Stop(drain: true);
                }
                finally
                {
                    CompleteAndDrainAllRooms();
                    try
                    {
                        _transport.Dispose();
                    }
                    finally
                    {
                        (_settlementOutbox as IDisposable)?.Dispose();   // 宿主接管（同 transport 所有权约定）
                    }
                }
                return;
            }

            // Mailbox 路由阶段先停止接收并由 Host owner 排空，再按 Worker → Transport → Outbox
            // 逆序释放；这样 drain=false 的测试/嵌入式形态也不会在 Transport 已释放后残留出站。
            if (_mailboxRouting)
                CompleteAndDrainAllRooms();
            try
            {
                _workerPool?.Stop(drain: true);
            }
            finally
            {
                try
                {
                    _transport.Dispose();
                }
                finally
                {
                    (_settlementOutbox as IDisposable)?.Dispose();   // 宿主接管（同 transport 所有权约定）
                }
            }
        }

        /// <summary>Complete 所有房间 Mailbox 并由宿主排空（含 Outbound 回传应用）。</summary>
        private void CompleteAndDrainAllRooms()
        {
            for (int i = 0; i < _registry.Count; i++) _registry.Order[i].Mailbox.Complete();
            for (int i = 0; i < _registry.Count; i++) DrainRoomMailbox(_registry.Order[i]);
        }

        private void OnWorkerFailure(Exception ex)
        {
            Interlocked.Increment(ref _workerFailures);
            HostLog.Error("Worker", "工作项异常已隔离：" + ex.GetType().Name);
        }

        private long NowMs() => _clock.ElapsedMilliseconds;

        /// <summary>进入一包处理：刷新宿主单调时钟并返回本跳毫秒（入站分发首步）。</summary>
        private long BeginInbound()
        {
            _nowMs = NowMs();
            return _nowMs;
        }

        private void OnTransportConnected(int connectionId)
        {
            _nowMs = NowMs();

            // 分层限流·IP 维度·连接：kcp2k 握手后才会触发本回调，
            // 远端地址此时已可达（KcpServer 先登记连接再回调）。地址探测不到 → 放行（限不了看不见的地址）。
            if (!_rateLimit.TryAcquireIpConnect(_transport.GetRemoteAddress(connectionId), _nowMs))
            {
                _transport.Disconnect(connectionId);
                return;
            }

            if (!_sessions.TryAddNew(connectionId, _nowMs, out _))
            {
                // 会话容量上限（§9.2）：超限不登记并断开——未认证连接不能把记账表撑成无界内存
                _ops.NoteSessionRejected();
                _transport.Disconnect(connectionId);
            }
        }

        private void OnTransportDisconnected(int connectionId)
        {
            _nowMs = NowMs();
            string roomId = null;
            Session disconnectedSession = null;
            if (_sessions.TryGet(connectionId, out Session session))
            {
                session.Disconnected = true;   // 掉线不停帧（§4.5-2）：标记 + 席位保留（重连窗口内可重绑）
                roomId = session.RoomId;
                disconnectedSession = session;
            }
            // 按会话所在房间通报（§6 路由）；未进房的连接没有房间，通报无对象——幂等忽略。
            RoomInstance room = GetRoomInstance(roomId);
            if (room == null) return;
            RoomCommand command = RoomCommand.Disconnect(connectionId);
            if (!TrySubmitCommandTo(room, command, disconnectedSession) && _mailboxRouting)
            {
                // 断线是控制面事实：若同一连接已有排队 Join/Rebind，先清理其迟到命令，
                // 再重试入盒，避免直接旁路打破 Control FIFO。
                room.Mailbox.DiscardControls(e => e.ConnectionId == connectionId);
                if (!TrySubmitCommandTo(room, command, disconnectedSession))
                    _pendingDisconnects.Enqueue(room, disconnectedSession);
            }
        }

        /// <summary>某拒绝分类的累计次数（Ops 观测；计数在 Join 入站族）。</summary>
        public int TicketRejections(JoinTicketRejection reason) => _inboundJoin.TicketRejections(reason);

        private void SendToSession(Session session, PacketType type, IMessage message, bool reliable)
        {
            _transport.SendTo(session.ConnectionId, new ArraySegment<byte>(PacketCodec.Encode(type, message)), reliable);
        }

        /// <summary>
        /// 进房拒绝的**诊断单一落点**（《框架先行》§8「可诊断」）：结构化记录（stage/code/关联键）→ 计数与日志。
        /// 关联键与客户端 <c>RoomClient</c> 的进房记录**同形同键**（<see cref="DiagTrace.JoinKey"/> 单源构造）——
        /// 一次注入失败即可按 key 把两端与各域记录对齐，<paramref name="stage"/> 直接指出失败段落（Build/Room/…）。
        /// 拒绝原因进 detail；**不回显 token/payload**（P0-3 与《Meta 专项》§13.1 不变）。
        /// </summary>
        private void RejectJoin(Session session, in Proto.JoinRequest join, string stage, string code, string reason)
        {
            DiagTrace.Emit(stage, code, DiagTrace.JoinKey(join.RoomId, session.PlayerId, join.BuildHash), reason);
            Reject(session, reason);
        }

        private void Reject(Session session, string reason)
        {
            _ops.NoteReject();
            HostLog.Warn("Reject", $"conn {session.ConnectionId}: {reason}");   // 只打原因，不回显 token/payload（P0-3）
        }

        /// <summary>
        /// 当前 Host owner 对已消费命令执行 Runtime 并应用输出；入站命令的 Mailbox admission
        /// 与消费由上层路由负责。
        /// <paramref name="joinContext"/> = 触发本次命令的进房连接（PlayerAdmitted 落位用）。
        /// </summary>
        private void SubmitCommand(in RoomCommand cmd, Session joinContext = null)
        {
            RoomInstance room = CurrentOrFirstRoom;
            if (room == null) return;
            _outputs.Clear();
            room.Runtime.Execute(cmd, _outputs);
            for (int i = 0; i < _outputs.Count; i++) ApplyOutput(_outputs[i], joinContext);
            _outputs.Clear();
        }

        /// <summary>
        /// 把命令提交到指定房间。Mailbox 路由阶段仍由当前 Host owner 消费；
        /// <paramref name="joinContext"/> 不跨队列保存，Join 输出应用时按连接代次重新解析。
        /// </summary>
        private bool TrySubmitCommandTo(RoomInstance room, in RoomCommand cmd, Session joinContext = null)
        {
            if (room == null) return false;

            if (_mailboxRouting)
            {
                int connectionId = cmd.ConnectionId;
                int playerId = cmd.PlayerId;
                long epoch = 0;
                if (joinContext != null)
                {
                    if (cmd.Kind == RoomCommandKind.Rebind)
                        connectionId = cmd.NewConnectionId;
                    else
                        connectionId = joinContext.ConnectionId;
                    if (cmd.Kind != RoomCommandKind.Rebind && cmd.Kind != RoomCommandKind.RestoreAck)
                        playerId = joinContext.PlayerId;
                    epoch = joinContext.Epoch;
                }
                else if (connectionId != RoomOutboundEnvelope.NoConnectionId && _sessions.TryGet(connectionId, out Session current))
                {
                    epoch = current.Epoch;
                    if (playerId < 0) playerId = current.PlayerId;
                }

                RoomMailboxEnqueueResult result;
                if (cmd.Kind == RoomCommandKind.ClientInput)
                {
                    RoomInputEnvelope envelope = RoomInputEnvelope.FromCommand(
                        room.RoomId, connectionId, playerId, epoch, _nowMs, cmd);
                    result = room.Mailbox.EnqueueInput(envelope);
                }
                else
                {
                    RoomControlEnvelope envelope = RoomControlEnvelope.FromCommand(
                        room.RoomId, connectionId, playerId, epoch, _nowMs, cmd);
                    result = room.Mailbox.EnqueueControl(envelope);
                }

                if (result != RoomMailboxEnqueueResult.Accepted)
                {
                    if (result == RoomMailboxEnqueueResult.RejectedFull)
                        Interlocked.Increment(ref _mailboxRejectedFull);
                    else
                        Interlocked.Increment(ref _mailboxClosed);
                    return false;
                }

                if (_workerExecution)
                    ScheduleRoomPump(room);          // 唤醒归属 Worker（合并登记：每房间至多一个在途驱动）
                else if (_drainMailboxesImmediately)
                    DrainRoomMailbox(room);
                return true;
            }

            ExecuteCommandTo(room, cmd, joinContext);
            return true;
        }

        /// <summary>控制命令的提交入口；路由失败由对应 admission 处理。</summary>
        private void SubmitCommandTo(RoomInstance room, in RoomCommand cmd, Session joinContext = null)
        {
            if (!TrySubmitCommandTo(room, cmd, joinContext)
                && _mailboxRouting && cmd.Kind != RoomCommandKind.ClientInput)
            {
                // 控制命令不能静默丢失；当前仍由同一 Host owner 执行，故在
                // Mailbox 关闭/满载时保留一个显式旁路兜底。入站 Join/Reconnect
                // 使用各自 admission 分支，不经过这里。
                ExecuteCommandTo(room, cmd, joinContext);
            }
        }

        private void ExecuteCommandTo(RoomInstance room, in RoomCommand cmd, Session joinContext = null)
        {
            if (room == null) return;
            RoomInstance saved = _currentRoom;
            _currentRoom = room;
            try
            {
                SubmitCommand(cmd, joinContext);
            }
            finally
            {
                _currentRoom = saved;
            }
        }

        /// <summary>
        /// Host owner 对单房间 Mailbox 的消费顺序：Control → Input → Outbound。
        /// Outbound 是 Worker 回传的应用通道（宿主 owner 形态下为空；Dispose 收口与
        /// Worker 执行形态的回传都走同一应用路径）。
        /// </summary>
        private void DrainRoomMailbox(RoomInstance room)
        {
            if (room == null) return;
            RoomMailboxMessage<RoomInputEnvelope, RoomControlEnvelope, RoomOutboundEnvelope> message;
            while (room.Mailbox.TryDequeue(out message))
            {
                switch (message.Lane)
                {
                    case RoomMailboxLane.Control:
                        DrainControlEnvelope(room, message.Control);
                        break;
                    case RoomMailboxLane.Input:
                        DrainInputEnvelope(room, message.Input);
                        break;
                    case RoomMailboxLane.Outbound:
                        ApplyOutboundEnvelope(room, message.Outbound);
                        break;
                }
            }
        }

        /// <summary>只排空 Control lane，用于在重试暂存的断线事实前保留跨 lane 优先级。</summary>
        private void DrainRoomControlMailbox(RoomInstance room)
        {
            if (room == null) return;
            while (room.Mailbox.TryDequeueControl(out RoomControlEnvelope envelope))
                DrainControlEnvelope(room, envelope);
        }

        /// <summary>只排空 Outbound lane（Worker 回传；Control/Input 归 Worker，宿主不消费）。</summary>
        private void DrainRoomOutbound(RoomInstance room)
        {
            if (room == null) return;
            while (room.Mailbox.TryDequeueOutbound(out RoomOutboundEnvelope envelope))
                ApplyOutboundEnvelope(room, envelope);
        }

        private void DrainControlEnvelope(RoomInstance room, RoomControlEnvelope envelope)
        {
            if (!IsCurrentEnvelope(envelope.RoomId, envelope.ConnectionId,
                envelope.PlayerId, envelope.SessionEpoch, envelope.Command.Kind))
            {
                if (envelope.Command.Kind == RoomCommandKind.AuthenticatedJoin
                    && _sessions.TryGet(envelope.ConnectionId, out Session staleJoin)
                    && staleJoin.Epoch == envelope.SessionEpoch)
                {
                    staleJoin.JoinPending = false;
                    staleJoin.Principal = null;
                    staleJoin.BuildHash = null;
                }
                RemoveEmptyRoom(room);
                Interlocked.Increment(ref _mailboxStale);
                return;
            }

            Session context = ResolveEnvelopeSession(envelope.ConnectionId, envelope.SessionEpoch);
            ExecuteCommandTo(room, envelope.Command, context);
        }

        private void DrainInputEnvelope(RoomInstance room, RoomInputEnvelope envelope)
        {
            if (!IsCurrentEnvelope(envelope.RoomId, envelope.ConnectionId,
                envelope.PlayerId, envelope.SessionEpoch, envelope.Command.Kind))
            {
                Interlocked.Increment(ref _mailboxStale);
                return;
            }
            if (!_sessions.TryGet(envelope.ConnectionId, out Session session)
                || session.PlayerId != envelope.PlayerId
                || session.Disconnected)
            {
                Interlocked.Increment(ref _mailboxStale);
                return;
            }

            long acceptedBefore = room.Runtime.Gate.AcceptedCount;
            ExecuteCommandTo(room, envelope.Command);
            _ops.CountAck(session.TryAcceptAck(envelope.Command.Input.AckSnapshot));
            if (room.Runtime.Gate.AcceptedCount > acceptedBefore)
                _ops.CountAcceptedInput();
        }

        private bool IsCurrentEnvelope(string roomId, int connectionId, int playerId,
            long epoch, RoomCommandKind kind)
        {
            // 连接号是传输层原值（kcp2k 随机 int，可为负）——"无连接"用相等比较判定，不用符号。
            if (connectionId == RoomOutboundEnvelope.NoConnectionId || epoch == 0) return true;
            if (!_sessions.TryGet(connectionId, out Session current) || current.Epoch != epoch)
                return false;
            switch (kind)
            {
                case RoomCommandKind.AuthenticatedJoin:
                    return current.RoomId == null && current.JoinPending && !current.Disconnected;
                case RoomCommandKind.Disconnect:
                    // Disconnect is deliberately marked on Session before it enters the
                    // mailbox; the disconnected bit is the fact being delivered here.
                    return string.Equals(roomId, current.RoomId, StringComparison.Ordinal);
                case RoomCommandKind.Rebind:
                    return !current.Disconnected
                        && (current.RoomId == null
                            || string.Equals(roomId, current.RoomId, StringComparison.Ordinal))
                        && (current.PlayerId < 0 || current.PlayerId == playerId);
                case RoomCommandKind.RestoreAck:
                    return !current.Disconnected
                        && current.PlayerId == playerId
                        && string.Equals(roomId, current.RoomId, StringComparison.Ordinal);
                default:
                    return !current.Disconnected
                        && string.Equals(roomId, current.RoomId, StringComparison.Ordinal);
            }
        }

        private Session ResolveEnvelopeSession(int connectionId, long epoch)
        {
            if (connectionId == RoomOutboundEnvelope.NoConnectionId
                || !_sessions.TryGet(connectionId, out Session session)) return null;
            return epoch == 0 || session.Epoch == epoch ? session : null;
        }

        // ================== Worker 执行形态（§8.2）：房间命令与广播在归属 Worker 上执行 ==================

        /// <summary>
        /// 唤醒房间归属 Worker（合并登记：每房间至多一个在途驱动工作项）。
        /// 生产者协议 = 先入盒、再登记；Worker 侧按"清标记 → 消费 → 复检后重登记"配对
        /// （见 <see cref="DriveRoomWorker"/>），保证不丢唤醒、也不堆积工作项。
        /// </summary>
        private void ScheduleRoomPump(RoomInstance room)
        {
            if (room == null || _workerPool == null || room.DriveAction == null) return;
            if (!room.TrySchedulePump()) return;               // 已有在途驱动
            if (_workerPool.TryEnqueue(room.RoomId, room.DriveAction)) return;
            room.ClearPumpScheduled();                         // 池已停/队列满：留给下一次命令再唤醒
            Interlocked.Increment(ref _workerFailures);
        }

        /// <summary>
        /// Worker 执行形态的 Tick 投递（合并节流：至多一条在途）。
        /// 不节流会让 Worker 落后时 Tick 堆积——后续输入会因"服务器已推进帧"被判过期而整批丢弃。
        /// </summary>
        private void SubmitWorkerTick(RoomInstance room)
        {
            if (!room.TryQueueTick())
            {
                _ops.NoteTickCoalesced();
                return;
            }
            if (!TrySubmitCommandTo(room, RoomCommand.Tick(_nowMs)))
                room.ClearPendingTick();
        }

        // **Worker 驱动体外提（P4）**：驱动核心、命令执行（含重连换绑/输入）、席位侧效应与回传出盒
        // 已整体搬到 `RoomWorkerDriver.cs`（按线程归属切分：宿主只留**生产侧** ScheduleRoomPump/
        // SubmitWorkerTick 与**回传应用侧** ApplyOutboundEnvelope 族）。调用点两处：
        // 建房时 BindDriveAction 绑 `_workerDriver.Drive`，管线 SendTo 绑 `_workerDriver.Send`。

        /// <summary>应用一条回传事实（宿主主线程；<c>_currentRoom</c> 指向该房间供既有应用路径定位）。</summary>
        private void ApplyOutboundEnvelope(RoomInstance room, in RoomOutboundEnvelope envelope)
        {
            RoomInstance saved = _currentRoom;
            _currentRoom = room;
            try
            {
                switch (envelope.Item)
                {
                    case RoomOutputItem outputItem:
                        ApplyWorkerOutput(envelope, outputItem.Output);
                        break;
                    case RoomSendItem sendItem:
                        ApplyWorkerSend(sendItem);
                        break;
                    case RoomSeatReboundItem reboundItem:
                        ApplyWorkerSeatRebound(room, envelope, reboundItem);
                        break;
                }
            }
            finally
            {
                _currentRoom = saved;
            }
        }

        /// <summary>
        /// 应用回传纯输出。<see cref="PlayerAdmitted"/> 特判：进房会话必须解析成功、仍在 JoinPending
        /// 且**未断线**——否则是"座位已占但发起连接已不在/已断"的迟到 Join（释放孤儿席位并尝试回滚
        /// 动态预留）。断线面必须在此拦截：在途 Join 期间传输断线时，会话 RoomId 尚未绑定
        /// （本回传应用才绑定），<see cref="OnTransportDisconnected"/> 无处路由断线命令——
        /// 不拦截则该席位永无 Disconnect 到达，Runtime 视为在线（未开局房永驻、对局中全员离场收尾失效）。
        /// </summary>
        private void ApplyWorkerOutput(in RoomOutboundEnvelope envelope, RoomOutput output)
        {
            if (output is SignalOutput signal && signal.Signal is PlayerAdmitted)
            {
                Session joinSession = ResolveOriginSession(envelope);
                if (joinSession == null || !joinSession.JoinPending || joinSession.Disconnected)
                {
                    if (joinSession != null && joinSession.Disconnected)
                    {
                        // 与 Control lane 迟到 Join 的清场同款（DrainControlEnvelope）：断线会话的
                        // JoinPending 若不清，会拖住 AnyJoinPending 扫描进而延迟其他房间的动态预留回滚。
                        joinSession.JoinPending = false;
                        joinSession.Principal = null;
                        joinSession.BuildHash = null;
                    }
                    ApplyStaleWorkerJoin(_currentRoom, envelope.OriginConnectionId);
                    return;
                }
                ApplySignal(signal, joinSession);
                return;
            }
            ApplyOutput(output, ResolveOriginSession(envelope));
        }

        /// <summary>回传信封的来源会话解析（代次不符/已消失 → null）。</summary>
        private Session ResolveOriginSession(in RoomOutboundEnvelope envelope)
        {
            if (envelope.OriginConnectionId == RoomOutboundEnvelope.NoOrigin) return null;
            if (!_sessions.TryGet(envelope.OriginConnectionId, out Session session)) return null;
            if (envelope.OriginSessionEpoch != 0 && session.Epoch != envelope.OriginSessionEpoch) return null;
            return session;
        }

        /// <summary>应用回传发送：按连接（+代次）解析会话，编码并经 Transport 发送。</summary>
        private void ApplyWorkerSend(RoomSendItem send)
        {
            if (!_sessions.TryGet(send.ConnectionId, out Session session)) return;
            if (send.SessionEpoch != 0 && session.Epoch != send.SessionEpoch) return;   // 连接复用后的迟到帧
            if (session.Disconnected) return;                                           // 宿主已知断线：不发送
            _transport.SendTo(send.ConnectionId,
                new ArraySegment<byte>(PacketCodec.Encode(send.Type, send.Message)), send.Reliable);
        }

        /// <summary>
        /// 应用重连落定：新连接接管席位（会话映射更新），旧连接失效；计 Ops。
        /// 来源会话解析失败（新连接已消失）时仍让旧连接失效——后续 Disconnect 命令会收尾。
        /// </summary>
        private void ApplyWorkerSeatRebound(RoomInstance room, in RoomOutboundEnvelope envelope, RoomSeatReboundItem rebound)
        {
            if (rebound.OldConnectionId != RoomOutboundEnvelope.NoConnectionId
                && rebound.OldConnectionId != rebound.NewConnectionId
                && _sessions.TryGet(rebound.OldConnectionId, out Session old))
                old.Disconnected = true;

            Session session = ResolveOriginSession(envelope);
            if (session == null)
            {
                Interlocked.Increment(ref _mailboxStale);
                return;
            }
            session.PlayerId = rebound.PlayerId;
            session.RoomId = room.RoomId;
            room.Seats[rebound.PlayerId] = session;
            _ops.NoteReconnectServed();
        }

        /// <summary>
        /// 迟到 Join 的回传落定：座位在 Runtime 已占，但发起连接已不在（断线/被替换）。
        /// 补一条 Disconnect 命令释放孤儿席位——断线通报按会话 RoomId 路由，而此刻会话尚未绑定房间，
        /// 不补发则该席位无人回收（房间永远开不了局）；动态预留的回滚留待后续 Pump 复检。
        /// </summary>
        private void ApplyStaleWorkerJoin(RoomInstance room, int originConnectionId)
        {
            Interlocked.Increment(ref _mailboxStale);
            if (room == null) return;
            room.MarkStaleJoinRollback();
            if (originConnectionId != RoomOutboundEnvelope.NoOrigin)
                TrySubmitCommandTo(room, RoomCommand.Disconnect(originConnectionId));
        }

        /// <summary>是否存在尚未由 Runtime 消费的 Join（保守判据：回滚动态预留的安全前提）。</summary>
        private bool AnyJoinPending()
        {
            foreach (Session s in _sessions.All())
                if (s.JoinPending) return true;
            return false;
        }

        /// <summary>
        /// 回收迟到 Join 留下的动态预留：房间仍是"动态 + 未开局 + 宿主席位全空 + 无待消费命令"
        /// 且全服没有待验收 Join 时移除（与 RemoveEmptyRoom 同语义，额外要求 StaleJoinRollbackPending）。
        /// 保守的 JoinPending 全局扫描保证不会误删"有 Join 在途"的房间。
        /// </summary>
        private bool TryRollbackStaleReservation(RoomInstance room)
        {
            if (room == null || !room.StaleJoinRollbackPending) return false;
            if (!room.IsDynamic || room.Runtime.Started
                || room.Mailbox.ControlCount != 0 || room.Mailbox.InputCount != 0
                || AnyJoinPending())
                return false;
            for (int i = 0; i < room.Seats.Length; i++)
                if (room.Seats[i] != null) return false;
            if (!_registry.IsRegistered(room)) return false;

            _registry.Remove(room);
            room.Mailbox.Complete();
            while (room.Mailbox.TryDequeue(out _)) { }
            room.ClearStaleJoinRollback();
            return true;
        }

        private void ApplyOutput(RoomOutput output, Session joinContext)
        {
            switch (output)
            {
                case SignalOutput so:
                    ApplySignal(so, joinContext);
                    break;
                case StartRejectedOutput sr:                         // 开房裁决：提前开局被拒 → 回发起者信令
                {
                    RoomInstance room = CurrentOrFirstRoom;
                    Session starter = room != null && sr.PlayerId >= 0 && sr.PlayerId < room.Seats.Length
                        ? room.Seats[sr.PlayerId]
                        : null;
                    if (starter != null && !starter.Disconnected)
                        SendToSession(starter, PacketType.StartMatchResponse,
                            new Proto.StartMatchResponse { Ok = false, Reason = sr.Reason }, reliable: true);
                    break;
                }
                case JoinRejectedOutput jr:
                    RoomInstance rejectedRoom = _currentRoom;
                    if (_sessions.TryGet(jr.ConnectionId, out Session rejectedSession))
                    {
                        // 房间侧拒绝（房间运行时的判定）——与宿主侧同键记录对齐（§8「可诊断」）
                        DiagTrace.Emit(DiagStage.Room, DiagCode.JoinRejectedRoom,
                            DiagTrace.JoinKey(rejectedRoom?.RoomId ?? rejectedSession.RoomId,
                                rejectedSession.PlayerId, rejectedSession.BuildHash),
                            jr.Reason);
                        rejectedSession.JoinPending = false;
                        if (rejectedSession.PlayerId < 0)
                        {
                            rejectedSession.Principal = null;
                            rejectedSession.BuildHash = null;
                        }
                    }
                    _ops.NoteReject();
                    HostLog.Warn("Reject", $"conn {jr.ConnectionId}: {jr.Reason}");   // 只打原因（P0-3）
                    // 动态房间是在 Join admission 时预留的；若 Runtime 在消费
                    // 命令时拒绝（例如房间已在排空/终态），释放仍为空的预留，
                    // 避免无效 roomId 占满 MaxRooms。已有状态或非动态房间不会被回收。
                    RemoveEmptyRoom(rejectedRoom);
                    break;
                case MatchStateChangedOutput mcs:
                    // §9.1/§13.1 结构化事件：低频迁移只在日志记录，不进指标高基数标签
                    _ops.NoteMatchStateChanged();
                    HostLog.Info("Match", $"room={mcs.MatchId} {mcs.From}->{mcs.To} reason={mcs.Reason} frame={mcs.Frame} nowMs={mcs.NowMs}");
                    break;
                case RoomAbortedOutput ra:
                {
                    // 终态中止的客户端信令（复用 MatchEnded——客户端既有终局链路；无结果可结算，Players 空）。
                    // 触发面：等待超时/运维关闭/排空超时等**未开局即收尾**路径——此前在座客户端收不到
                    // 任何信令，房间销毁后停在房间页无反馈。对局中收尾经 SettlementReady → MatchEnded
                    // （带结果），不经此。
                    RoomInstance room = CurrentOrFirstRoom;
                    if (room == null) break;
                    var aborted = new Proto.MatchEnded
                    {
                        MatchId = ra.MatchId ?? "",
                        FinalFrame = ra.Frame,
                        EndReason = (int)ra.Reason,
                        GameplayEndReason = (int)MatchEndReason.None,
                        WinnerEntityId = 0L,
                    };
                    for (int p = 0; p < room.Seats.Length; p++)
                    {
                        Session member = room.Seats[p];
                        if (member == null || member.Disconnected) continue;
                        SendToSession(member, PacketType.MatchEnded, aborted, reliable: true);
                    }
                    HostLog.Info("Match", $"房间中止信令已发：room={ra.MatchId} reason={ra.Reason} frame={ra.Frame}");
                    break;
                }
                case SettlementReadyOutput sr:
                    _ops.NoteSettlementReady();
                    SendMatchResult(sr.Summary);
                    HostLog.Info("Settle", $"room={sr.Summary.MatchId} seed={sr.Summary.Seed} finalFrame={sr.Summary.FinalFrame} end={sr.Summary.EndReason} seats={sr.Summary.SeatPlayerIds.Length}");
                    // §11.3"本地持久 Outbox"：冻结事实入盒（幂等/有界/失败显式计数——不抛进权威循环，
                    // §6"持久化写入不得阻塞/炸掉 Room Worker"）。null = 未装配。
                    if (_settlementOutbox != null)
                    {
                        // 席位账号映射（《上云测试》裁决点①）：从当前房间席位会话取得、随行持久化——
                        // 提交管道重启续投不丢账号归属；取不到（旧会话/未认证）→ 空串（Meta 如实拒绝）。
                        string[] seatAccountIds = BuildSeatAccountIds(_currentRoom, sr.Summary);
                        switch (_settlementOutbox.Enqueue(sr.Summary, seatAccountIds))
                        {
                            case SettlementOutboxResult.Appended:
                                _ops.NoteSettlementJournaled();
                                break;
                            case SettlementOutboxResult.Duplicate:
                                _ops.NoteSettlementOutboxDuplicate();
                                break;
                            case SettlementOutboxResult.RejectedFull:
                                _ops.NoteSettlementOutboxRejected();
                                HostLog.Warn("Settle", $"Outbox 容量满拒（计数不丢）：{sr.Summary.MatchId}");
                                break;
                            default:
                                _ops.NoteSettlementOutboxFailed();
                                HostLog.Error("Settle", $"Outbox 写入失败（介质故障，计数不丢）：{sr.Summary.MatchId}");
                                break;
                        }
                    }
                    break;
            }
        }

        private void SendMatchResult(MatchResultSummary summary)
        {
            RoomInstance room = CurrentOrFirstRoom;
            if (room == null) return;
            var message = new Proto.MatchEnded
            {
                MatchId = summary.MatchId, FinalFrame = summary.FinalFrame,
                EndReason = (int)summary.EndReason, GameplayEndReason = (int)summary.GameplayEndReason,
                WinnerEntityId = summary.WinnerEntityId,
            };
            foreach (RoomServer.Runtime.PlayerMatchResult result in summary.Players)
                message.Players.Add(new Proto.PlayerMatchResult { PlayerId = result.PlayerId,
                    EntityId = result.EntityId, Kills = result.Kills, Deaths = result.Deaths });
            foreach (Session member in room.Seats)
                if (member != null && !member.Disconnected)
                    SendToSession(member, PacketType.MatchEnded, message, reliable: true);
        }

        /// <summary>
        /// 席位账号映射（《上云测试》裁决点① "结算载荷补 SeatAccountIds"）：与
        /// <see cref="MatchResultSummary.Players"/> **同序等长**（按席位 playerId 查会话）；
        /// 查不到（座位空/会话已清）→ 空串——各端按同一序对齐，Meta 侧缺账号如实拒绝。
        /// </summary>
        private static string[] BuildSeatAccountIds(RoomInstance room, MatchResultSummary summary)
        {
            RoomServer.Runtime.PlayerMatchResult[] players = summary.Players;
            var ids = new string[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                int playerId = players[i].PlayerId;
                Session seat = null;
                if (room != null && playerId >= 0 && playerId < room.Seats.Length)
                    seat = room.Seats[playerId];
                ids[i] = seat?.Principal?.AccountId ?? string.Empty;
            }
            return ids;
        }

        /// <summary>席位显示名占位前缀（房间页玩家列表"玩家{k}"；与客户端局内 PlayerIdentity 同口径，
        /// G3 Profile 接真昵称时两处来源一并更替）。</summary>
        private const string SeatDisplayNamePrefix = "玩家";

        private void ApplySignal(SignalOutput so, Session joinContext)
        {
            switch (so.Signal)
            {
                case PlayerAdmitted pa:
                {
                    RoomInstance room = CurrentOrFirstRoom;
                    if (room == null) break;
                    Session session = joinContext ?? (pa.PlayerId < room.Seats.Length ? room.Seats[pa.PlayerId] : null);
                    if (session == null) break;
                    session.JoinPending = false;
                    session.PlayerId = pa.PlayerId;
                    session.RoomId = room.RoomId;          // 多房间路由键（输入/重连/断线按它找房间）
                    room.Seats[pa.PlayerId] = session;

                    // E3 重连票据（一次性）：进房成功才发——重连时凭它换权威快照（§5.6 首选路径的服务器侧能力）
                    string ticket = _reconnects.Issue(pa.PlayerId, room.RoomId);
                    // 回执 proto 单源（HandshakePacker，与本地服共一份——凭据/房事实经参数注入，字段面不可能再两端漂移）
                    SendToSession(session, PacketType.JoinAck, HandshakePacker.BuildJoinAck(pa, ticket,
                        (int)(_reconnects.TtlMs / 1000), room.Runtime?.MapId ?? 0,
                        room.Runtime?.ExpectedPlayers ?? 0), reliable: true);
                    break;
                }
                case MatchStarted ms:
                {
                    RoomInstance room = CurrentOrFirstRoom;
                    if (room == null) break;
                    for (int p = 0; p < room.Seats.Length; p++)
                    {
                        Session member = room.Seats[p];
                        if (member == null || member.Disconnected) continue;
                        SendToSession(member, PacketType.StartGame, HandshakePacker.BuildStartGame(ms), reliable: true);
                    }
                    break;
                }
                case SeatRestored:
                {
                    _ops.NoteRestoreCompleted();              // §9.3 步骤 6：席位回 Active——增量广播恢复
                    RoomInstance room = CurrentOrFirstRoom;
                    room?.Pipeline.RequestFullSnapshot();  // 重锚广播链：抑制期间基线已越过重连快照帧，整帧全量补齐 (N_r, B] 缝
                    break;
                }
                case RoomServer.Runtime.OwnerChanged oc:                  // 房主迁移：可靠广播——UI 换"开始对局"按钮归属
                {                                                        // （限定信号类型——与 Proto.OwnerChanged 同名二义）
                    RoomInstance room = CurrentOrFirstRoom;
                    if (room == null) break;
                    for (int p = 0; p < room.Seats.Length; p++)
                    {
                        Session member = room.Seats[p];
                        if (member == null || member.Disconnected) continue;
                        SendToSession(member, PacketType.OwnerChanged,
                            new Proto.OwnerChanged { PlayerId = oc.PlayerId }, reliable: true);
                    }
                    break;
                }
                case SeatsChanged sc:                                     // 席位实时面：可靠广播——房间页玩家列表全量快照
                {
                    RoomInstance room = CurrentOrFirstRoom;
                    if (room == null) break;

                    // 席位释放同步（未开局离场即释放——内核已摘席）：宿主侧会话槽一并清空，
                    // 并复位该会话的路由字段（与 TryDestroyTerminalRoom 同款）——不复位则幽灵会话
                    // 持续计入 OccupiedPlayerCount。对局中断线席位仍在列表（Connected=false），
                    // 不受影响；同批进房的 PlayerAdmitted 先于本信号应用（lane FIFO）。
                    for (int p = 0; p < room.Seats.Length; p++)
                    {
                        Session released = room.Seats[p];
                        if (released == null) continue;
                        bool present = false;
                        for (int i = 0; i < sc.Seats.Length; i++)
                            if (sc.Seats[i].PlayerId == p) { present = true; break; }
                        if (present) continue;
                        room.Seats[p] = null;
                        if (string.Equals(released.RoomId, room.RoomId, StringComparison.Ordinal)) released.RoomId = null;
                        if (released.PlayerId == p)
                        {
                            released.PlayerId = -1;
                            released.Principal = null;
                            released.BuildHash = null;
                        }
                    }

                    var seats = new Proto.RoomSeats();
                    for (int i = 0; i < sc.Seats.Length; i++)
                    {
                        seats.Seats.Add(new Proto.SeatInfo
                        {
                            PlayerId = sc.Seats[i].PlayerId,
                            // 占位名（与客户端局内 PlayerIdentity.PlaceholderNamePrefix 同口径：玩家{k}，k=席序+1
                            //——完整房间下席序=实体序；G3 Profile 接真昵称时两处来源一并更替）
                            DisplayName = SeatDisplayNamePrefix + (sc.Seats[i].PlayerId + 1),
                            Connected = sc.Seats[i].Connected,
                        });
                    }
                    for (int p = 0; p < room.Seats.Length; p++)
                    {
                        Session member = room.Seats[p];
                        if (member == null || member.Disconnected) continue;
                        SendToSession(member, PacketType.RoomSeats, seats, reliable: true);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 单循环一帧（MVP 形态，§10.2）：tick 顺序 Incoming → 权威步（命令）→ 快照广播 → Outgoing。
        /// 帧节拍由外部循环控制（<see cref="ServerLoop"/> 绝对锚定 60Hz）。
        /// </summary>
        public void Pump()
        {
            if (_disposed) return;
            _nowMs = NowMs();
            _transport.TickIncoming();
            if (_mailboxRouting)
                _pendingDisconnects.Flush();

            // 逐房间推进（§8.2"一个 Worker 顺序驱动多个 RoomActor"）。
            // 宿主 owner 形态：宿主主线程即唯一执行者；Worker 执行形态：宿主只投递命令并应用回传。
            // 快照广播的播放条件由**每房间自己的席位**判定：多房间下必须按房间传各自的可播视图。
            for (int i = 0; i < _registry.Count; i++)
            {
                RoomInstance room = _registry.Order[i];

                // 排空超时兜底（§12 第 3 步"超时则归档 Aborted 原因并安全关闭"）：
                // 排空期间若某房间未在时限内自然收敛，此处强制关闭，避免永久停在排空态。
                // 两种形态都只负责**投递 Shutdown 命令**；执行者按形态而定。
                if (room.DrainDeadlineMs >= 0 && _nowMs >= room.DrainDeadlineMs
                    && !room.Runtime.Closed && room.TryIssueDrainTimeout())
                {
                    // 超时关闭只投递一次（CAS 在 TryIssueDrainTimeout 内；重复投递会放大计数并挤占控制 lane）
                    if (_workerExecution)
                    {
                        // Worker 形态：命令入盒即计（执行由 Worker 完成；重复关闭在 Runtime 幂等）
                        if (TrySubmitCommandTo(room, RoomCommand.Shutdown(ShutdownReason.DrainTimeout)))
                            _ops.NoteRoomDrainTimedOut();
                    }
                    else
                    {
                        _ops.NoteRoomDrainTimedOut();
                        SubmitCommandTo(room, RoomCommand.Shutdown(ShutdownReason.DrainTimeout));
                    }
                }

                if (_workerExecution)
                {
                    // Worker 执行形态：Tick 命令入 Control lane（合并节流：至多一条在途），
                    // Worker 执行后自行广播；宿主本轮只应用已到达的回传。
                    // 终态房间不再投递 Tick（空转无产出，且干扰销毁的静默判定）。
                    if (!room.Runtime.Closed)
                        SubmitWorkerTick(room);
                    DrainRoomOutbound(room);

                    if (!_registry.IsRegistered(room))
                    {
                        i--;
                        continue;
                    }
                    if (TryRollbackStaleReservation(room)) { i--; continue; }   // 迟到 Join 的动态预留回收
                    if (TryDestroyTerminalRoom(room)) i--;
                    continue;
                }

                // Deferred Mailbox mode must consume the transport batch before the
                // owner-generated Tick. Otherwise Tick advances the authoritative frame
                // first and an input that was valid for frame+1 is classified stale when
                // the Input lane is drained afterwards. Control still wins over Input
                // within this drain; Tick is submitted only after the already-arrived
                // batch has been applied.
                if (_mailboxRouting && !_drainMailboxesImmediately)
                {
                    // 先清空已经在 Control lane 中的消息，再重试暂存的断线事实，
                    // 最后才消费 Input/Outbound；这样重试命令不会越过同批输入。
                    DrainRoomControlMailbox(room);
                    _pendingDisconnects.Flush(room);
                    DrainRoomMailbox(room);
                }

                // A stale dynamic Join can release this room reservation while the
                // mailbox is being drained. Adjust the index so the next room is
                // still visited in this Pump instead of being skipped.
                if (!_registry.IsRegistered(room))
                {
                    i--;
                    continue;
                }

                SubmitCommandTo(room, RoomCommand.Tick(_nowMs));
                if (_mailboxRouting && !_drainMailboxesImmediately)
                    DrainRoomMailbox(room);
                if (room.Runtime.Started)
                    room.Pipeline.BroadcastIfDue(room.Runtime.AuthSim.Frame, room.Runtime.AuthSim,
                        room.Runtime.Gate, room.Runtime.EntityIdOf, room.SeatBroadcastable);

                // 终态房间销毁（§42："drain 与房间销毁后清理归 R2"）：本房间当轮输出已在上方应用
                // （结算入盒等），此处先移除再调整下标，下一轮不再遍历本房间。
                if (TryDestroyTerminalRoom(room)) i--;
            }
            if (++_ticksSinceCleanup >= _tuning.SessionCleanupIntervalTicks)
            {
                _ticksSinceCleanup = 0;
                _ops.NoteSessionsCleaned(_sessions.Cleanup(_nowMs));   // §9.2 周期清理（容量+时效）
                _reconnects.PurgeExpired();
                _rateLimit.PurgeIdle(_nowMs);                        // §343 限流桶同节奏周期清理
            }
            _transport.TickOutgoing();
            MaybePrintOps();
        }

        /// <summary>
        /// 席位可播判定（§9.3 步骤 6）：只有 Active 席位接收增量广播；Restoring（重连恢复中）抑制。
        /// 快照管线按房间构造，故这里只需按 <paramref name="playerId"/> 查该房间自己的席位表。
        /// </summary>
        private bool SeatBroadcastable(int playerId)
        {
            RoomInstance room = CurrentOrFirstRoom;
            return room != null && room.Runtime.TryGetSeat(playerId, out PlayerSession seat)
                && seat.Phase == SeatPhase.Active;
        }

        /// <summary>
        /// 开始排空／优雅关闭（《商业级通用服务端框架总设计》§12"优雅关闭"第 2–5 步的宿主侧）。
        ///
        /// **设计五步与本实现的对应**：
        /// 1. "Readiness 置 false，Lobby 不再分配新 Match"——本服务不接 Lobby，
        ///    故无 readiness 面；等价语义由 <see cref="Draining"/> 对**本进程**生效：不做新房间、不收新进房。
        /// 2. "停止接受新 Join，现有房间进入 drain"——<see cref="Draining"/> 置位后
        ///    <see cref="InboundJoinHandlers"/> 一律拒绝（含已在册房间），既有对局继续跑。
        /// 3. "在配置时限内完成对局；超时则归档 Aborted 原因并安全关闭"——到
        ///    <paramref name="deadlineMs"/> 仍未终态的房间在 <see cref="Pump"/> 中发
        ///    <see cref="ShutdownReason.DrainTimeout"/> 强制关闭。
        /// 4. "刷新 Outbox/Archive 到持久介质"——装配了 <see cref="ISettlementOutbox"/> 时结算在
        ///    SettlementReady 即逐条 write-through 落盘，排空收口经 <see cref="FlushSettlementOutbox"/>
        ///    （未装配时本步为无操作）。
        /// 5. "停止 Worker、Transport 和 Host"——<see cref="Dispose"/> 按
        ///    Worker Stop(drain:true) → Transport → Outbox 顺序收尾；本方法不主动执行第 5 步。
        ///
        /// **幂等**：重复调用只在前移截止时刻时生效（取更早者，不延后已定的排空期限）。
        /// </summary>
        /// <param name="deadlineMs">排空截止（单调毫秒，与 <c>Pump</c> 同一时钟源）。</param>
        public void BeginDrain(long deadlineMs)
        {
            if (_draining && deadlineMs >= _drainDeadlineMs) return;   // 已排空且新期限不更早 → 无操作
            _draining = true;
            _drainDeadlineMs = deadlineMs;

            for (int i = 0; i < _registry.Count; i++)
            {
                RoomInstance room = _registry.Order[i];
                if (room.Runtime.Closed) continue;                     // 终态房间不必排空
                // 取更早的截止时刻：排空期限只可前移，不可被后一次调用延后（规则收在 SetDrainDeadline 内）
                room.SetDrainDeadline(deadlineMs);
            }
            HostLog.Info("Drain", $"开始排空：在册 {_registry.Count} 房，截止 +{deadlineMs}ms（单调时钟）");
        }

        /// <summary>是否已进入排空（§12 第 1–2 步：不再接受新 Join／新房间）。</summary>
        public bool Draining
        {
            get { return _draining; }
        }

        /// <summary>当前在盒待提交的结算条数（未装配 Outbox 时为 0；退出日志/排空收口用）。</summary>
        public int SettlementOutboxPending
        {
            get { return _settlementOutbox?.Count ?? 0; }
        }

        /// <summary>
        /// §12 第 4 步"刷新 Outbox 到持久介质"——排空完成（<see cref="DrainComplete"/>）后的收口动作：
        /// 逐条 write-through 形态下条目在 Enqueue 时已落盘，此处为幂等收口确认（未装配为无操作）。
        /// 调用序：BeginDrain → 等 DrainComplete → 本方法 → Dispose（第 5 步）。
        /// </summary>
        public void FlushSettlementOutbox()
        {
            _settlementOutbox?.Flush();
        }

        /// <summary>
        /// 宿主单调时钟当前毫秒——与 <see cref="Pump"/> 的 <c>_nowMs</c>、<see cref="BeginDrain"/>
        /// 的截止时刻**同一时钟源**。调用方据此刻画排空期限（<c>CurrentMs + 配置时限</c>）。
        /// </summary>
        public long CurrentMs
        {
            get { return NowMs(); }
        }

        /// <summary>排空截止时刻（-1 = 未排空）。</summary>
        public long DrainDeadlineMs
        {
            get { return _drainDeadlineMs; }
        }

        /// <summary>
        /// 排空是否已完成：已进入排空，且**所有在册房间都到终态**（§12 第 3 步收敛判据）。
        /// 调用方据此决定何时执行第 5 步（停 Transport/Host）。
        /// </summary>
        public bool DrainComplete
        {
            get
            {
                if (!_draining) return false;
                for (int i = 0; i < _registry.Count; i++)
                    if (!_registry.Order[i].Runtime.Closed) return false;
                return true;
            }
        }

        /// <summary>按房间号取房间（不存在返回 false；**不创建**——建房要容量校验与配置模板，见 <see cref="OpenRoom"/>）。</summary>
        public bool TryGetRoom(string roomId, out RoomRuntime room)
        {
            if (roomId != null && _registry.TryGetInstance(roomId, out RoomInstance inst))
            {
                room = inst.Runtime;
                return true;
            }
            room = null;
            return false;
        }

        /// <summary>
        /// 打开房间（幂等：已存在则返回既有实例，**不重建**）。走配置模板造房间配置，受
        /// <see cref="MaxRooms"/> 约束——达上限返回 null（调用方**拒绝进房**，不是静默排队）。
        ///
        /// **失败语义**（都返回 null，由调用方转成"拒绝进房"）：
        /// - 未装配 <see cref="RoomServerConfig"/> 且 roomId 与预置房间不符（单房间形态）；
        /// - 达容量上限；
        /// - 模板缺失或模板配置非法（<see cref="RoomServerConfig.BuildRoomConfig"/> 抛，此处捕获转为 null）；
        /// - 多图批：模板与请求行键冲突、请求行键缺当地图（<see cref="ResolveMap"/> 抛，同口径转拒绝）。
        ///
        /// **空 roomId**：建房必须有路由键，直接 null。
        /// </summary>
        /// <param name="requestedMapId">票据盖章的请求地图行键（0 = 未指定——模板/宿主默认图）。</param>
        public RoomRuntime OpenRoom(string roomId, int requestedMapId = 0, int requestedExpectedPlayers = 0)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            if (_registry.TryGetInstance(roomId, out RoomInstance existing)) return existing.Runtime;

            // 单房间兼容形态：只认预置房间，不动态建房（容量 1 由构造路径保证）
            if (_serverConfig == null) return null;
            if (_registry.AtCapacity)
            {
                _ops.NoteRoomRejectedAtCapacity();
                return null;
            }

            try
            {
                // 无模板参数 → 配置的 default_template
                RoomConfig cfg = _serverConfig.BuildRoomConfig(null, roomId, requestedMapId, requestedExpectedPlayers);
                // **内核构造也在 try 内**（P0-2）：此前 try 只包配置解析，AddRoom → RoomRuntime
                // 构造抛出的异常会一路窜上权威循环线程（现成触发源：expected_players 超过内核容量，
                // 现两侧已同源校验；此处是不依赖上游校验的兜底）。构造失败时房间尚未登记（登记是
                // AddRoom 的最后两步），故直接拒绝即可，无半成品残留。
                return AddRoom(cfg, dynamic: true).Runtime;
            }
            catch (Exception ex)
            {
                // 配置/构造错误不炸宿主：拒绝该房号并计数（§429 范围校验在装载期已挡掉大部分，此处兜底）
                _ops.NoteRoomRejectedBadConfig();
                HostLog.Warn("Room", $"建房被拒 room={roomId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 行键 → 判定图解析（多图批）：0/负 = 宿主默认图（<see cref="_map"/>）；&gt;0 经 <see cref="_mapCatalog"/>；
        /// **目录缺失 = 配置缺失显式抛**（不静默回落默认图——"请求训练场"静默变首图是判定面分叉源）。
        /// 目录实现的缺行/坏值语义 = fail-closed 抛（调用方 <see cref="OpenRoom"/> 转"拒绝建房"）。
        /// </summary>
        private SimMapData ResolveMap(int mapId)
        {
            if (mapId <= 0) return _map;
            if (_mapCatalog == null)
                throw new InvalidOperationException(
                    $"宿主未装配多图目录（mapCatalog）——不可路由 map_id={mapId}（装配经 HostAssembly.Inputs.MapCatalog）");
            return _mapCatalog(mapId);
        }

        /// <summary>登记一个房间（构造预置与 <see cref="OpenRoom"/> 共用）。</summary>
        private RoomInstance AddRoom(RoomConfig cfg, bool dynamic = false)
        {
            // 宿主地图落进房间配置（配置自带者优先）——RoomRuntime 据此必填读取（D22）；
            // 多图批：按房间行键解析（0 = 宿主默认图，见 ResolveMap）。
            cfg.Map ??= ResolveMap(cfg.MapId);
            int mailboxCapacity = _serverConfig?.MailboxCapacity ?? RoomInstance.DefaultMailboxCapacity;
            RoomInstance inst = _workerExecution
                ? new RoomInstance(cfg, mailboxCapacity, mailboxCapacity, mailboxCapacity, dynamic, true, _combat, _weapons, _gameplayDigestBase)
                : new RoomInstance(cfg, mailboxCapacity, dynamic, _combat, _weapons, _gameplayDigestBase);
            if (_workerExecution)
            {
                // Worker 执行形态：房间在宿主线程建立，随后按 roomId 交给归属 Worker 驱动；
                // 广播发送入 Outbound 回传（宿主解析会话、编码并经 Transport 发送）。
                inst.BindDriveAction(() => _workerDriver.Drive(inst));
                inst.Pipeline.SendTo = (seat, type, message, reliable) =>
                    _workerDriver.Send(inst, seat, type, message, reliable);
            }
            else
            {
                inst.Pipeline.SendTo = (seat, type, message, reliable) =>
                    SendToSession((Session)seat, type, message, reliable);
            }
            _registry.Add(inst);
            return inst;
        }

        /// <summary>
        /// 只移除刚刚预留且尚未形成席位的空房间。动态 Join 入盒失败或 Runtime
        /// 消费后拒绝时用来回滚房间容量 reservation；已有席位/已开局房间绝不回收。
        /// 身份校验防止误删**同房号重建后的新房间**（旧实例被终态销毁后，同房号可再建）。
        /// </summary>
        private bool RemoveEmptyRoom(RoomInstance room)
        {
            if (room == null || !room.IsDynamic || room.Runtime.Started
                || room.Mailbox.Count != 0)
                return false;
            for (int i = 0; i < room.Seats.Length; i++)
                if (room.Seats[i] != null) return false;
            if (!_registry.IsRegistered(room)) return false;
            _registry.Remove(room);
            room.Mailbox.Complete();
            return true;
        }

        /// <summary>
        /// 终态房间销毁（《商业级通用服务端框架总设计》§42"drain 与房间销毁后清理归 R2"）。
        ///
        /// 触发：房间到达终态（<see cref="RoomRuntime.Closed"/> = Closed/Aborted）后，在随后的
        /// <see cref="Pump"/> 中销毁——销毁前本房间**当轮输出已全部应用**（结算入 Outbox、
        /// 关闭事件已记日志），因此不丢结算事实。
        ///
        /// 动作：从房间表移除（容量归还，达上限前不再占用）→ Mailbox Complete（停止入站，
        /// 残余消息丢弃——房间已终态，输入/Tick 均为无操作）→ 清理席位会话的路由字段。
        ///
        /// **为什么必须清理会话路由**：销毁后同房号可重建。若旧会话仍带
        /// <c>RoomId/PlayerId</c>，其迟到输入会按房号命中**新房间**并可能落在陌生人的席位上
        /// （跨连接的输入串位）。清理后旧会话重新 Join 必须再次走准入。
        ///
        /// **Worker 执行形态的静默前提**：销毁必须等房间静默（Worker 无在途驱动、无待驱动工作项、
        /// 三条 lane 全空）——Worker 执行 Shutdown 与宿主观察到 Closed 之间存在回传窗口，
        /// 不静默就丢弃会把仍在路上的结算输出一并丢掉。
        /// </summary>
        private bool TryDestroyTerminalRoom(RoomInstance room)
        {
            if (room == null || !room.Runtime.Closed) return false;

            // Worker 执行形态的销毁前提是房间**静默**：Worker 无在途驱动、无待驱动工作项、
            // 三条 lane 全空。否则"销毁时丢弃残留"会把仍在回传路上的结算输出一并丢掉
            //（Worker 执行 Shutdown 与宿主观察到 Closed 之间有一个回传窗口）。
            if (_workerExecution)
            {
                if (room.HasPendingWork) return false;   // 登记位或在途计数任一非零 = 未静默
                DrainRoomOutbound(room);   // 静默后收口最后一次回传（结算入盒等）
                if (room.Mailbox.Count != 0) return false;
            }

            if (!_registry.IsRegistered(room))
                return false;   // 已被移除或已重建：不是本实例

            for (int p = 0; p < room.Seats.Length; p++)
            {
                Session seat = room.Seats[p];
                room.Seats[p] = null;
                if (seat == null) continue;
                if (string.Equals(seat.RoomId, room.RoomId, StringComparison.Ordinal)) seat.RoomId = null;
                if (seat.PlayerId == p)
                {
                    seat.PlayerId = -1;
                    seat.Principal = null;
                    seat.BuildHash = null;
                }
            }

            _registry.Remove(room);
            room.Mailbox.Complete();
            while (room.Mailbox.TryDequeue(out _)) { }   // 残余控制/输入/出站消息随房间终态丢弃
            _ops.NoteRoomDestroyed();
            HostLog.Info("Room", $"终态房间已销毁：{room.RoomId}（在册 {_registry.Count}/{MaxRooms}）");
            return true;
        }

        /// <summary>按房间号取房间（新建房时用；不存在返回 null）。</summary>
        private RoomInstance GetRoomInstance(string roomId)
        {
            return roomId != null && _registry.TryGetInstance(roomId, out RoomInstance i) ? i : null;
        }

        /// <summary>
        /// Ops 周期打印（默认 5s）。**逐房间一行**——多房间下把整台服务器的帧号/快照合成一行会丢掉
        /// "哪个房间慢"这个唯一有用的信息（§514"多房间并发时一个慢客户端或过载房间不拖累其他房间"
        /// 的观测前提就是能按房间看）。节拍与宿主级计数只打在首行（属进程/Worker 级，不属房间）。
        /// </summary>
        private void MaybePrintOps()
        {
            if (!_ops.PrintEnabled) return;
            if (!_ops.TryBeginPrint(_nowMs, _tuning.OpsIntervalMs)) return;   // 节流判据归 Ops（P4）

            for (int i = 0; i < _registry.Count; i++)
            {
                RoomInstance room = _registry.Order[i];
                HostLog.Info("Ops", _ops.Format(room.RoomId, room.Runtime, room.Pipeline, _sessions,
                    _registry.Count, i == 0 ? LoopStats : null, i == 0 ? _rateLimit : null,
                    room.OutboundRejectedCount));   // P4：回传拒绝数接进 Ops 行（原先只增不读）
            }
        }

        /// <summary>节拍统计来源（宿主装配 ServerLoop 后注入；null = 不打印节拍段——用例/嵌入式用法）。</summary>
        public ServerLoop.LoopStats LoopStats { get; set; }

        /// <summary>
        /// 入站端口适配（自 <see cref="ServerHost"/> 私有面收窄给三个入站处理器）：
        /// 单实例实现五端口，处理器构造时注入；房间编排/诊断/时钟仍归宿主单点。
        /// </summary>
        private sealed class InboundHostAdapter :
            IInboundCoreHost, IInboundJoinHost, IInboundReconnectHost, IRoomGateway, IInboundDiagnostics
        {
            private readonly ServerHost _host;

            public InboundHostAdapter(ServerHost host)
            {
                _host = host;
            }

            long IInboundCoreHost.BeginInbound() => _host.BeginInbound();
            int IInboundCoreHost.MaxInboundPacketBytes => _host._tuning.MaxInboundPacketBytes;
            SessionManager IInboundCoreHost.Sessions => _host._sessions;
            RateLimiter IInboundCoreHost.RateLimit => _host._rateLimit;
            Ops IInboundCoreHost.Ops => _host._ops;
            bool IInboundCoreHost.MailboxRouting => _host._mailboxRouting;
            IRoomGateway IInboundCoreHost.Rooms => this;
            IInboundDiagnostics IInboundCoreHost.Diagnostics => this;

            JoinAdmissionGate IInboundJoinHost.Admission => _host._admission;
            Ops IInboundJoinHost.Ops => _host._ops;
            bool IInboundJoinHost.Draining => _host._draining;
            bool IInboundJoinHost.MailboxRouting => _host._mailboxRouting;
            IRoomGateway IInboundJoinHost.Rooms => this;
            IInboundDiagnostics IInboundJoinHost.Diagnostics => this;

            IRoomTransport IInboundReconnectHost.Transport => _host._transport;
            ReconnectService IInboundReconnectHost.Reconnects => _host._reconnects;
            SessionManager IInboundReconnectHost.Sessions => _host._sessions;
            RateLimiter IInboundReconnectHost.RateLimit => _host._rateLimit;
            Ops IInboundReconnectHost.Ops => _host._ops;
            bool IInboundReconnectHost.MailboxRouting => _host._mailboxRouting;
            bool IInboundReconnectHost.WorkerExecution => _host._workerExecution;
            IRoomGateway IInboundReconnectHost.Rooms => this;
            IInboundDiagnostics IInboundReconnectHost.Diagnostics => this;

            RoomInstance IRoomGateway.Find(string roomId) => _host.GetRoomInstance(roomId);

            RoomRuntime IRoomGateway.Open(string roomId, int requestedMapId, int requestedExpectedPlayers)
                => _host.OpenRoom(roomId, requestedMapId, requestedExpectedPlayers);

            bool IRoomGateway.RemoveEmpty(RoomInstance room) => _host.RemoveEmptyRoom(room);

            void IRoomGateway.Submit(RoomInstance room, in RoomCommand cmd, Session joinContext)
                => _host.SubmitCommandTo(room, in cmd, joinContext);

            bool IRoomGateway.TrySubmit(RoomInstance room, in RoomCommand cmd, Session joinContext)
                => _host.TrySubmitCommandTo(room, in cmd, joinContext);

            void IRoomGateway.EnqueuePendingDisconnect(RoomInstance room, Session session)
                => _host._pendingDisconnects.Enqueue(room, session);

            void IRoomGateway.SchedulePump(RoomInstance room) => _host.ScheduleRoomPump(room);

            void IRoomGateway.DrainMailbox(RoomInstance room) => _host.DrainRoomMailbox(room);

            int IRoomGateway.MaxRooms => _host.MaxRooms;

            int IRoomGateway.RoomCount => _host._registry.Count;

            void IInboundDiagnostics.SendToSession(Session session, PacketType type, IMessage message, bool reliable)
                => _host.SendToSession(session, type, message, reliable);

            void IInboundDiagnostics.RejectJoin(Session session, in Proto.JoinRequest join, string stage, string code, string reason)
                => _host.RejectJoin(session, in join, stage, code, reason);

            void IInboundDiagnostics.NoteMailboxClosed() => Interlocked.Increment(ref _host._mailboxClosed);

            void IInboundDiagnostics.NoteMailboxRejectedFull() => Interlocked.Increment(ref _host._mailboxRejectedFull);
        }
    }
}
