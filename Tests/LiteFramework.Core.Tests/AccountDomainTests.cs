using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using MetaWire;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 账号重入单点的顺序与去重（方案 A5 / N2）：Logout（软）→ await LeaveMatchAsync → Close（硬）→ ShowLogin。
    /// 域开合的真生命周期在 EditMode 真实 MetaSegment/RootScope 装配验收，本文件只测顺序/去重/失败仍导航。
    /// </summary>
    public sealed class AccountDomainTests
    {
        private sealed class Recorder
        {
            public readonly List<string> Calls = new List<string>();
            public int Count(string call) => Calls.FindAll(c => c == call).Count;
        }

        private sealed class FakeLoginControl : IAccountLoginControl
        {
            private readonly Recorder _log;

            public FakeLoginControl(Recorder log) => _log = log;

            public string AccountId => IsLoggedIn ? "acc-1" : null;
            public bool IsLoggedIn { get; private set; } = true;

            public void Logout(string reason)
            {
                _log.Calls.Add("logout:" + reason);
                IsLoggedIn = false;
            }

            public bool LeaveMatch()
            {
                _log.Calls.Add("leave");
                return false;
            }
        }

        private sealed class FakeRooms : IRoomFlow
        {
            private readonly Recorder _log;

            public FakeRooms(Recorder log) => _log = log;

            /// <summary>非 null 时 LeaveMatchAsync 挂起，由用例放行（观测 _returning 跨 await）。</summary>
            public UniTaskCompletionSource<bool> Gate;
            public bool Result = true;
            public int LeaveAsyncCalls;

            public UniTask<MetaCall<JoinTicketInfo>> EnterRoomAsync(string roomId, CancellationToken ct = default)
                => UniTask.FromResult(MetaCall<JoinTicketInfo>.Failed(MetaFailure.Network("unused")));

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId = 0, int expectedPlayers = 0,
                CancellationToken ct = default)
                => UniTask.FromResult(MetaCall<JoinTicketInfo>.Failed(MetaFailure.Network("unused")));

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId, int expectedPlayers,
                string template, string gmKey, CancellationToken ct = default)
                => UniTask.FromResult(MetaCall<JoinTicketInfo>.Failed(MetaFailure.Network("unused")));

            public bool LeaveMatch()
            {
                _log.Calls.Add("leave");
                return false;
            }

            public UniTask<bool> LeaveMatchAsync(CancellationToken ct = default)
            {
                LeaveAsyncCalls++;
                _log.Calls.Add("leaveAsync");
                return Gate != null ? Gate.Task : UniTask.FromResult(Result);
            }
        }

        private sealed class FakeAccountSession : IMetaAccountSession
        {
            public string AccountId => "acc-1";
            public bool IsLoggedIn => true;

            public UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync()
                => UniTask.FromResult(MetaCall<IReadOnlyList<LobbyInstanceView>>.OkResult(new LobbyInstanceView[0]));

            public UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string roomId, CancellationToken ct = default)
                => UniTask.FromResult(MetaCall<JoinTicketInfo>.Failed(MetaFailure.Network("unused")));

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId = 0, int expectedPlayers = 0,
                CancellationToken ct = default)
                => UniTask.FromResult(MetaCall<JoinTicketInfo>.Failed(MetaFailure.Network("unused")));

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId, int expectedPlayers,
                string template, string gmKey, CancellationToken ct = default)
                => UniTask.FromResult(MetaCall<JoinTicketInfo>.Failed(MetaFailure.Network("unused")));

            public UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecordsAsync(int limit)
                => UniTask.FromResult(MetaCall<IReadOnlyList<MatchRecordInfo>>.OkResult(new MatchRecordInfo[0]));

            public void Logout(string reason) { }
        }

        private sealed class FakeDomain : IAccountDomain
        {
            private readonly Recorder _log;
            private readonly ClientScope _scope;
            private readonly ServiceContainer _services;

            public FakeDomain(Recorder log)
            {
                _log = log;
                _scope = new ClientScope("Account");
                _services = new ServiceContainer();
                _services.Seal();
            }

            public bool IsOpen { get; private set; } = true;
            public string AccountId => "acc-1";
            public ClientScope Scope => _scope;
            public ServiceContainer Services => _services;
            public IMetaAccountSession Account { get; } = new FakeAccountSession();

            /// <summary>Close 时账户域是否仍在开（调度期不得自拆）。</summary>
            public bool OpenWhenClosed { get; private set; }

            public void Open(string accountId, string accessToken) => IsOpen = true;

            public void Revoke(AccountCloseReason reason)
            {
                _log.Calls.Add("revoke:" + reason);
                IsOpen = false;
            }

            public void Close(AccountCloseReason reason)
            {
                OpenWhenClosed = IsOpen;
                _log.Calls.Add("close:" + reason);
                IsOpen = false;
                _scope.Dispose();
                _services.Dispose();
            }

            public void Dispose()
            {
                _scope.Dispose();
                _services.Dispose();
            }
        }

        private sealed class FakeSurface : ILoginSurface
        {
            private readonly Recorder _log;

            public FakeSurface(Recorder log) => _log = log;

            /// <summary>false = 开面异步入队（ShowLogin 返回时 IsLoginOpen 仍为 false）。</summary>
            public bool SyncOpen = true;

            public bool IsLoginOpen { get; private set; }

            public void ShowLogin(string reason)
            {
                _log.Calls.Add("showLogin:" + reason);
                if (SyncOpen) IsLoginOpen = true;
            }

            /// <summary>模拟页面真正落地（异步开面完成）。</summary>
            public void Land() => IsLoginOpen = true;
        }

        /// <summary>取最完整的可用构造（T5 起应含 <see cref="IAccountDomain"/>；缺则顺序断言自然失败）。</summary>
        private static AccountReentry NewReentry(IAccountLoginControl account, IRoomFlow rooms,
            IAccountDomain domain, ILoginSurface surface)
        {
            var ports = new Dictionary<Type, object>
            {
                [typeof(IAccountLoginControl)] = account,
                [typeof(IRoomFlow)] = rooms,
                [typeof(IAccountDomain)] = domain,
                [typeof(ILoginSurface)] = surface,
            };

            ConstructorInfo best = null;
            object[] bestArgs = null;
            foreach (ConstructorInfo ctor in typeof(AccountReentry).GetConstructors())
            {
                ParameterInfo[] ps = ctor.GetParameters();
                var args = new object[ps.Length];
                bool usable = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!ports.TryGetValue(ps[i].ParameterType, out object arg))
                    {
                        usable = false;
                        break;
                    }
                    args[i] = arg;
                }
                if (!usable) continue;
                if (best == null || ps.Length > best.GetParameters().Length)
                {
                    best = ctor;
                    bestArgs = args;
                }
            }

            Assert.True(best != null, "AccountReentry 缺少可用构造（需要 IAccountLoginControl/IRoomFlow/IAccountDomain/ILoginSurface 端口）");
            return (AccountReentry)best.Invoke(bestArgs);
        }

        [Fact]
        public async System.Threading.Tasks.Task 重入_顺序固定_登出_等离场_关域_回登录页()
        {
            var log = new Recorder();
            var account = new FakeLoginControl(log);
            var rooms = new FakeRooms(log) { Result = true };
            var domain = new FakeDomain(log);
            var surface = new FakeSurface(log);
            AccountReentry reentry = NewReentry(account, rooms, domain, surface);

            bool navigated = await reentry.ReturnToLoginAsync("token-expired");

            Assert.True(navigated);
            Assert.Equal(
                new[] { "logout:token-expired", "leaveAsync", "close:Logout", "showLogin:token-expired" },
                log.Calls);
            Assert.False(account.IsLoggedIn);
            Assert.True(domain.OpenWhenClosed, "硬关域必须发生在登出之后（软关闭先行使在途操作失效）");
        }

        [Fact]
        public async System.Threading.Tasks.Task 重入_离场返回false_仍回登录页_不硬关域()
        {
            var log = new Recorder();
            var account = new FakeLoginControl(log);
            var rooms = new FakeRooms(log) { Result = false };
            var domain = new FakeDomain(log);
            var surface = new FakeSurface(log);
            AccountReentry reentry = NewReentry(account, rooms, domain, surface);

            Assert.True(await reentry.ReturnToLoginAsync("token-expired"));

            Assert.Equal(new[] { "logout:token-expired", "leaveAsync", "showLogin:token-expired" }, log.Calls);
            Assert.Equal(0, log.Count("close:Logout"));
            Assert.Equal(0, log.Count("close:AuthLost"));
            Assert.True(surface.IsLoginOpen, "清理未完成（false）也必须回登录页");
            Assert.True(domain.IsOpen, "false 分支不得硬关账户域（保留 busy，不覆盖既有 fatal 语义）");
        }

        [Fact]
        public async System.Threading.Tasks.Task 重入_四次并发401_只登出与离场一次()
        {
            var log = new Recorder();
            var account = new FakeLoginControl(log);
            var rooms = new FakeRooms(log) { Gate = new UniTaskCompletionSource<bool>() };
            var domain = new FakeDomain(log);
            var surface = new FakeSurface(log);
            AccountReentry reentry = NewReentry(account, rooms, domain, surface);

            UniTask<bool> first = reentry.ReturnToLoginAsync("meta.401");
            UniTask<bool>[] others =
            {
                reentry.ReturnToLoginAsync("meta.401"),
                reentry.ReturnToLoginAsync("meta.401"),
                reentry.ReturnToLoginAsync("meta.401"),
            };

            Assert.Equal(new[] { "logout:meta.401", "leaveAsync" }, log.Calls);
            Assert.Equal(0, log.Count("showLogin:meta.401"));
            for (int i = 0; i < others.Length; i++)
            {
                Assert.False(await others[i], "重入窗口内的第二次 401 不得重复导航");
            }

            rooms.Gate.TrySetResult(true);
            Assert.True(await first);
            Assert.Equal(1, log.Count("logout:meta.401"));
            Assert.Equal(1, rooms.LeaveAsyncCalls);
            Assert.Equal(1, log.Count("showLogin:meta.401"));
        }

        [Fact]
        public async System.Threading.Tasks.Task 重入_完成后再次调用_登录页已在场_不二次导航()
        {
            var log = new Recorder();
            var account = new FakeLoginControl(log);
            var rooms = new FakeRooms(log) { Result = true };
            var domain = new FakeDomain(log);
            var surface = new FakeSurface(log);
            AccountReentry reentry = NewReentry(account, rooms, domain, surface);

            Assert.True(await reentry.ReturnToLoginAsync("meta.401"));
            int calls = log.Calls.Count;

            Assert.False(await reentry.ReturnToLoginAsync("meta.401"));
            Assert.Equal(calls, log.Calls.Count);
        }

        [Fact]
        public async System.Threading.Tasks.Task 重入_开面异步入队_落地前再次401不重复导航()
        {
            var log = new Recorder();
            var account = new FakeLoginControl(log);
            var rooms = new FakeRooms(log) { Result = true };
            var domain = new FakeDomain(log);
            var surface = new FakeSurface(log) { SyncOpen = false };
            AccountReentry reentry = NewReentry(account, rooms, domain, surface);

            Assert.True(await reentry.ReturnToLoginAsync("meta.401"));
            Assert.False(surface.IsLoginOpen, "开面异步入队：ShowLogin 返回时页面尚未落地（前置）");

            Assert.False(await reentry.ReturnToLoginAsync("meta.401"),
                "开面在途（记忆位）期间的重复重入不得再次导航");
            Assert.Equal(1, log.Count("showLogin:meta.401"));

            surface.Land();
            Assert.False(await reentry.ReturnToLoginAsync("meta.401"), "落地后同样不重复导航");
            Assert.Equal(1, log.Count("showLogin:meta.401"));
        }

        [Fact]
        public void 重入_端口为空_拒绝构造()
        {
            Assert.Throws<ArgumentNullException>(() => new AccountReentry(null, new FakeSurface(new Recorder())));
            Assert.Throws<ArgumentNullException>(() => new AccountReentry(new FakeLoginControl(new Recorder()), null));
        }
    }
}
