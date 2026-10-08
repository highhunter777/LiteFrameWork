using System;
using System.Security.Cryptography;
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
    /// </summary>
    public static class LobbyEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost("/lobby/instances/register", async (HttpContext context) =>
            {
                InstanceRegistry registry = context.RequestServices.GetService<InstanceRegistry>();
                if (registry == null) return LobbyDisabled();

                MetaConfig meta = context.RequestServices.GetRequiredService<IOptions<MetaConfig>>().Value;
                if (!InstanceKeyMatches(context, meta.LobbyInstanceKeyBase64))
                    return InstanceUnauthorized();

                InstanceRegisterCommand command;
                try
                {
                    command = await context.Request.ReadFromJsonAsync<InstanceRegisterCommand>();
                }
                catch (OperationCanceledException)
                {
                    throw;                                        // 请求中止随管道传播
                }
                catch (Exception)
                {
                    // 坏 JSON / 非 JSON content-type / 超长解码失败——一律按无效请求收口（不 500）
                    command = null;
                }

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
                        return Results.Json(new MetaError
                        {
                            Code = LobbyErrorCodes.RegistryFull,
                            MessageKey = LobbyErrorCodes.MessageKeyUnavailable,
                            Args = Array.Empty<string>(),
                        }, statusCode: StatusCodes.Status503ServiceUnavailable);
                    default:
                        // 字段错误回显**字段名**（非敏感）帮助部署排障；不回显字段值
                        return Results.Json(new MetaError
                        {
                            Code = LobbyErrorCodes.InvalidRequest,
                            MessageKey = LobbyErrorCodes.MessageKeyInvalidRequest,
                            Args = new[] { reason ?? "payload" },
                        }, statusCode: StatusCodes.Status400BadRequest);
                }
            });

            app.MapPost("/lobby/join-ticket", async (HttpContext context) =>
            {
                JoinTicketIssueUseCase useCase = context.RequestServices.GetService<JoinTicketIssueUseCase>();
                AccessTokenService tokens = context.RequestServices.GetService<AccessTokenService>();
                if (useCase == null || tokens == null) return LobbyDisabled();

                string token = ReadBearer(context);
                AccessTokenFormat.Rejection rejection = tokens.Validate(token, out AccessTokenFormat.Claims claims);
                if (rejection != AccessTokenFormat.Rejection.None)
                {
                    // 拒绝分类进 args（非敏感）：稳定错误码 + 分类便于对数，票据原文绝不回显
                    return Results.Json(new MetaError
                    {
                        Code = AuthErrorCodes.Unauthorized,
                        MessageKey = AuthErrorCodes.MessageKeyUnauthorized,
                        Args = new[] { rejection.ToString() },
                    }, statusCode: StatusCodes.Status401Unauthorized);
                }

                JoinTicketCommand command;
                try
                {
                    command = await context.Request.ReadFromJsonAsync<JoinTicketCommand>();
                }
                catch (OperationCanceledException)
                {
                    throw;                                        // 请求中止随管道传播
                }
                catch (Exception)
                {
                    // 坏 JSON / 非 JSON content-type / 超长解码失败——一律按无效请求收口（不 500）
                    command = null;
                }

                if (command == null)
                {
                    return Results.Json(new MetaError
                    {
                        Code = LobbyErrorCodes.InvalidRequest,
                        MessageKey = LobbyErrorCodes.MessageKeyInvalidRequest,
                        Args = Array.Empty<string>(),
                    }, statusCode: StatusCodes.Status400BadRequest);
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
                        return Results.Json(new MetaError
                        {
                            Code = rejected.ErrorCode,
                            MessageKey = rejected.MessageKey,
                            Args = Array.Empty<string>(),
                        }, statusCode: rejected.ErrorCode == LobbyErrorCodes.VersionMismatch
                            ? StatusCodes.Status409Conflict
                            : StatusCodes.Status400BadRequest);
                    default:
                        return Results.Json(new MetaError
                        {
                            Code = LobbyErrorCodes.NoCapacity,
                            MessageKey = LobbyErrorCodes.MessageKeyUnavailable,
                            Args = Array.Empty<string>(),
                        }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });

            app.MapGet("/lobby/rooms", (HttpContext context) =>
            {
                InstanceRegistry registry = context.RequestServices.GetService<InstanceRegistry>();
                if (registry == null) return LobbyDisabled();

                MetaConfig meta = context.RequestServices.GetRequiredService<IOptions<MetaConfig>>().Value;
                if (!InstanceKeyMatches(context, meta.LobbyInstanceKeyBase64))
                    return InstanceUnauthorized();

                return Results.Json(new LobbyRoomsResponse { Rooms = registry.ProjectRooms() });
            });
        }

        /// <summary>功能未配置（服务缺席）——稳定 503，不回退假数据（§10）。</summary>
        private static IResult LobbyDisabled()
        {
            return Results.Json(new MetaError
            {
                Code = LobbyErrorCodes.Disabled,
                MessageKey = LobbyErrorCodes.MessageKeyUnavailable,
                Args = Array.Empty<string>(),
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        private static IResult InstanceUnauthorized()
        {
            return Results.Json(new MetaError
            {
                Code = LobbyErrorCodes.InstanceUnauthorized,
                MessageKey = LobbyErrorCodes.MessageKeyUnauthorized,
                Args = Array.Empty<string>(),
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        /// <summary>实例密钥比对：Bearer 原文按 Base64 解码后**常量时间**比较（防时序侧信道）。</summary>
        private static bool InstanceKeyMatches(HttpContext context, string expectedBase64)
        {
            string provided = ReadBearer(context);
            if (provided == null || string.IsNullOrWhiteSpace(expectedBase64)) return false;
            byte[] expected;
            byte[] actual;
            try
            {
                expected = Convert.FromBase64String(expectedBase64);
                actual = Convert.FromBase64String(provided);
            }
            catch (FormatException)
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        /// <summary>读取 Bearer 凭据（缺失/坏形状返回 null——统一走鉴权失败路径，不区分对待）。</summary>
        private static string ReadBearer(HttpContext context)
        {
            string header = context.Request.Headers["Authorization"];
            const string prefix = "Bearer ";
            if (string.IsNullOrEmpty(header) || header.Length <= prefix.Length
                || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            return header.Substring(prefix.Length).Trim();
        }
    }
}
