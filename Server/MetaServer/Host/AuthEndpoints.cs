using System;
using System.Text.Json;
using System.Threading.Tasks;
using MetaServer.Contracts.Auth;
using MetaServer.Modules.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MetaServer
{
    /// <summary>Auth 首批 HTTP 入口：游客登录。正式错误形状统一为 MetaError。</summary>
    public static class AuthEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost("/auth/guest", async (HttpContext context) =>
            {
                GuestLoginUseCase useCase = context.RequestServices.GetService<GuestLoginUseCase>();
                if (useCase == null)
                {
                    bool authConfigured = context.RequestServices.GetService<AccessTokenService>() != null;
                    return Results.Json(new MetaError
                    {
                        Code = authConfigured ? AuthErrorCodes.StoreUnavailable : AuthErrorCodes.AuthDisabled,
                        MessageKey = AuthErrorCodes.MessageKeyUnavailable,
                        Args = Array.Empty<string>(),
                    }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                GuestLoginCommand command;
                try
                {
                    command = await context.Request.ReadFromJsonAsync<GuestLoginCommand>();
                }
                catch (JsonException)
                {
                    command = null;
                }

                if (command == null)
                {
                    return Results.Json(new MetaError
                    {
                        Code = AuthErrorCodes.InvalidRequest,
                        MessageKey = AuthErrorCodes.MessageKeyInvalidRequest,
                        Args = Array.Empty<string>(),
                    }, statusCode: StatusCodes.Status400BadRequest);
                }

                GuestLoginUseCase.Result result = await useCase.ExecuteAsync(command, context.RequestAborted);
                switch (result)
                {
                    case GuestLoginUseCase.Result.Accepted accepted:
                        return Results.Json(new GuestLoginResponse
                        {
                            AccountId = accepted.AccountId,
                            AccessToken = accepted.AccessToken,
                            TokenType = "Bearer",
                            ExpiresAtMs = accepted.ExpiresAtMs,
                        });
                    case GuestLoginUseCase.Result.Rejected rejected:
                        return Results.Json(new MetaError
                        {
                            Code = rejected.ErrorCode,
                            MessageKey = rejected.MessageKey,
                            Args = Array.Empty<string>(),
                        }, statusCode: StatusCodes.Status400BadRequest);
                    case GuestLoginUseCase.Result.StoreUnavailable:
                        return Results.Json(new MetaError
                        {
                            Code = AuthErrorCodes.StoreUnavailable,
                            MessageKey = AuthErrorCodes.MessageKeyUnavailable,
                            Args = Array.Empty<string>(),
                        }, statusCode: StatusCodes.Status503ServiceUnavailable);
                    default:
                        return Results.Json(new MetaError
                        {
                            Code = AuthErrorCodes.StoreUnavailable,
                            MessageKey = AuthErrorCodes.MessageKeyUnavailable,
                            Args = Array.Empty<string>(),
                        }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });
        }
    }
}
