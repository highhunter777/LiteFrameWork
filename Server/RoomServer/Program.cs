using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using LiteSim;
using LiteNet.Transport;
using Microsoft.Extensions.DependencyInjection;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;

// 排空宽限：收到停止信号后等待对局自然收敛的时长（§12 第 3 步"在配置时限内完成对局"）。
const long DrainGraceMs = 5_000;

// RoomServer 入口（权威循环 + 快照/回溯/Ops）。
// 节拍由 ServerLoop 绝对锚定（60Hz，防漂移累积）。
//
// 配置来源：宿主参数走配置文件 + 命令行覆盖；**玩法数值走 .bytes 表**
// （与客户端同一份二进制——两端同代码同数据，物理上不可漂移）。
//   --config <path>        配置文件路径（默认 Config/roomserver.json）
//   --combat-table <dir>   玩法数值表目录（缺省走 CombatNumbers.LoadFromRepo 的仓库路径 Assets/GameData/Config）
//   其余：--port / --duration <ms> / --quiet / --ticket-key <kid>:<base64> / --audience <id>
//   --envelope-key <base64>（或环境变量 LITENET_SECURE_ENVELOPE_KEY）
//   --lobby-instance-key <base64>（或环境变量 LITENET_LOBBY_INSTANCE_KEY；配置 lobby.url 时必填——
//     实例注册凭据，与 Meta 的 LobbyInstanceKeyBase64 同值）
//   --allow-insecure-local（仅本机联调，显式允许裸 KCP；公网/共享环境禁止）
//
// **房间形态**：单进程多房间 + 动态创建（§6"一个 roomId 只能映射一个独立 RoomActor"）。
// 房间参数来自配置模板，玩法数值为**进程级共享**（Sim 静态读 CombatConfig——参数化迁 G1）。
//
// **装配（2026-10-08 批1）**：组合根走 MS DI（《服务端宿主装配收敛专项设计》）——
// 对象图注册/构建在 HostAssembly；解析序=创建序（outbox → transport → host），
// 容器按创建序逆序释放，与历史 using 声明序（host→transport→outbox）等价。
var serverConfig = RoomServerConfig.Load(ResolveConfigPath(args));

long durationMs = 0;
bool quiet = false;
int? portOverride = null;
string combatTableDir = null;
var ticketKeys = new List<JoinTicketKey>();
string audienceOverride = null;
string envelopeKeyBase64 = Environment.GetEnvironmentVariable("LITENET_SECURE_ENVELOPE_KEY");
string lobbyInstanceKeyBase64 = Environment.GetEnvironmentVariable("LITENET_LOBBY_INSTANCE_KEY");
bool allowInsecureLocal = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port": if (i + 1 < args.Length) portOverride = int.Parse(args[++i]); break;
        case "--duration": if (i + 1 < args.Length) durationMs = long.Parse(args[++i]); break;
        case "--quiet": quiet = true; break;
        case "--audience": if (i + 1 < args.Length) audienceOverride = args[++i]; break;
        case "--envelope-key": if (i + 1 < args.Length) envelopeKeyBase64 = args[++i]; break;
        case "--lobby-instance-key": if (i + 1 < args.Length) lobbyInstanceKeyBase64 = args[++i]; break;
        case "--allow-insecure-local": allowInsecureLocal = true; break;
        case "--combat-table": if (i + 1 < args.Length) combatTableDir = args[++i]; break;
        case "--ticket-key":
            if (i + 1 < args.Length)
            {
                string spec = args[++i];
                int sep = spec.IndexOf(':');
                if (sep <= 0 || sep == spec.Length - 1)
                    throw new ArgumentException($"--ticket-key 需要 <kid>:<base64>，收到：{spec}");
                ticketKeys.Add(JoinTicketKey.FromBase64(spec.Substring(0, sep), spec.Substring(sep + 1)));
            }
            break;
    }
}

SecureEnvelopeOptions envelopeOptions = null;
if (!string.IsNullOrWhiteSpace(envelopeKeyBase64))
{
    envelopeOptions = SecureEnvelopeOptions.FromBase64(envelopeKeyBase64);
}
else if (!allowInsecureLocal)
{
    throw new InvalidOperationException("RoomServer 默认拒绝裸 KCP：请提供 --envelope-key 或 LITENET_SECURE_ENVELOPE_KEY；仅本机联调可显式使用 --allow-insecure-local。");
}

