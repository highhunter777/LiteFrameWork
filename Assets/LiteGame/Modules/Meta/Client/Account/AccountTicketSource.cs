using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MetaWire;
using LiteGame.Modules.Meta.Abstractions;
using LiteGame.Modules.Meta.Abstractions.Account;
using LiteGame.Modules.Meta.Abstractions.Room;

namespace LiteGame.Modules.Meta.Client.Account
{
    /// <summary>root 票据源门面：转当前账户会话（无账户 = NotLoggedIn；未配置优先）。</summary>
    public sealed class AccountTicketSource : IRoomTicketSource
    {
        private readonly IAccountDomain _domain;
        private readonly IMetaConfigFacts _facts;

        public AccountTicketSource(IAccountDomain domain, IMetaConfigFacts facts = null)
        {
            _domain = domain ?? throw new ArgumentNullException(nameof(domain));
            _facts = facts;
        }

        public bool IsConfigured => _facts != null ? _facts.IsConfigured : _domain.IsConfigured;
        public string AccountId => _domain.AccountId;
        public bool IsLoggedIn => _domain.IsOpen;

        public void Logout(string reason) => _domain.Revoke(AccountCloseReason.Logout);

        public UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string roomId,
            CancellationToken ct = default)
            => WithAccount<JoinTicketInfo>(session => session.RequestJoinTicketAsync(roomId, ct));

        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId = 0, int expectedPlayers = 0,
            CancellationToken ct = default)
            => WithAccount<JoinTicketInfo>(session => session.CreateRoomTicketAsync(mapId, expectedPlayers, ct));

        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId, int expectedPlayers,
            string template, string gmKey, CancellationToken ct = default)
            => WithAccount<JoinTicketInfo>(session => session.CreateRoomTicketAsync(mapId, expectedPlayers,
                template, gmKey, ct));

        private UniTask<MetaCall<T>> WithAccount<T>(Func<IMetaAccountSession, UniTask<MetaCall<T>>> call)
        {
            if (!IsConfigured) return UniTask.FromResult(NotConfigured<T>());
            IMetaAccountSession session = _domain.Account;
            if (session == null || !_domain.IsOpen) return UniTask.FromResult(NotLoggedIn<T>());
            return call(session);
        }

        private static MetaCall<T> NotConfigured<T>()
            => MetaCall<T>.Failed(MetaFailure.Rejected(0, "entry.not-configured", "未配置服务器地址（-meta.url）"));

        private static MetaCall<T> NotLoggedIn<T>()
            => MetaCall<T>.Failed(MetaFailure.Rejected(0, "entry.not-logged-in", "未登录"));
    }
}
