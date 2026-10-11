using System.Threading.Tasks;
using MetaServer.Contracts.Auth;
using MetaServer.Modules.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MetaServer
{
    /// <summary>Auth 首批 HTTP 入口：游客登录。正式错误形状统一为 <see cref="MetaError"/>，
    /// **构造与状态码经 <see cref="MetaResults"/> 单源**（P2：样板不再散在各端点）。</summary>
    public static class AuthEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost("/auth/guest", async (HttpContext context) =>
            {
                GuestLoginUseCase useCase = context.RequestServices.GetService<GuestLoginUseCase>();
                if (useCase == null)
                {
                    // 功能关闭分两因（客户端处置不同）：已配签名密钥但存储缺席（等存储恢复）
                    // vs 未配签名密钥（这一功能根本没开）。
                    bool authConfigured = context.RequestServices.GetService<AccessTokenService>() != null;
                    return MetaResults.Disabled(
                        authConfigured ? AuthErrorCodes.StoreUnavailable : AuthErrorCodes.AuthDisabled,
                        AuthErrorCodes.MessageKeyUnavailable);
                }

                // 解析收口：坏 JSON / 非 JSON content-type / 形状解码失败 → null（400），不 500
                GuestLoginCommand command = await MetaResults.TryReadJsonAsync<GuestLoginCommand>(context);
                if (command == null)
                {
                    return MetaResults.InvalidRequest(
                        AuthErrorCodes.InvalidRequest, AuthErrorCodes.MessageKeyInvalidRequest);
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
                        return MetaResults.InvalidRequest(rejected.ErrorCode, rejected.MessageKey);
                    default:
                        // StoreUnavailable 与理论兜底同形：503 不可用（重试安全——登录幂等）
                        return MetaResults.Disabled(
                            AuthErrorCodes.StoreUnavailable, AuthErrorCodes.MessageKeyUnavailable);
                }
            });
        }
    }
}