// 玩法数值/武器表装载（.bytes 表——与客户端同一份文件；fail-fast：数值缺失宁可起不来）。
// 装载产物 = CombatValues/WeaponTable 实例（技术债 #1：不再回填全局静态面）——经 HostAssembly.Inputs
// 显式传入宿主/房间；缺失会在装配入口被拒（必填校验），"丢弃返回值跑默认值"的分叉面结构性消失。
ServerTableLoad load = combatTableDir != null
    ? CombatNumbers.LoadTableBytes(combatTableDir)
    : CombatNumbers.LoadFromRepo();
CombatValues combat = load.Combat.ToValues();
WeaponTable weapons = load.Weapons;

// 票据验证器装配：生产**必须**传 key（fail-closed）。缺 key 不静默退回——显式告警（§6"不能悄悄退回 fake"）。
if (portOverride.HasValue) serverConfig.OverridePort(portOverride.Value);

// Lobby 注册参数（配置了 lobby.url 才组装）：实例密钥经环境变量/命令行注入（不进配置文件，
// 《上云测试专项设计》§5"密钥经环境变量/Secret"——命令行明文仅本机联调）。
LobbyRegistrationClient.Settings lobbySettings = null;
if (serverConfig.LobbyEnabled)
{
    if (string.IsNullOrWhiteSpace(lobbyInstanceKeyBase64))
        throw new InvalidOperationException(
            "RoomServer 已配置 Lobby 注册（lobby.url）但缺实例密钥：请提供 --lobby-instance-key 或环境变量 LITENET_LOBBY_INSTANCE_KEY。");
    byte[] lobbyKey;
    try
    {
        lobbyKey = Convert.FromBase64String(lobbyInstanceKeyBase64);
    }
    catch (FormatException)
    {
        throw new InvalidOperationException("Lobby 实例密钥必须是 Base64（原值不回显）。");
    }
    lobbySettings = new LobbyRegistrationClient.Settings
    {
        Url = serverConfig.LobbyUrl,
        InstanceId = serverConfig.LobbyInstanceId,
        InstanceKey = lobbyKey,
        AdvertiseHost = serverConfig.LobbyAdvertiseHost,
        HeartbeatIntervalMs = (int)serverConfig.LobbyHeartbeatIntervalMs,
        RequestTimeoutMs = 5000,
    };
}

// 结算提交参数（配置了 settlement.submit_url 才组装）：与 Lobby 注册共用实例密钥
// （同一条房间侧→Meta 服务通道）。
SettlementSubmitService.Settings settlementSettings = null;
if (serverConfig.SettlementEnabled)
{
    if (string.IsNullOrWhiteSpace(lobbyInstanceKeyBase64))
        throw new InvalidOperationException(
            "RoomServer 已配置结算提交管道（settlement.submit_url）但缺实例密钥：请提供 --lobby-instance-key 或环境变量 LITENET_LOBBY_INSTANCE_KEY。");
    byte[] settlementKey;
    try
    {
        settlementKey = Convert.FromBase64String(lobbyInstanceKeyBase64);
    }
    catch (FormatException)
    {
        throw new InvalidOperationException("结算提交实例密钥必须是 Base64（原值不回显）。");
    }
    settlementSettings = new SettlementSubmitService.Settings
    {
        Url = serverConfig.SettlementSubmitUrl,
        InstanceKey = settlementKey,
        IntervalMs = (int)serverConfig.SettlementIntervalMs,
        MaxBackoffMs = (int)serverConfig.SettlementMaxBackoffMs,
        RequestTimeoutMs = (int)serverConfig.SettlementTimeoutMs,
    };
}

IJoinTicketValidator ticketValidator = null;
string audience = audienceOverride ?? serverConfig.Audience;
if (ticketKeys.Count > 0)
{
    ticketValidator = new HmacJoinTicketValidator(ticketKeys);
    Console.WriteLine($"[RoomServer] 票据验签已启用（kid: {string.Join(",", ticketKeys.ConvertAll(k => k.Kid))} / audience: {(string.IsNullOrEmpty(audience) ? "不校验" : audience)}）");
}
else
{
    Console.WriteLine("[RoomServer] !! 未装配票据验签：token 只作非空校验（原型形态）——公网部署前必须传入 --ticket-key");
}

