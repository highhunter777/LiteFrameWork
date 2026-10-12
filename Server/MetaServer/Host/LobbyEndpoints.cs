using MetaServer.Contracts.Auth;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Auth;
using MetaServer.Modules.Lobby;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MetaServer
{
    /// <summary>
    /// Lobby HTTP 入口（《Meta 服务专项设计》§7、《上云测试专项设计》§2）：实例注册/心跳、
    /// Join Ticket 签发、房间状态投影查询。
    ///
    /// 鉴权分两类（§12 中间件链的鉴权段在本层显式化——Lobby 为首批受保护端点）：
    /// - 实例注册/查询：**实例密钥**（Bearer = Meta 配置的 Base64 串，常量时间比较）；
    /// - 票据签发：**访问令牌**（Bearer = 游客登录签发的 v1 令牌，消费 AccessTokenService）。
    /// 未配置对应功能（服务未注册）→ 稳定 <c>lobby.disabled</c>（§10"拒绝相应功能"）。
    ///
    /// **错误体与解析经 <see cref="MetaResults"/> 单源**（P2）：本类不再写 `new MetaError{…}` 样板，
    /// 也不再持有 `LobbyDisabled/InstanceUnauthorized/InstanceKeyMatches/ReadBearer` 这类
    /// 一行转发包装（凭据比对与 Bearer 读取的归属是 <see cref="InstanceKeyAuth"/>）。
    /// </summary>
    public static class LobbyEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost("/lobby/instances/register", async (HttpContext context) =>
            {
                InstanceRegistry registry = context.RequestServices.GetService<InstanceRegistry>();
                if (registry == null) return Disabled();

                MetaConfig meta = context.RequestServices.GetRequiredService<IOptions<MetaConfig>>().Value;
                if (!InstanceKeyAuth.Matches(context, meta.LobbyInstanceKeyBase64)) return InstanceUnauthorized();

                InstanceRegisterCommand command = await MetaResults.TryReadJsonAsync<InstanceRegisterCommand>(context);
                InstanceRegistry.RegisterResult result = registry.Register(command, out string reason);
                switch (result)
                {
                    case InstanceRegistry.RegisterResult.Accepted:
                        context.RequestServices.GetRequiredService<Ops>().CountInstanceRegister();
                        return Results.Json(new InstanceRegisterResponse
                        {
                            HeartbeatTtlMs = registry.HeartbeatTtlMs,
                        });
                    case InstanceRegistry.RegisterResult.RegistryFull:
                        return MetaResults.Error(StatusCodes.Status503ServiceUnavailable,
                            LobbyErrorCodes.RegistryFull, LobbyErrorCodes.MessageKeyUnavailable);
                    default:
                        // 字段错误回显**字段名**（非敏感）帮助部署排障；不回显字段值
                        return MetaResults.InvalidRequest(
                            LobbyErrorCodes.InvalidRequest, LobbyErrorCodes.MessageKeyInvalidRequest,
                            reason ?? "payload");
                }
            });

            app.MapPost("/lobby/join-ticket", async (HttpContext context) =>
            {
                JoinTicketIssueUseCase useCase = context.RequestServices.GetService<JoinTicketIssueUseCase>();
                AccessTokenService tokens = context.RequestServices.GetService<AccessTokenService>();
                if (useCase == null || tokens == null) return Disabled();

                string token = InstanceKeyAuth.ReadBearer(context);
                AccessTokenFormat.Rejection rejection = tokens.Validate(token, out AccessTokenFormat.Claims claims);
                if (rejection != AccessTokenFormat.Rejection.None)
                {
                    // 拒绝分类进 args（非敏感）：稳定错误码 + 分类便于对数，票据原文绝不回显
                    return MetaResults.Error(StatusCodes.Status401Unauthorized,
                        AuthErrorCodes.Unauthorized, AuthErrorCodes.MessageKeyUnauthorized, rejection.ToString());
                }

                JoinTicketCommand command = await MetaResults.TryReadJsonAsync<JoinTicketCommand>(context);
                if (command == null)
                {
                    return MetaResults.InvalidRequest(
                        LobbyErrorCodes.InvalidRequest, LobbyErrorCodes.MessageKeyInvalidRequest);
                }

                JoinTicketIssueUseCase.Result result = useCase.Issue(claims.AccountId, command);
                switch (result)
                {
                    case JoinTicketIssueUseCase.Result.Accepted accepted:
                        context.RequestServices.GetRequiredService<Ops>().CountTicketIssued();
                        return Results.Json(new JoinTicketResponse
                        {
                            InstanceId = accepted.InstanceId,
                            Address = accepted.Address,
                            RoomId = accepted.RoomId,
                            Ticket = accepted.Ticket,
                            ExpiresAtMs = accepted.ExpiresAtMs,
                        });
                    case JoinTicketIssueUseCase.Result.Rejected rejected:
                        // 版本不符 409（换客户端构建/等新实例）区别于其它拒绝 400
                        return MetaResults.Error(
                            rejected.ErrorCode == LobbyErrorCodes.VersionMismatch
                                ? StatusCodes.Status409Conflict
                                : StatusCodes.Status400BadRequest,
                            rejected.ErrorCode, rejected.MessageKey);
                    default:
                        return MetaResults.Error(StatusCodes.Status503ServiceUnavailable,
                            LobbyErrorCodes.NoCapacity, LobbyErrorCodes.MessageKeyUnavailable);
                }
            });

            app.MapGet("/lobby/rooms", (HttpContext context) =>
            {
                InstanceRegistry registry = context.RequestServices.GetService<InstanceRegistry>();
                if (registry == null) return Disabled();

                MetaConfig meta = context.RequestServices.GetRequiredService<IOptions<MetaConfig>>().Value;
                if (!InstanceKeyAuth.Matches(context, meta.LobbyInstanceKeyBase64)) return InstanceUnauthorized();

                return Results.Json(new LobbyRoomsResponse { Rooms = registry.ProjectRooms() });
            });

            // 房间列表批：**玩家可见**的实例+房间明细投影（Bearer = 玩家访问令牌——与票据签发同凭据类别；
            // 客户端据此发现"哪台服务器有哪些房间在等待"，挑 roomId → 换票 → 进房）。
            // 字段与实例面端点同源（ProjectRooms 单次投影，含 address/buildHash——玩家进房要用）。
            app.MapGet("/lobby/rooms/public", (HttpContext context) =>
            {
                InstanceRegistry registry = context.RequestServices.GetService<InstanceRegistry>();
                AccessTokenService tokens = context.RequestServices.GetService<AccessTokenService>();
                if (registry == null || tokens == null) return Disabled();

                string token = InstanceKeyAuth.ReadBearer(context);
                AccessTokenFormat.Rejection rejection = tokens.Validate(token, out AccessTokenFormat.Claims claims);
                if (rejection != AccessTokenFormat.Rejection.None)
                {
                    return MetaResults.Error(StatusCodes.Status401Unauthorized,
                        AuthErrorCodes.Unauthorized, AuthErrorCodes.MessageKeyUnauthorized, rejection.ToString());
                }
                _ = claims;                                  // 鉴权即用（不按账号维度过滤——实例/房间不含个人数据）

                return Results.Json(new LobbyRoomsResponse { Rooms = registry.ProjectRooms() });
            });
        }

        /// <summary>功能未配置（服务缺席）——稳定 503，不回退假数据（§10）。</summary>
        private static IResult Disabled()
        {
            return MetaResults.Disabled(LobbyErrorCodes.Disabled, LobbyErrorCodes.MessageKeyUnavailable);
        }

        private static IResult InstanceUnauthorized()
        {
            return MetaResults.InstanceUnauthorized(
                LobbyErrorCodes.InstanceUnauthorized, LobbyErrorCodes.MessageKeyUnauthorized);
        }
    }
}
