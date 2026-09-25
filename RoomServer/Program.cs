using System;
using System.Collections.Generic;
using System.Threading;
using LiteSim;
using LiteNet.Transport;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;

// RoomServer 入口（M10：批② 权威循环 + 批③ 快照/回溯/Ops）。
// 节拍由 ServerLoop 绝对锚定（60Hz，防漂移累积）。
//
// 配置来源（2026-09-26 起）：配置文件为**主源**，命令行可覆盖少量宿主参数。
//   --config <path>   配置文件路径（默认 Config/roomserver.json）
//   --combat-table <path>  玩法数值表（缺省走 CombatNumbers.LoadFromRepo 的仓库路径）
//   其余：--port / --duration <ms> / --quiet / --ticket-key <kid>:<base64> / --audience <id>
//
// **房间形态**：单进程多房间 + 动态创建（§6"一个 roomId 只能映射一个独立 RoomActor"）。
// 房间参数来自配置模板，玩法数值为**进程级共享**（Sim 静态读 CombatConfig——参数化迁 G1）。
var serverConfig = RoomServerConfig.Load(ResolveConfigPath(args));
serverConfig.Combat.Apply();          // 共享数值装载进 Sim 静态消费面（所有房间同一份）

long durationMs = 0;
bool quiet = false;
int? portOverride = null;
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
Console.WriteLine($"[RoomServer] 玩法数值（进程级共享，所有房间同一份）：move={serverConfig.Combat.MoveSpeed} " +
    $"gravity={serverConfig.Combat.Gravity} hp={serverConfig.Combat.EntityHp} dmg={serverConfig.Combat.BaseDamage}±{serverConfig.Combat.DamageSpread}");

// 预置房间：**不预置**——单进程多房间下所有房间首次进房时按配置模板创建（懒创建），
// 容量上限 max_rooms 就是全部房间数。预置一个房间会白占一格，且"该预置哪个 roomId"没有依据。
using var transport = new KcpTransportServer();
using var host = new ServerHost(transport, null, ticketValidator, audience, serverConfig);
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
    loop.Start();                               // 常驻形态
    Console.WriteLine("[RoomServer] 常驻中（Ctrl+C 退出）");
    Thread.Sleep(Timeout.Infinite);
}

/// <summary>取 --config 的值；缺省用配置类给出的相对路径（相对工作目录）。</summary>
static string ResolveConfigPath(string[] argv)
{
    for (int i = 0; i < argv.Length; i++)
        if (argv[i] == "--config" && i + 1 < argv.Length) return argv[i + 1];
    return RoomServerConfig.DefaultRelativePath;
}