Console.WriteLine($"[RoomServer] 配置：{serverConfig.Describe()}（{serverConfig.SourcePath}）");
Console.WriteLine($"[RoomServer] 启动（端口 {serverConfig.Port} / 容量 {serverConfig.MaxRooms} 房 / 模板 [{string.Join(",", serverConfig.TemplateIds)}] / {SimConfig.TickRate}Hz 权威步 / {SimConfig.SnapshotHz}Hz 快照）");
Console.WriteLine($"[RoomServer] buildHash={ServerHost.ServerBuildHash}（源码内容哈希——Sim 或协议一改即变）");
Console.WriteLine($"[RoomServer] 玩法数值（装载实例——所有房间同一份）：move={combat.MoveSpeed} " +
    $"gravity={combat.Gravity} hp={combat.EntityHp} dmg={combat.BaseDamage}±{combat.DamageSpread}");

// 预置房间：**不预置**——单进程多房间下所有房间首次进房时按配置模板创建（懒创建），
// 容量上限 max_rooms 就是全部房间数。预置一个房间会白占一格，且"该预置哪个 roomId"没有依据。

// ---- 组合根（2026-10-08 批1：MS DI）——对象图装配在 HostAssembly，见《服务端宿主装配收敛专项设计》----
var services = new ServiceCollection();
HostAssembly.Register(services, new HostAssembly.Inputs
{
    Config = serverConfig,
    Audience = audience,
    TicketValidator = ticketValidator,
    EnvelopeOptions = envelopeOptions,
    CombatValues = combat,
    Weapons = weapons,
    Lobby = lobbySettings,
    Settlement = settlementSettings,
});
using var provider = HostAssembly.Build(services);

// 解析序=创建序：outbox → transport → host。容器按创建序**逆序**释放（host→transport→outbox），
// 与历史 using 声明序等价（排空第 5 步手验基线）。**不得先解析 host**——否则创建序会变成
// transport→outbox→host、释放序漂移为 host→outbox→transport。

// 结算 Outbox（§11.3"本地持久 Outbox"；排空第 4 步的落地面）。启动时续接既有日志：
// 进程重启后待提交条目不丢（重启恢复面）；坏行（崩溃半行）跳过并计数。
var settlementOutbox = provider.GetRequiredService<FileSettlementOutbox>();
if (settlementOutbox.Count > 0 || settlementOutbox.SkippedCorruptLines > 0)
    Console.WriteLine($"[RoomServer] 结算日志重放：待提交 {settlementOutbox.Count} 条（坏行跳过 {settlementOutbox.SkippedCorruptLines}）");

_ = provider.GetRequiredService<IRoomTransport>();      // 仅锁定创建序（宿主工厂取回同一单例）
Console.WriteLine(envelopeOptions == null
    ? "[RoomServer] !! 安全信封未启用：仅允许本机联调，不得暴露公网"
    : "[RoomServer] 安全信封已启用（AES-256-GCM / HKDF-SHA256 / 64 位重放窗口）");

var host = provider.GetRequiredService<ServerHost>();
host.Ops.PrintEnabled = !quiet;

// Lobby 注册客户端（解析在宿主之后——创建序=解析序，释放逆序天然"先停心跳、再收宿主"）。
// 排空期间心跳继续（快照 Draining=true）——Lobby 立即停止分配新对局（§7 drain 协同），
// 直到进程退出才停止上报。
LobbyRegistrationClient lobbyClient = provider.GetService<LobbyRegistrationClient>();
if (lobbyClient != null)
{
    Console.WriteLine($"[RoomServer] Lobby 注册已启用：url={serverConfig.LobbyUrl} instance={serverConfig.LobbyInstanceId} advertise={serverConfig.LobbyAdvertiseHost}:{host.BoundPort} 心跳={serverConfig.LobbyHeartbeatIntervalMs}ms");
    lobbyClient.Start();
}

// 结算提交管道（解析在 Lobby 之后——释放逆序先停提交、再停心跳、再收宿主）。
// 启动即从日志待提交面续投（重启恢复面：条目在 Outbox 日志，不在本服务内存）。
SettlementSubmitService settlementSubmit = provider.GetService<SettlementSubmitService>();
if (settlementSubmit != null)
{
    Console.WriteLine($"[RoomServer] 结算提交管道已启用：url={serverConfig.SettlementSubmitUrl} 待提交={settlementSubmit.PendingCount} 间隔={serverConfig.SettlementIntervalMs}ms 退避上限={serverConfig.SettlementMaxBackoffMs}ms");
    settlementSubmit.Start();
}

