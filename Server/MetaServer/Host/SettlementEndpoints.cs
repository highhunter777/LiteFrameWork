using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MetaServer.Contracts.Auth;
using MetaServer.Contracts.Persistence;
using MetaServer.Contracts.Profile;
using MetaServer.Modules.Auth;
using MetaServer.Modules.Profile;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MetaServer
{
    /// <summary>
    /// 结算 HTTP 入口（《上云测试专项设计》§2/§3 批A；《Meta 服务专项设计》§8.2）：
    /// 对局结果提交（幂等落库＝归档面 + 账本面）与按账号查询。
    ///
    /// 鉴权分两类（与 Lobby 同款纪律——§12 中间件链的鉴权段在本层显式化）：
    /// - `POST /matches/result`：**实例密钥**（房间侧提交管道；与 Lobby 注册共用凭据）；
    /// - `GET /matches`：**访问令牌**（游客登录签发的 v1 令牌——按令牌账号查询，不接收账号参数）。
    ///
    /// 幂等语义：一局一条（MatchId 唯一）——重复提交同一局返回 200 + <c>duplicate:true</c>（归档面命中）
    /// 且逐玩家账本项 duplicate 命中，提交方据此标记本地 Outbox 完成（§6 验收"同一结果重复提交
    /// 100 次只落一条"）。任一玩家 CAS conflict 时整响应 409（已处理项照常携带，整包重试收敛）。
    /// </summary>
    public static class SettlementEndpoints
    {
        /// <summary>查询条数缺省与上限（有界——§13"任何队列/缓存…必须有显式容量"同款口径）。</summary>
        public const int DefaultQueryLimit = 20;
        public const int MaxQueryLimit = 100;

        public static void Map(WebApplication app)
        {
            app.MapPost("/matches/result", async (HttpContext context) =>
            {
                ApplyMatchResultUseCase useCase = context.RequestServices.GetService<ApplyMatchResultUseCase>();
                if (useCase == null) return Disabled();

                MetaConfig meta = context.RequestServices.GetRequiredService<IOptions<MetaConfig>>().Value;
                if (string.IsNullOrWhiteSpace(meta.LobbyInstanceKeyBase64)) return Disabled();
                if (!InstanceKeyAuth.Matches(context, meta.LobbyInstanceKeyBase64))
                    return InstanceUnauthorized();

                MatchResultSubmission submission;
                try
                {
                    submission = await context.Request.ReadFromJsonAsync<MatchResultSubmission>();
                }
                catch (OperationCanceledException)
                {
                    throw;                                        // 请求中止随管道传播
                }
                catch (Exception)
                {
                    // 坏 JSON / 非 JSON content-type / 长度/形状解码失败——一律按无效请求收口（不 500）
                    submission = null;
                }

                ApplyMatchResultUseCase.Result result =
                    await useCase.ExecuteAsync(submission, context.RequestAborted);
                switch (result)
                {
                    case ApplyMatchResultUseCase.Result.Processed processed:
                        Ops ops = context.RequestServices.GetRequiredService<Ops>();
                        if (processed.Duplicate) ops.CountMatchResultDuplicate();
                        else ops.CountMatchResultStore();
                        return Results.Json(new MatchSettlementResponse
                        {
                            MatchId = processed.MatchId,
                            Duplicate = processed.Duplicate,
                            Results = processed.Outcomes,
                        }, statusCode: processed.HasConflict
                            ? StatusCodes.Status409Conflict
                            : StatusCodes.Status200OK);
                    case ApplyMatchResultUseCase.Result.Rejected rejected:
                        // 字段级错误回显（错误文案只含字段名与上界数字，不含字段原值）
                        return Results.Json(new MetaError
                        {
                            Code = ProfileErrorCodes.InvalidRequest,
                            MessageKey = ProfileErrorCodes.MessageKeyInvalidRequest,
                            Args = rejected.Errors,
                        }, statusCode: StatusCodes.Status400BadRequest);
                    default:
                        return StoreUnconfirmed();
                }
            });

            app.MapGet("/matches", async (HttpContext context) =>
            {
                IMatchResultArchive archive = context.RequestServices.GetService<IMatchResultArchive>();
                AccessTokenService tokens = context.RequestServices.GetService<AccessTokenService>();
                if (archive == null || tokens == null) return Disabled();

                string token = InstanceKeyAuth.ReadBearer(context);
                AccessTokenFormat.Rejection rejection = tokens.Validate(token, out AccessTokenFormat.Claims claims);
                if (rejection != AccessTokenFormat.Rejection.None)
                {
                    return Results.Json(new MetaError
                    {
                        Code = AuthErrorCodes.Unauthorized,
                        MessageKey = AuthErrorCodes.MessageKeyUnauthorized,
                        Args = new[] { rejection.ToString() },
                    }, statusCode: StatusCodes.Status401Unauthorized);
                }

                int limit = DefaultQueryLimit;
                string rawLimit = context.Request.Query["limit"];
                if (!string.IsNullOrEmpty(rawLimit))
                {
                    if (!int.TryParse(rawLimit, out limit) || limit < 1 || limit > MaxQueryLimit)
                    {
                        return Results.Json(new MetaError
                        {
                            Code = ProfileErrorCodes.InvalidRequest,
                            MessageKey = ProfileErrorCodes.MessageKeyInvalidRequest,
                            Args = new[] { "limit 必须在 1.." + MaxQueryLimit },
                        }, statusCode: StatusCodes.Status400BadRequest);
                    }
                }

                IReadOnlyList<AccountMatchResult> matches;
                try
                {
                    matches = await archive.ListByAccountAsync(claims.AccountId, limit, context.RequestAborted);
                }
                catch (SettlementStoreUnavailableException)
                {
                    return StoreUnconfirmed();
                }

                var views = new List<MatchSummaryView>(matches.Count);
                for (int i = 0; i < matches.Count; i++)
                {
                    AccountMatchResult m = matches[i];
                    views.Add(new MatchSummaryView
                    {
                        MatchId = m.MatchId,
                        Kills = m.Kills,
                        Deaths = m.Deaths,
                        EndReason = m.EndReason,
                        GameplayEndReason = m.GameplayEndReason,
                        FinishedAtMs = ToUnixMs(m.FinishedUtc),
                    });
                }
                return Results.Json(new MatchQueryResponse { Matches = views });
            });
        }

        private static long ToUnixMs(DateTime utc)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        /// <summary>功能未配置（服务缺席）——稳定 503，不回退假数据（§10）。</summary>
        private static IResult Disabled()
        {
            return Results.Json(new MetaError
            {
                Code = ProfileErrorCodes.Disabled,
                MessageKey = ProfileErrorCodes.MessageKeyUnavailable,
                Args = Array.Empty<string>(),
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        private static IResult InstanceUnauthorized()
        {
            return Results.Json(new MetaError
            {
                Code = ProfileErrorCodes.InstanceUnauthorized,
                MessageKey = ProfileErrorCodes.MessageKeyUnauthorized,
                Args = Array.Empty<string>(),
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        /// <summary>存储未确认（重试安全——幂等保证重试收敛；§8.2）。</summary>
        private static IResult StoreUnconfirmed()
        {
            return Results.Json(new MetaError
            {
                Code = ProfileErrorCodes.StoreUnconfirmed,
                MessageKey = ProfileErrorCodes.MessageKeyUnavailable,
                Args = Array.Empty<string>(),
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
