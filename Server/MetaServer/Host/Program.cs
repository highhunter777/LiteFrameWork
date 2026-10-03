using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MetaServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Meta 服务宿主入口（《Meta 服务专项设计》§4.1/§4.2/§10）。
//
// 命令行只用于**本地覆盖**（§10"命令行只用于本地覆盖"）；生产参数走版本化文件与环境变量：
//   --bind <url>  监听地址（默认 http://127.0.0.1:5000）
//
// 本批为宿主骨架：Generic Host + Options 校验 + /live /ready /metrics + 优雅关闭与 drain。
// 业务模块（Auth/Lobby/Profile）归 G3，不在此提前建空壳（§15 施工映射）。
//
// 结束码约定（服务端总设计 §12）：配置非法必须非零退出，不带默认错配置继续运行。
int exitCode = 0;

// 解析覆盖项（不在此校验——校验由 ValidateOnStart 在启动时统一执行，
// 覆盖文件/环境变量/命令行合并后的**最终生效值**；手写预校验会漏掉配置来源，见 §10）。
// 未知参数/缺值 = 配置错误，退出码 2（§12"启动失败必须返回非零退出码"——
// 静默忽略拼错的覆盖项会让"以为改了配置"成为假象；硬化批 2026-10-03 门禁化）。
var overrides = MetaHost.ParseCliOverrides(args, out string parseError);
if (parseError != null)
{
    Console.Error.WriteLine("[MetaServer] " + parseError);
    return 2;
}

WebApplication app = null;
try
{
    // 注意：ValidateOnStart 门禁可能在 **Build 阶段**就触发（Host 构造 ConsoleLifetime 时会解析
    // IOptions<HostOptions>，进而带出 MetaConfig 校验），因此 Build 与 StartAsync 必须同在
    // try 内——否则非法配置会以未处理异常逃逸，拿不到 §12 要求的明确退出码与错误清单。
    app = MetaHost.Build(args, overrides);
    MetaHost.WireGracefulShutdown(app);

    await app.StartAsync();

    // 端口 0 时由内核分配临时端口，从 IServer 取回真实地址后再打印——
    // 与测试使用同一技法（Meta 专项设计 §4.1 依据 5）。
    MetaConfig effective = app.Services.GetRequiredService<IOptions<MetaConfig>>().Value;
    var server = app.Services.GetRequiredService<IServer>();
    string bound = string.Join(",", server.Features.Get<IServerAddressesFeature>().Addresses);

    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "[MetaServer] 启动：bind={0} maxInbound={1}B shutdown={2}s build={3}",
        bound, effective.MaxInboundBytes, effective.ShutdownTimeoutSeconds, MetaHost.BuildIdentity));
    Console.WriteLine("[MetaServer] 端点：/live /ready /metrics");
    Console.WriteLine("[MetaServer] 常驻中（Ctrl+C 退出）");

    await app.WaitForShutdownAsync();
}
catch (OptionsValidationException ex)
{
    // §12"启动失败必须返回非零退出码"：一次打印全部违规项。
    Console.Error.WriteLine("[MetaServer] 配置非法，拒绝启动：");
    foreach (string failure in ex.Failures)
        Console.Error.WriteLine("  - " + failure);
    exitCode = 2;
}
catch (Exception ex)
{
    // 完整堆栈（ToString）——迁移/依赖失败排障需要原始抛点，只打 Message 会丢失定位线索
    Console.Error.WriteLine("[MetaServer] 启动失败：" + ex.ToString());
    exitCode = 1;
}
finally
{
    if (app != null)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

return exitCode;
