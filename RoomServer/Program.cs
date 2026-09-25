using System;
using System.Collections.Generic;
using System.Threading;
using LiteSim;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;

// RoomServer 入口（M10：批② 权威循环 + 批③ 快照/回溯/Ops）。
// MVP 参数固定（端口 17777 / Room-A / 2 人房）；节拍由 ServerLoop 绝对锚定（60Hz，防漂移累积）。
// 常用命令行：--port <n> / --room <id> / --players <n>（4 人房）/ --seed <n>（固定 seed，确定性验收）/
//            --duration <ms>（跑满即退出，验收脚本用）/ --quiet（关 Ops 打印）。
// 票据（§P0-6 / 《框架先行》§5-4）：--ticket-key <kid>:<base64>（可重复，支持轮换期新旧并存）
//            / --audience <id>。**未传 key = 退化为原型级非空校验**（见下方告警）。
// 装配参数一律收口到 RoomConfig（2026-09-19 审计建议 2/3）——房间容量与房间号不再写死。
var config = RoomConfig.Default();
long durationMs = 0;
bool quiet = false;
var ticketKeys = new List<JoinTicketKey>();
string audience = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port": if (i + 1 < args.Length) config.Port = int.Parse(args[++i]); break;
        case "--room": if (i + 1 < args.Length) config.RoomId = args[++i]; break;
        case "--players": if (i + 1 < args.Length) config.ExpectedPlayers = int.Parse(args[++i]); break;
        case "--seed": if (i + 1 < args.Length) config.Seed = long.Parse(args[++i]); break;
        case "--duration": if (i + 1 < args.Length) durationMs = long.Parse(args[++i]); break;
        case "--quiet": quiet = true; break;
        case "--audience": if (i + 1 < args.Length) audience = args[++i]; break;
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
IJoinTicketValidator ticketValidator = null;
if (ticketKeys.Count > 0)
{
    ticketValidator = new HmacJoinTicketValidator(ticketKeys);
    Console.WriteLine($"[RoomServer] 票据验签已启用（kid: {string.Join(",", ticketKeys.ConvertAll(k => k.Kid))} / audience: {(string.IsNullOrEmpty(audience) ? "不校验" : audience)}）");
}
else
{
    Console.WriteLine("[RoomServer] !! 未装配票据验签：token 只作非空校验（原型形态）——公网部署前必须传入 --ticket-key");
}

Console.WriteLine($"[RoomServer] 启动（端口 {config.Port} / 房间 {config.RoomId} / {config.ExpectedPlayers} 人房 / {SimConfig.TickRate}Hz 权威步 / {SimConfig.SnapshotHz}Hz 快照）");
Console.WriteLine($"[RoomServer] buildHash={ServerHost.ServerBuildHash}（源码内容哈希——Sim 或协议一改即变）");

// 玩法数值：读 gen.bat Pass 1b 的 json 产物（与客户端同一表源；缺表/坏表 → fail-fast，不带着错数值跑权威局）
CombatNumbers.LoadFromRepo();

using var transport = new LiteNet.Transport.KcpTransportServer();
using var host = new ServerHost(transport, config, ticketValidator, audience);
host.Ops.PrintEnabled = !quiet;

var loop = new ServerLoop(host);
host.LoopStats = loop.Stats;                    // Ops 行带上节拍/掉债观测（常驻过载时可见）
if (durationMs > 0)
{
    loop.Run(durationMs);                       // 验收形态：跑满时长即退出
    var stats = loop.Stats;
    Console.WriteLine($"[RoomServer] 跑满 {durationMs}ms：帧号={host.Room.AuthSim.Frame} ticks={stats.Ticks} " +
        $"掉时债={stats.DroppedTimeMs}ms 放弃追帧={stats.CatchUpAbandoned}");
}
else
{
    loop.Start();                               // 常驻形态
    Console.WriteLine("[RoomServer] 常驻中（Ctrl+C 退出）");
    Thread.Sleep(Timeout.Infinite);
}
