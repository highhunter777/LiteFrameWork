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
    /// 样例命令 HTTP 入口（《框架先行》样例⑤"简单测试命令 → 实际持久化确认 → 重复请求/故障 → 进程恢复"；
    /// Meta 专项 §14 L3"真实 HTTP + Mongo 测试容器"）。**非业务端点**——G3 的 Auth/Lobby/Profile
    /// 不以此为基础扩展。样例链路出生产构建归 P3（审计 §三：它在生产构建里没有消费者）。
    ///
    /// **响应形状（P2 批二收口，审计"同一 503 三种体"）**：本端点的**错误体**并入全服单源
    /// <see cref="MetaResults"/> 的 <c>{code, messageKey, args}</c>——"存储未配置/存储未确认/载荷非法"
    /// 三类在 Lobby/Settlement/样例三处现在是**同一个体**（客户端按 <c>code</c> 分支，不必先猜端点）。
    /// 代码风格与 <see cref="SettlementEndpoints"/> 逐项对齐（同一个 <see cref="ProfileErrorCodes"/> 表，
    /// 因为样例操作的就是同一张结算账本）。
    ///
    /// **一处刻意不并**：<c>409 revision-conflict</c> 走 <see cref="SampleConflict"/>——它是**领域体**
    /// 而非错误体（形如业务侧 409 的 <c>MatchSettlementResponse</c>：冲突项必须携带 expected/actual
    /// **数字**供调用方修正修订号，压进 <c>args</c> 字符串会降契约质量）。判据：
    /// **协议级错误（4xx/5xx 的"请求没被受理"）→ {code,messageKey,args}；领域结果（含 409 冲突）→ 领域 DTO。**
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
