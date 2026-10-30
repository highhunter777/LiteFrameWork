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

using MatchQueryResponse = MetaWire.MatchesResponse;
using MatchSummaryView = MetaWire.MatchRecordInfo;
// 提交形状在 MetaWire；服务端保留继承壳（见 ProfileContracts），此处显式指向本服务端类型，
// 避免 `using MetaWire;` 与 `using MetaServer.Contracts.Profile;` 造成 CS0104。
using MatchResultSubmission = MetaServer.Contracts.Profile.MatchResultSubmission;

using MetaWire;
namespace MetaServer
{
    /// <summary>
    /// 结算 HTTP 入口（《上云测试专项设计》§2/§3 批A；《Meta 服务专项设计》§8.2）：
    /// 对局结果提交（幂等落库＝归档面 + 账本面）与按账号查询。
    ///
    /// 鉴权分两类（与 Lobby 同款纪律——§12 中间件链的鉴权段在本层显式化）：
    /// - `POST /matches/result`：**结算实例密钥**（房间侧提交管道；**与 Lobby 注册分权**，P1②）；
    /// - `GET /matches`：**访问令牌**（游客登录签发的 v1 令牌——按令牌账号查询，不接收账号参数）。
    ///
    /// 幂等语义：一局一条（MatchId 唯一）——重复提交同一局返回 200 + <c>duplicate:true</c>（归档面命中）
    /// 且逐玩家账本项 duplicate 命中，提交方据此标记本地 Outbox 完成（§6 验收"同一结果重复提交
    /// 100 次只落一条"）。任一玩家 CAS conflict 时整响应 409（已处理项照常携带，整包重试收敛）。
    ///
    /// **错误体与解析经 <see cref="MetaResults"/> 单源**（P2）：本类不再持有
    /// `Disabled/InstanceUnauthorized/StoreUnconfirmed` 私有助手（与 Lobby 双份同构的那三个）。
    /// </summary>
    public static class SettlementEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost(MetaWireEndpoints.MatchesResult, async (HttpContext context) =>
            {
                ApplyMatchResultUseCase useCase = context.RequestServices.GetService<ApplyMatchResultUseCase>();
                if (useCase == null) return Disabled();

                MetaConfig meta = context.RequestServices.GetRequiredService<IOptions<MetaConfig>>().Value;
                // **结算凭据与 Lobby 注册凭据分权**（P1②）：/matches/result 写的是账本，
                // 不该由"能注册实例"这一权限面直接覆盖；未配置结算凭据 = 该端点不提供（稳定 503）。
                if (string.IsNullOrWhiteSpace(meta.SettlementInstanceKeyBase64)) return Disabled();
                if (!InstanceKeyAuth.Matches(context, meta.SettlementInstanceKeyBase64))
                    return InstanceUnauthorized();

                // 解析收口：坏 JSON / 非 JSON content-type / 形状解码失败 → null（由用例按字段校验收口）
                MatchResultSubmission submission = await MetaResults.TryReadJsonAsync<MatchResultSubmission>(context);

                ApplyMatchResultUseCase.Result result =
                    await useCase.ExecuteAsync(submission, context.RequestAborted);
                switch (result)
                {
                    case ApplyMatchResultUseCase.Result.Processed processed:
                        Ops ops = context.RequestServices.GetRequiredService<Ops>();
                        if (processed.Duplicate) ops.CountMatchResultDuplicate();
                        else ops.CountMatchResultStore();
                        return MetaResults.Ok(new MatchSettlementResponse
                        {
                            MatchId = processed.MatchId,
                            Duplicate = processed.Duplicate,
                            Results = processed.Outcomes,
                        }, statusCode: processed.HasConflict
                            ? StatusCodes.Status409Conflict
                            : StatusCodes.Status200OK);
                    case ApplyMatchResultUseCase.Result.Rejected rejected:
                        // 字段级错误回显（错误文案只含字段名与上界数字，不含字段原值）
                        return MetaResults.InvalidRequest(
                            ProfileErrorCodes.InvalidRequest, ProfileErrorCodes.MessageKeyInvalidRequest,
                            rejected.Errors);
                    default:
                        return StoreUnconfirmed();
                }
            });

            app.MapGet(MetaWireEndpoints.Matches, async (HttpContext context) =>
            {
                IMatchResultArchive archive = context.RequestServices.GetService<IMatchResultArchive>();
                AccessTokenService tokens = context.RequestServices.GetService<AccessTokenService>();
                if (archive == null || tokens == null) return Disabled();

                string token = InstanceKeyAuth.ReadBearer(context);
                AccessTokenFormat.Rejection rejection = tokens.Validate(token, out AccessTokenFormat.Claims claims);
                if (rejection != AccessTokenFormat.Rejection.None)
                {
                    // 拒绝分类进 args（非敏感）
                    return MetaResults.Error(StatusCodes.Status401Unauthorized,
                        AuthErrorCodes.Unauthorized, AuthErrorCodes.MessageKeyUnauthorized, rejection.ToString());
                }

                // 分页边界进配置（P2 批二：此前是端点里的字面量常量）；每条请求读一次（IOptions 缓存实例）
                MetaConfig meta = context.RequestServices.GetRequiredService<IOptions<MetaConfig>>().Value;
                int limit = meta.SettlementDefaultQueryLimit;
                string rawLimit = context.Request.Query["limit"];
                if (!string.IsNullOrEmpty(rawLimit))
                {
                    if (!int.TryParse(rawLimit, out limit) || limit < 1 || limit > meta.SettlementMaxQueryLimit)
                    {
                        return MetaResults.InvalidRequest(
                            ProfileErrorCodes.InvalidRequest, ProfileErrorCodes.MessageKeyInvalidRequest,
                            "limit 必须在 1.." + meta.SettlementMaxQueryLimit);
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
                return MetaResults.Ok(new MatchQueryResponse { Matches = views });
            });
        }

        private static long ToUnixMs(DateTime utc)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        /// <summary>功能未配置（服务缺席）——稳定 503，不回退假数据（§10）。</summary>
        private static IResult Disabled()
        {
            return MetaResults.Disabled(ProfileErrorCodes.Disabled, ProfileErrorCodes.MessageKeyUnavailable);
        }

        private static IResult InstanceUnauthorized()
        {
            return MetaResults.InstanceUnauthorized(
                ProfileErrorCodes.InstanceUnauthorized, ProfileErrorCodes.MessageKeyUnauthorized);
        }

        /// <summary>存储未确认（重试安全——幂等保证重试收敛；§8.2）。</summary>
        private static IResult StoreUnconfirmed()
        {
            return MetaResults.StoreUnconfirmed(
                ProfileErrorCodes.StoreUnconfirmed, ProfileErrorCodes.MessageKeyUnavailable);
        }
    }
}
