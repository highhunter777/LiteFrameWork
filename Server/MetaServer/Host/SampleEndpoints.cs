using System;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MetaServer.Contracts.Profile;
using MetaServer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MetaServer
{
    /// <summary>
    /// 样例命令 HTTP 入口（《框架先行》样例⑤"简单测试命令 → 实际持久化确认 → 重复请求/故障 → 进程恢复"； Meta 专项 §14 L3"真实 HTTP + Mongo 测试容器"）。
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
                    // 与 Lobby/Settlement 的"功能未配置"同一个体（P2 批二前这里是样例专用的
                    // {code:"store-not-configured"}——同语义在三条端点上三种形状）
                    return MetaResults.Disabled(
                        ProfileErrorCodes.Disabled, ProfileErrorCodes.MessageKeyUnavailable);
                }

                // 解析收口与其它端点同源（P2 批一）：坏 JSON / 非 JSON content-type / 形状失败一律 400
                SampleSettlementCommand command = await MetaResults.TryReadJsonAsync<SampleSettlementCommand>(context);
                if (command == null)
                {
                    return MetaResults.InvalidRequest(
                        ProfileErrorCodes.InvalidRequest, ProfileErrorCodes.MessageKeyInvalidRequest);
                }

                SampleCommandResult result = await useCase.ExecuteAsync(command, context.RequestAborted);
                return result switch
                {
                    SampleCommandResult.Accepted accepted => MetaResults.Ok(
                        new SampleAccepted { Status = "accepted", Record = accepted.Record }),
                    SampleCommandResult.DuplicateHit duplicate => MetaResults.Ok(
                        new SampleAccepted { Status = "duplicate", Record = duplicate.Record }),
                    // 领域体（见类注释的判据）：冲突必须带 expected/actual 数字
                    SampleCommandResult.Conflict conflict => MetaResults.Ok(
                        new SampleConflict
                        {
                            Code = "revision-conflict",
                            Expected = conflict.Expected,
                            Actual = conflict.Actual,
                        },
                        statusCode: StatusCodes.Status409Conflict),
                    // 字段级错误一次报全（错误文案只含字段名与上界，不回显字段值）——args 即清单
                    SampleCommandResult.Rejected rejected => MetaResults.InvalidRequest(
                        ProfileErrorCodes.InvalidRequest, ProfileErrorCodes.MessageKeyInvalidRequest,
                        rejected.Errors),
                    // 未确认：与业务侧同体同语义（不冒充成功；重试安全）。内部原因**不进响应体**。
                    _ => MetaResults.StoreUnconfirmed(
                        ProfileErrorCodes.StoreUnconfirmed, ProfileErrorCodes.MessageKeyUnavailable),
                };
            });
        }

        /// <summary>成功/幂等命中的**领域体**（样例专用形状——随样例链路出生产构建一并消失）。</summary>
        public sealed class SampleAccepted
        {
            public string Status { get; set; }

            public SettlementRecord Record { get; set; }
        }

        /// <summary>修订冲突的**领域体**（409；expected/actual 为数字——见类注释的判据）。</summary>
        public sealed class SampleConflict
        {
            public string Code { get; set; }

            public long Expected { get; set; }

            public long Actual { get; set; }
        }
    }
}
