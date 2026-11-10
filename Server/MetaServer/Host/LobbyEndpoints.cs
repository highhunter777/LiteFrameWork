using System;
using MetaServer.Contracts.Auth;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Auth;
using MetaServer.Modules.Lobby;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using InstanceRegisterResponse = MetaWire.InstanceRegisterResponse;
using JoinTicketResponse = MetaWire.JoinTicketInfo;
using LobbyRoomsResponse = MetaWire.RoomsResponse;

using MetaWire;
namespace MetaServer
{
    /// <summary>
    /// Lobby HTTP 入口（《Meta 服务专项设计》§7、《上云测试专项设计》§2）：实例注册/心跳、 Join Ticket 签发、房间状态投影查询。
    /// </summary>
    public static class LobbyEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost(MetaWireEndpoints.InstanceRegister, async (HttpContext context) =>
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
                        return MetaResults.Ok(new InstanceRegisterResponse
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

            app.MapPost(MetaWireEndpoints.JoinTicket, async (HttpContext context) =>
            {
                JoinTicketIssueUseCase useCase = context.RequestServices.GetService<JoinTicketIssueUseCase>();
                AccessTokenService tokens = context.RequestServices.GetService<AccessTokenService>();
                if (useCase == null || tokens == null) return Disabled();

                IResult denied = InstanceKeyAuth.TryValidateToken(context, tokens, out AccessTokenFormat.Claims claims);
                if (denied != null) return denied;

                JoinTicketCommand command = await MetaResults.TryReadJsonAsync<JoinTicketCommand>(context);
                if (command == null)
                {
                    return MetaResults.InvalidRequest(
                        LobbyErrorCodes.InvalidRequest, LobbyErrorCodes.MessageKeyInvalidRequest);
                }

                JoinTicketIssueUseCase.Result result = useCase.Issue(claims.AccountId, command);
                return TicketIssueResult(result, () =>
                    context.RequestServices.GetRequiredService<Ops>().CountTicketIssued());
            });

            // 创建房间（房主开局批）：服务端出唯一新号并按它签发进房票据——客户端不发明房号
            // （响应形状 = join-ticket 同款；房间实体随首入座者物化，开房者=房主）。
            app.MapPost(MetaWireEndpoints.CreateRoom, async (HttpContext context) =>
            {
                CreateRoomUseCase useCase = context.RequestServices.GetService<CreateRoomUseCase>();
                AccessTokenService tokens = context.RequestServices.GetService<AccessTokenService>();
                if (useCase == null || tokens == null) return Disabled();

                IResult denied = InstanceKeyAuth.TryValidateToken(context, tokens, out AccessTokenFormat.Claims claims);
                if (denied != null) return denied;

                CreateRoomCommand command = await MetaResults.TryReadJsonAsync<CreateRoomCommand>(context);
                if (command == null)
                {
                    return MetaResults.InvalidRequest(
                        LobbyErrorCodes.InvalidRequest, LobbyErrorCodes.MessageKeyInvalidRequest);
                }

                JoinTicketIssueUseCase.Result result = useCase.Create(claims.AccountId, command);
                return TicketIssueResult(result, () =>
                    context.RequestServices.GetRequiredService<Ops>().CountRoomCreated());
            });

            app.MapGet(MetaWireEndpoints.RoomsAll, (HttpContext context) =>
            {
                InstanceRegistry registry = context.RequestServices.GetService<InstanceRegistry>();
                if (registry == null) return Disabled();

                MetaConfig meta = context.RequestServices.GetRequiredService<IOptions<MetaConfig>>().Value;
                if (!InstanceKeyAuth.Matches(context, meta.LobbyInstanceKeyBase64)) return InstanceUnauthorized();

                return MetaResults.Ok(new LobbyRoomsResponse { Rooms = registry.ProjectRooms() });
            });

            // 房间列表批：**玩家可见**的实例+房间明细投影（Bearer = 玩家访问令牌——与票据签发同凭据类别； 客户端据此发现"哪台服务器有哪些房间在等待"，挑 roomId → 换票 → 进房）。
            app.MapGet(MetaWireEndpoints.RoomsPublic, (HttpContext context) =>
            {
                InstanceRegistry registry = context.RequestServices.GetService<InstanceRegistry>();
                AccessTokenService tokens = context.RequestServices.GetService<AccessTokenService>();
                if (registry == null || tokens == null) return Disabled();

                IResult denied = InstanceKeyAuth.TryValidateToken(context, tokens, out AccessTokenFormat.Claims claims);
                if (denied != null) return denied;
                _ = claims;                                  // 鉴权即用（不按账号维度过滤——实例/房间不含个人数据）

                return MetaResults.Ok(new LobbyRoomsResponse { Rooms = registry.ProjectRooms() });
            });
        }

        /// <summary>
        /// 签发类端点的结果映射（join-ticket / create-room 共用——两处同为"按号签发"语义，映射必须 一致，单源防漂移）：Accepted → 票据响应（先触发调用方计数）。
        /// </summary>
        private static IResult TicketIssueResult(JoinTicketIssueUseCase.Result result, Action onAccepted)
        {
            switch (result)
            {
                case JoinTicketIssueUseCase.Result.Accepted accepted:
                    onAccepted();
                    return MetaResults.Ok(new JoinTicketResponse
                    {
                        InstanceId = accepted.InstanceId,
                        Address = accepted.Address,
                        RoomId = accepted.RoomId,
                        Ticket = accepted.Ticket,
                        ExpiresAtMs = accepted.ExpiresAtMs,
                    });
                case JoinTicketIssueUseCase.Result.Rejected rejected:
                    // 版本不符 409 / 越权（测试房口令）403 / 其余 400——三者处置不同，不合并
                    int status = rejected.ErrorCode == LobbyErrorCodes.VersionMismatch
                        ? StatusCodes.Status409Conflict
                        : rejected.ErrorCode == LobbyErrorCodes.Forbidden
                            ? StatusCodes.Status403Forbidden
                            : StatusCodes.Status400BadRequest;
                    return MetaResults.Error(status, rejected.ErrorCode, rejected.MessageKey);
                default:
                    return MetaResults.Error(StatusCodes.Status503ServiceUnavailable,
                        LobbyErrorCodes.NoCapacity, LobbyErrorCodes.MessageKeyUnavailable);
            }
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