// --port 是宿主级覆盖（配置文件里的 port 是同一个值的来源；此处允许验收脚本临时换端口）。
// 通过配置对象自身复用来覆盖：ServerHost 从 RoomServerConfig.Port 取监听端口。

var loop = provider.GetRequiredService<ServerLoop>();   // LoopStats 接线在装配工厂内
if (durationMs > 0)
{
    loop.Run(durationMs);                       // 验收形态：跑满时长即退出
var stats = loop.Stats;
    Console.WriteLine($"[RoomServer] 跑满 {durationMs}ms：房间数={host.RoomCount} ticks={stats.Ticks} " +
        $"掉时债={stats.DroppedTimeMs}ms 放弃追帧={stats.CatchUpAbandoned}");
}
else
{
    // 常驻形态：Ctrl+C 触发**优雅关闭**（§12 优雅关闭 2→4 步；不再直接杀进程）。
    // 第 1 步（readiness 置 false / Lobby 停分配）本服务无 Lobby 面，等价语义由 host.Draining 承担；
    // 第 4 步（刷 Outbox 到持久介质）经 host.FlushSettlementOutbox() 收口；
    // 第 5 步的 Worker/Transport 生命周期已由 using/Dispose 接线。
    var shutdown = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;                    // 不让默认行为直接杀进程——先排空
        shutdown.Set();
    };
    // 容器 stop/K8s 默认发 SIGTERM（≠Ctrl+C）——《上云测试专项设计》§5：同排空路径。
    using var sigterm = TryRegisterSigterm(shutdown);

    loop.Start();
    Console.WriteLine($"[RoomServer] 常驻中（Ctrl+C 优雅关闭；排空时限 {DrainGraceMs}ms；结算日志 {serverConfig.SettlementJournalPath}）");
    shutdown.Wait();

    Console.WriteLine("[RoomServer] 收到停止信号 → 开始排空");
    host.BeginDrain(host.CurrentMs + DrainGraceMs);
    var drainWatch = System.Diagnostics.Stopwatch.StartNew();
    while (!host.DrainComplete && drainWatch.ElapsedMilliseconds < DrainGraceMs + 2_000)
    {
        Thread.Sleep(50);
    }
    loop.Stop();

    // §12 第 4 步：刷新 Outbox 到持久介质（逐条 write-through 已落盘，此处为收口确认）；
    // 未提交条目保持在日志中可重试——下次启动的"结算日志重放"即恢复面。
    host.FlushSettlementOutbox();
    Console.WriteLine(host.DrainComplete
        ? $"[RoomServer] 排空完成（{drainWatch.ElapsedMilliseconds}ms）：房间数={host.RoomCount} drainTimeout={host.Ops.RoomsDrainTimedOut} 结算在盒={host.SettlementOutboxPending}（待提交）"
        : $"[RoomServer] 排空未在时限内完成（{drainWatch.ElapsedMilliseconds}ms）——按超时退出（结算在盒={host.SettlementOutboxPending}）");
}

/// <summary>取 --config 的值；缺省用配置类给出的相对路径（相对工作目录）。</summary>
static string ResolveConfigPath(string[] argv)
{
    for (int i = 0; i < argv.Length; i++)
        if (argv[i] == "--config" && i + 1 < argv.Length) return argv[i + 1];
    return RoomServerConfig.DefaultRelativePath;
}

/// <summary>
/// SIGTERM 排空接线（《上云测试专项设计》§5）：容器 stop/K8s 停止信号与 Ctrl+C 走同一排空路径
/// （BeginDrain → DrainComplete → Flush → Dispose）。Windows 等不支持该信号的平台返回 null
/// （本地常驻形态仍由 Ctrl+C 承担）；注册本身失败不致命——如实降级不阻断启动。
/// </summary>
static PosixSignalRegistration TryRegisterSigterm(ManualResetEventSlim shutdown)
{
    try
    {
        return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;          // 阻止默认终止——交给排空链收尾
            shutdown.Set();
        });
    }
    catch (Exception)
    {
        return null;
    }
}
