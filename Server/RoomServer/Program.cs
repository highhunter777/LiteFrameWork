using System;
using System.Collections.Generic;
using System.Threading;
using LiteSim;
using LiteNet.Transport;
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
//
// **房间形态**：单进程多房间 + 动态创建（§6"一个 roomId 只能映射一个独立 RoomActor"）。
// 房间参数来自配置模板，玩法数值为**进程级共享**（Sim 静态读 CombatConfig——参数化迁 G1）。
var serverConfig = RoomServerConfig.Load(ResolveConfigPath(args));

long durationMs = 0;
bool quiet = false;
int? portOverride = null;
string combatTableDir = null;
var ticketKeys = new List<JoinTicketKey>();
string audienceOverride = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port": if (i + 1 < args.Length) portOverride = int.Parse(args[++i]); break;
        case "--duration": if (i + 1 < args.Length) durationMs = long.Parse(args[++i]); break;
        case "--quiet": quiet = true; break;
        case "--audience": if (i + 1 < args.Length) audienceOverride = args[++i]; break;
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

// 玩法数值装载（.bytes 表——与客户端同一份文件；fail-fast：数值缺失宁可起不来）。
// 装载即回填 CombatConfig 权威面（客户端 ConfigService 同语义）——Sim 消费的数值就是表值。
if (combatTableDir != null) CombatNumbers.LoadTableBytes(combatTableDir);
else CombatNumbers.LoadFromRepo();

// 票据验证器装配：生产**必须**传 key（fail-closed）。缺 key 不静默退回——显式告警（§6"不能悄悄退回 fake"）。
if (portOverride.HasValue) serverConfig.OverridePort(portOverride.Value);

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
Console.WriteLine($"[RoomServer] 玩法数值（进程级共享，所有房间同一份）：move={LiteSim.CombatConfig.MoveSpeed} " +
    $"gravity={LiteSim.CombatConfig.Gravity} hp={LiteSim.CombatConfig.EntityHp} dmg={LiteSim.CombatConfig.BaseDamage}±{LiteSim.CombatConfig.DamageSpread}");

// 预置房间：**不预置**——单进程多房间下所有房间首次进房时按配置模板创建（懒创建），
// 容量上限 max_rooms 就是全部房间数。预置一个房间会白占一格，且"该预置哪个 roomId"没有依据。

// 结算 Outbox（§11.3"本地持久 Outbox"；排空第 4 步的落地面）。启动时续接既有日志：
// 进程重启后待提交条目不丢（重启恢复面）；坏行（崩溃半行）跳过并计数。
using var settlementOutbox = FileSettlementOutbox.Open(
    serverConfig.SettlementJournalPath, serverConfig.SettlementOutboxCapacity);
if (settlementOutbox.Count > 0 || settlementOutbox.SkippedCorruptLines > 0)
    Console.WriteLine($"[RoomServer] 结算日志重放：待提交 {settlementOutbox.Count} 条（坏行跳过 {settlementOutbox.SkippedCorruptLines}）");

using var transport = new KcpTransportServer();
// workerExecution: true = 生产形态（§8.2）：房间命令与快照广播在 hash 归属的 Worker 上执行，
// 宿主只做 Transport IO、准入与回传应用（Outbound lane）。嵌入式/历史用例保持默认的宿主 owner 直驱形态。
using var host = new ServerHost(transport, null, ticketValidator, audience, serverConfig, settlementOutbox,
    mailboxRouting: true, drainMailboxesImmediately: false, workerExecution: true);
host.Ops.PrintEnabled = !quiet;

// --port 是宿主级覆盖（配置文件里的 port 是同一个值的来源；此处允许验收脚本临时换端口）。
// 通过配置对象自身复用来覆盖：ServerHost 从 RoomServerConfig.Port 取监听端口。

var loop = new ServerLoop(host);
host.LoopStats = loop.Stats;                    // Ops 行带上节拍/掉债观测（常驻过载时可见）
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
