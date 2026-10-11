using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MetaServer
{
    /// <summary>
    /// 健康检查端点（§11.3）。
    ///
    /// **挂载形态统一（P2 批二）**：此前它由 <see cref="MetaHost"/> 的私有方法挂载，业务端点由
    /// <c>XxxEndpoints.Map</c> 挂载——"新增端点该放哪、契约去哪读"要按端点种类分别判断，
    /// 且健康端点的接线无法与业务端点一起被扫读。统一为 <c>XxxEndpoints.Map(app)</c> 后，
    /// 宿主只留一行挂载序。
    ///
    /// 语义分工是硬要求：`/live` 只证进程事件循环仍能响应；`/ready` 证配置与依赖正常**且未处于 drain**。
    /// 二者不可合并——把 drain 混进 live 会让编排器误杀进程而不是摘流量。
    ///
    /// M0-c 批二起 `/ready` 包含真实依赖：配置了存储时 ping Mongo（§10"/ready 的判定必须包含
    /// 真实依赖（Mongo 可达）"）；功能关闭（未配置连接串）时无依赖可检，仍返回 ready。
    /// ping 超时可配（硬化批 2026-10-03；原硬编码 2s）——健康检查必须 bounded。
    /// </summary>
    public static class HealthEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/live", () => Results.Text("live", "text/plain"));

            app.MapGet("/ready", async Task<IResult> (HttpContext context, Ops ops) =>
            {
                if (ops.Draining)
                {
                    return Results.Text("draining", "text/plain",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                IMongoDatabase database = context.RequestServices.GetService<IMongoDatabase>();
                if (database != null)
                {
                    // ping 超时可配（硬化批 2026-10-03；原硬编码 2s）——健康检查必须 bounded
                    MetaConfig meta = context.RequestServices
                        .GetRequiredService<IOptions<MetaConfig>>().Value;
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromMilliseconds(meta.ReadyPingTimeoutMs));
                    try
                    {
                        await database.RunCommandAsync(
                            new BsonDocumentCommand<BsonDocument>(new BsonDocument("ping", 1)),
                            cancellationToken: timeout.Token);
                    }
                    catch
                    {
                        return Results.Text("store-unreachable", "text/plain",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                }

                return Results.Text("ready", "text/plain");
            });

            app.MapGet("/metrics", (Ops ops) => Results.Text(ops.FormatMetrics(), "text/plain"));
        }
    }
}
