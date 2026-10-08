using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Auth;
using MetaServer.Contracts.Persistence;

namespace MetaServer.Modules.Auth
{
    /// <summary>
    /// 游客登录用例（《Meta 服务专项设计》§6.1 登录）：设备标识 get-or-create → 签发访问令牌。
    ///
    /// - **幂等裁判在存储**：deviceId 唯一键决定"同设备 → 同账号"（§5.3"数据库唯一键是最终幂等裁判"）；
    ///   并发同设备双登 → 一方插入、另一方撞唯一键重读，收敛到同一账号。
    /// - **字段边界 fail-closed**：requestId/deviceId 缺失或超 128 UTF-8 字节 → 拒绝
    ///   （§5 P0-3"长度在解析前限制"的用例侧半边——HTTP 侧还有请求体上限）。
    /// - **结果型返回**（不抛业务异常——异常面只承载基础设施故障，端点按结果映射 HTTP）。
    /// - accountId：签发端 CSPRNG（"a"+20 hex）——全局唯一性由随机空间保证，无自增表依赖。
    /// - **令牌/账号 Id 不写日志**（§11.1；本类不持日志面——requestId 的落日志归端点/中间件）。
    /// </summary>
    public sealed class GuestLoginUseCase
    {
        /// <summary>requestId/deviceId 的 UTF-8 字节上限（与端点侧字段限一致；§P0-3）。</summary>
        public const int MaxFieldBytes = 128;

        private readonly IAccountStore _accounts;
        private readonly AccessTokenService _tokens;
        private readonly Func<DateTime> _utcNow;

        public GuestLoginUseCase(IAccountStore accounts, AccessTokenService tokens, Func<DateTime> utcNow)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        /// <summary>执行结果（端点映射 HTTP：Accepted→200；Rejected→400；StoreUnavailable→503）。</summary>
        public abstract class Result
        {
            private Result() { }

            public sealed class Accepted : Result
            {
                public string AccountId;
                public string AccessToken;
                public long ExpiresAtMs;
            }

            public sealed class Rejected : Result
            {
                public string ErrorCode;
                public string MessageKey;
            }

            public sealed class StoreUnavailable : Result
            {
                public string Reason;
            }
        }

        public async Task<Result> ExecuteAsync(GuestLoginCommand command, CancellationToken ct)
        {
            if (!ValidField(command?.RequestId) || !ValidField(command.DeviceId))
                return new Result.Rejected
                {
                    ErrorCode = AuthErrorCodes.InvalidRequest,
                    MessageKey = AuthErrorCodes.MessageKeyInvalidRequest,
                };

            try
            {
                AccountRecord account = await _accounts.FindByDeviceIdAsync(command.DeviceId, ct);
                if (account == null)
                {
                    // get-or-create：先建（撞唯一键 = 并发同设备已建 → 重读收敛，§5.3）
                    var created = new AccountRecord
                    {
                        AccountId = NewAccountId(),
                        DeviceId = command.DeviceId,
                        CreatedUtc = _utcNow().ToUniversalTime(),
                    };
                    account = await _accounts.CreateAsync(created, ct);
                    if (account == null)
                        account = await _accounts.FindByDeviceIdAsync(command.DeviceId, ct);
                }

                if (account == null)
                    return new Result.StoreUnavailable { Reason = "account-create-unconfirmed" };

                string token = _tokens.Issue(account.AccountId, out long expiresAtMs);
                return new Result.Accepted
                {
                    AccountId = account.AccountId,
                    AccessToken = token,
                    ExpiresAtMs = expiresAtMs,
                };
            }
            catch (OperationCanceledException)
            {
                throw;                                    // 取消不吞——端点侧随请求管道传播
            }
            catch (Exception)
            {
                // 存储故障归基础设施异常面（端点映射 503；不打日志面——宿主/端点统一记录）
                return new Result.StoreUnavailable { Reason = "account-store-failure" };
            }
        }

        /// <summary>字段边界（非空且 UTF-8 字节数 ≤128；null/空串/超界都不进存储）。</summary>
        private static bool ValidField(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return Encoding.UTF8.GetByteCount(value) <= MaxFieldBytes;
        }

        private static string NewAccountId()
        {
            byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(10);
            return "a" + Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
