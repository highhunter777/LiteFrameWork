using System;
using System.Text.Json;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MetaServer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MetaServer
{
    /// <summary>
    /// 样例命令 HTTP 入口（《框架先行》样例⑤"简单测试命令 → 实际持久化确认 → 重复请求/故障 → 进程恢复"；
    /// Meta 专项 §14 L3"真实 HTTP + Mongo 测试容器"）。**非业务端点**——G3 的 Auth/Lobby/Profile
    /// 不以此为基础扩展，届时由 Contracts 层的正式 DTO/错误形状（§5.2 {code,messageKey,args}）承接。
    ///
    /// 未配置存储（功能关闭）时显式 503——拒绝相应功能，不悄悄退回替身（框架先行 §6 末条）。
    /// </summary>
    public static class SampleEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost("/sample/settlement", async (HttpContext context) =>
            {
                SettlementSampleUseCase useCase =
                    context.RequestServices.GetService<SettlementSampleUseCase>();
                if (useCase == null)
                {
                    return Results.Json(
                        new SampleError { Code = "store-not-configured" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                SampleSettlementCommand command;
                try
                {
                    command = await context.Request.ReadFromJsonAsync<SampleSettlementCommand>();
                    if (command == null)
                    {
                        return Results.Json(
                            new SampleError { Code = "invalid-json" },
                            statusCode: StatusCodes.Status400BadRequest);
                    }
                }
                catch (JsonException)
                {
                    return Results.Json(
                        new SampleError { Code = "invalid-json" },
                        statusCode: StatusCodes.Status400BadRequest);
                }

                SampleCommandResult result = await useCase.ExecuteAsync(command, context.RequestAborted);
                return result switch
                {
                    SampleCommandResult.Accepted accepted => Results.Json(
                        new SampleAccepted { Status = "accepted", Record = accepted.Record }),
                    SampleCommandResult.DuplicateHit duplicate => Results.Json(
                        new SampleAccepted { Status = "duplicate", Record = duplicate.Record }),
                    SampleCommandResult.Conflict conflict => Results.Json(
                        new SampleConflict
                        {
                            Code = "revision-conflict",
                            Expected = conflict.Expected,
                            Actual = conflict.Actual,
                        },
                        statusCode: StatusCodes.Status409Conflict),
                    SampleCommandResult.Rejected rejected => Results.Json(
                        new SampleRejected { Code = "invalid-command", Errors = rejected.Errors },
                        statusCode: StatusCodes.Status400BadRequest),
                    SampleCommandResult.Unconfirmed unconfirmed => Results.Json(
                        new SampleError { Code = "store-unavailable", Reason = unconfirmed.Reason },
                        statusCode: StatusCodes.Status503ServiceUnavailable),
                    _ => Results.Json(
                        new SampleError { Code = "store-unavailable" },
                        statusCode: StatusCodes.Status503ServiceUnavailable),
                };
            });
        }

        /// <summary>错误载荷（简化形状；正式 {code,messageKey,args} 归 G3 Contracts）。</summary>
        public sealed class SampleError
        {
            public string Code { get; set; }

            public string Reason { get; set; }
        }

        public sealed class SampleAccepted
        {
            public string Status { get; set; }

            public SettlementRecord Record { get; set; }
        }

        public sealed class SampleConflict
        {
            public string Code { get; set; }

            public long Expected { get; set; }

            public long Actual { get; set; }
        }

        public sealed class SampleRejected
        {
            public string Code { get; set; }

            public System.Collections.Generic.IReadOnlyList<string> Errors { get; set; }
        }
    }
}
