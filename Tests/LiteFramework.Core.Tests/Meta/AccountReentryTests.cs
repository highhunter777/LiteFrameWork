using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteGame;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 账号重入单点（M3-5c 恢复动作「重新登录」；《客户端依赖注入专项设计》§6.2）：
    /// 登录失效后唯一路径 = ① 登出（令牌即弃）→ ② 离场（在途对局回 Main）→ ③ 回登录页；
    /// 登录页已在场 = 幂等 no-op；取消 = 不动作。零引擎端口替身直测。
    /// </summary>
    public sealed class AccountReentryTests
    {
        private sealed class FakeAccount : IAccountSessionControl
        {
            public readonly List<string> Calls = new List<string>();
            public bool LeaveResult;

            public string AccountId => IsLoggedIn ? "acc-1" : null;
            public bool IsLoggedIn { get; private set; } = true;

            public void Logout(string reason)
            {
                Calls.Add("logout:" + reason);
                IsLoggedIn = false;
            }

            public bool LeaveMatch()
            {
                Calls.Add("leave");
                return LeaveResult;
            }
        }

        private sealed class FakeSurface : ILoginSurface
        {
            public readonly List<string> Calls = new List<string>();

            public bool IsLoginOpen { get; set; }

            public void ShowLogin(string reason) => Calls.Add("login:" + reason);
        }

        [Fact]
        public async Task 重入_顺序固定_登出_离场_回登录页()
        {
            var account = new FakeAccount { LeaveResult = true };
            var surface = new FakeSurface();
            var reentry = new AccountReentry(account, surface);

            bool navigated = await reentry.ReturnToLoginAsync("token-expired");

            Assert.True(navigated);
            Assert.Equal(new[] { "logout:token-expired", "leave" }, account.Calls);
            Assert.Equal(new[] { "login:token-expired" }, surface.Calls);
            Assert.False(account.IsLoggedIn);                     // 令牌即弃（不重放旧令牌）
        }

        [Fact]
        public async Task 重入_已在Main_离场为noop_仍回登录页()
        {
            var account = new FakeAccount { LeaveResult = false };
            var surface = new FakeSurface();
            var reentry = new AccountReentry(account, surface);

            Assert.True(await reentry.ReturnToLoginAsync("token-expired"));
            Assert.Equal(new[] { "logout:token-expired", "leave" }, account.Calls);
            Assert.Single(surface.Calls);                          // 离场 no-op 不影响回登录页
            Assert.False(account.IsLoggedIn);
        }

        [Fact]
        public async Task 重入_登录页已在场_幂等不重复导航()
        {
            var account = new FakeAccount();
            var surface = new FakeSurface { IsLoginOpen = true };
            var reentry = new AccountReentry(account, surface);

            Assert.False(await reentry.ReturnToLoginAsync("token-expired"));
            Assert.Empty(account.Calls);                           // 不清登录态（未失效路径不误伤）
            Assert.Empty(surface.Calls);
        }

        [Fact]
        public async Task 重入_取消_不动作()
        {
            var account = new FakeAccount();
            var surface = new FakeSurface();
            var reentry = new AccountReentry(account, surface);

            Assert.False(await reentry.ReturnToLoginAsync("token-expired",
                new CancellationToken(canceled: true)));
            Assert.Empty(account.Calls);
            Assert.Empty(surface.Calls);
        }

        [Fact]
        public void 重入_端口为空_拒绝构造()
        {
            Assert.Throws<ArgumentNullException>(() => new AccountReentry(null, new FakeSurface()));
            Assert.Throws<ArgumentNullException>(() => new AccountReentry(new FakeAccount(), null));
        }
    }
}
