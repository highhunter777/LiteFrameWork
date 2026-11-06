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
