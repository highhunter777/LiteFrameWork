using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Auth;
using MetaServer.Contracts.Persistence;
using MetaServer.Modules.Auth;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 游客登录用例（<see cref="GuestLoginUseCase"/>）L1：《Meta 服务专项设计》§6.1/§5.3——
    /// deviceId 唯一键 get-or-create 幂等、并发撞键重读收敛、字段边界 fail-closed、
    /// 存储故障归 StoreUnavailable（端点映射 503）。存储用本工程内建双打（M0-c 同款纪律）。
    /// </summary>
    public sealed class GuestLoginUseCaseTests
    {
        /// <summary>
        /// 内存账号双打：模拟唯一索引（deviceId 重复 Create → null）；可注入故障。
        /// 行为与 Mongo 唯一索引语义对齐（真适配器由 L3 MongoAccountStore 用例钉）。
        /// </summary>
        private sealed class InMemoryAccountStore : IAccountStore
        {
            private readonly Dictionary<string, AccountRecord> _byDevice =
                new Dictionary<string, AccountRecord>(StringComparer.Ordinal);
            public int CreateCalls;
            public Exception ThrowOnCreate;

            public ValueTask<AccountRecord> FindByDeviceIdAsync(string deviceId, CancellationToken ct)
            {
                _byDevice.TryGetValue(deviceId, out AccountRecord record);
                return new ValueTask<AccountRecord>(record);
            }

            public ValueTask<AccountRecord> CreateAsync(AccountRecord record, CancellationToken ct)
            {
                CreateCalls++;
                if (ThrowOnCreate != null) throw ThrowOnCreate;
                if (_byDevice.ContainsKey(record.DeviceId)) return new ValueTask<AccountRecord>((AccountRecord)null);
                // 竞态注入：双打在登记前让位（模拟并发同设备已建——重读路径由用例覆盖）
                _byDevice[record.DeviceId] = record;
                return new ValueTask<AccountRecord>(record);
            }
        }

        private static AccessTokenService NewTokenService()
            => new AccessTokenService(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
                "k1", "meta", 600, () => 1_000L);

        private static GuestLoginCommand Cmd(string requestId, string deviceId)
            => new GuestLoginCommand { RequestId = requestId, DeviceId = deviceId };

        private static Func<DateTime> Now()
            => () => new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc);

        [Fact]
        public async Task 首登建账号_同设备重复登录同账号_异设备新账号()
        {
            var store = new InMemoryAccountStore();
            var useCase = new GuestLoginUseCase(store, NewTokenService(), Now());

            GuestLoginUseCase.Result.Accepted first =
                await useCase.ExecuteAsync(Cmd("r1", "device-a"), CancellationToken.None) as GuestLoginUseCase.Result.Accepted;
            Assert.NotNull(first);
            Assert.StartsWith("a", first.AccountId);
            Assert.Equal(21, first.AccountId.Length);                 // "a" + 20 hex
            Assert.NotEmpty(first.AccessToken);
            Assert.Equal(1_000L + 600_000L, first.ExpiresAtMs);

            // 同设备重复登录：同一账号（deviceId = 最终幂等裁判）；新令牌
            GuestLoginUseCase.Result.Accepted again =
                await useCase.ExecuteAsync(Cmd("r2", "device-a"), CancellationToken.None) as GuestLoginUseCase.Result.Accepted;
            Assert.NotNull(again);
            Assert.Equal(first.AccountId, again.AccountId);
            Assert.NotEqual(first.AccessToken, again.AccessToken);    // 每次登录新令牌（jti 不同）
            Assert.Equal(1, store.CreateCalls);                       // 只建过一次

            GuestLoginUseCase.Result.Accepted other =
                await useCase.ExecuteAsync(Cmd("r3", "device-b"), CancellationToken.None) as GuestLoginUseCase.Result.Accepted;
            Assert.NotEqual(first.AccountId, other.AccountId);
        }

        [Fact]
        public async Task 并发撞唯一键_重读收敛同账号()
        {
            // 双打把"竞态窗口内他方已建"编码为：首查为空、Create 撞键（null）、二查返回他方账号
            var store = new RaceAccountStore();
            var useCase = new GuestLoginUseCase(store, NewTokenService(), Now());

            GuestLoginUseCase.Result.Accepted result =
                await useCase.ExecuteAsync(Cmd("r1", "device-a"), CancellationToken.None) as GuestLoginUseCase.Result.Accepted;
            Assert.NotNull(result);
            Assert.Equal(RaceAccountStore.Precreated.AccountId, result.AccountId);   // 重读收敛到已存在账号
        }

        [Fact]
        public async Task 字段边界fail_closed_缺失或超128字节拒绝()
        {
            var useCase = new GuestLoginUseCase(new InMemoryAccountStore(), NewTokenService(), Now());
            string long131 = new string('x', 131);

            GuestLoginUseCase.Result.Rejected nullCmd =
                await useCase.ExecuteAsync(null, CancellationToken.None) as GuestLoginUseCase.Result.Rejected;
            GuestLoginUseCase.Result.Rejected missingDevice =
                await useCase.ExecuteAsync(Cmd("r1", null), CancellationToken.None) as GuestLoginUseCase.Result.Rejected;
            GuestLoginUseCase.Result.Rejected emptyDevice =
                await useCase.ExecuteAsync(Cmd("r1", ""), CancellationToken.None) as GuestLoginUseCase.Result.Rejected;
            GuestLoginUseCase.Result.Rejected oversize =
                await useCase.ExecuteAsync(Cmd(long131, "d"), CancellationToken.None) as GuestLoginUseCase.Result.Rejected;

            foreach (GuestLoginUseCase.Result.Rejected r in new[] { nullCmd, missingDevice, emptyDevice, oversize })
            {
                Assert.NotNull(r);
                Assert.Equal(AuthErrorCodes.InvalidRequest, r.ErrorCode);
                Assert.Equal(AuthErrorCodes.MessageKeyInvalidRequest, r.MessageKey);
            }
        }

        [Fact]
        public async Task 存储故障归StoreUnavailable_不向调用方抛基础设施异常()
        {
            var store = new InMemoryAccountStore
            {
                ThrowOnCreate = new InvalidOperationException("mongo down"),
            };
            var useCase = new GuestLoginUseCase(store, NewTokenService(), Now());

            GuestLoginUseCase.Result.StoreUnavailable result =
                await useCase.ExecuteAsync(Cmd("r1", "device-a"), CancellationToken.None)
                as GuestLoginUseCase.Result.StoreUnavailable;
            Assert.NotNull(result);
            Assert.Equal("account-store-failure", result.Reason);
        }

        [Fact]
        public async Task 响应令牌可被同服务校验通过()
        {
            var store = new InMemoryAccountStore();
            var tokens = NewTokenService();
            var useCase = new GuestLoginUseCase(store, tokens, Now());

            GuestLoginUseCase.Result.Accepted result =
                await useCase.ExecuteAsync(Cmd("r1", "device-a"), CancellationToken.None) as GuestLoginUseCase.Result.Accepted;

            Assert.Equal(AccessTokenFormat.Rejection.None, tokens.Validate(result.AccessToken, out AccessTokenFormat.Claims claims));
            Assert.Equal(result.AccountId, claims.AccountId);
        }

        /// <summary>竞态双打：FindByDeviceId 首查为空、Create 撞键返回 null、二查返回"他方已建"的账号。</summary>
        private sealed class RaceAccountStore : IAccountStore
        {
            public static readonly AccountRecord Precreated = new AccountRecord
            {
                AccountId = "a00ff00ff00ff00ff00ff", DeviceId = "device-a", CreatedUtc = DateTime.UtcNow,
            };

            private int _findCalls;

            public ValueTask<AccountRecord> FindByDeviceIdAsync(string deviceId, CancellationToken ct)
            {
                _findCalls++;
                return new ValueTask<AccountRecord>(_findCalls >= 2 ? Precreated : null);
            }

            public ValueTask<AccountRecord> CreateAsync(AccountRecord record, CancellationToken ct)
                => new ValueTask<AccountRecord>((AccountRecord)null);      // 模拟撞唯一键
        }
    }
}
