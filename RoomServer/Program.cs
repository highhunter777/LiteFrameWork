using System;
using System.Threading;
using LiteSim;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;

// RoomServer 入口（M10：批② 权威循环 + 批③ 快照/回溯/Ops）。
// MVP 参数固定（端口 17777 / Room-A / 2 人房）；节拍由 ServerLoop 绝对锚定（60Hz，防漂移累积）。
// 常用命令行：--port <n> / --room <id> / --players <n>（4 人房）/ --seed <n>（固定 seed，确定性验收）/
//            --duration <ms>（跑满即退出，验收脚本用）/ --quiet（关 Ops 打印）。
// 装配参数一律收口到 RoomConfig（2026-09-19 审计建议 2/3）——房间容量与房间号不再写死。
var config = RoomConfig.Default();
long durationMs = 0;
bool quiet = false;

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
    }
}

Console.WriteLine($"[RoomServer] 启动（端口 {config.Port} / 房间 {config.RoomId} / {config.ExpectedPlayers} 人房 / {SimConfig.TickRate}Hz 权威步 / {SimConfig.SnapshotHz}Hz 快照）");
Console.WriteLine($"[RoomServer] buildHash={ServerHost.ServerBuildHash}（源码内容哈希——Sim 或协议一改即变）");

// 玩法数值：读 gen.bat Pass 1b 的 json 产物（与客户端同一表源；缺表/坏表 → fail-fast，不带着错数值跑权威局）
CombatNumbers.LoadFromRepo();

using var transport = new LiteNet.Transport.KcpTransportServer();
using var host = new ServerHost(transport, config);
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
