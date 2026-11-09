using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using MetaWire;

namespace LiteGame
{
    /// <summary>每账户 Meta 会话：令牌面调用；401 调度一次重入，自身不关域。</summary>
    public sealed class MetaAccountSession : IMetaAccountSession
    {
        private readonly IMetaClient _client;
        private readonly IdentityContext _identity;
        private readonly IAccountReentry _reentry;
        private readonly string _template;
        private readonly string _gmKey;
        private string _token;

        public MetaAccountSession(IMetaClient client, IdentityContext identity, string accountId,
            string accessToken, string template, string gmKey, IAccountReentry reentry = null)
        {
            _client = client;
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            AccountId = accountId;
            _token = accessToken;
            _template = template ?? string.Empty;
            _gmKey = gmKey ?? string.Empty;
            _reentry = reentry;
        }

        public string AccountId { get; }
        public bool IsLoggedIn => _token != null;

        public async UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync()
        {
            if (!IsConfigured) return NotConfigured<IReadOnlyList<LobbyInstanceView>>();
            if (_token == null) return NotLoggedIn<IReadOnlyList<LobbyInstanceView>>();
            MetaCall<IReadOnlyList<LobbyInstanceView>> call = await _client.ListRoomsAsync(_token);
            NotifyIfAuthLost(call.Failure);
            return call;
        }

        public async UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string roomId,
            CancellationToken ct = default)
        {
            if (!IsConfigured) return NotConfigured<JoinTicketInfo>();
            if (_token == null) return NotLoggedIn<JoinTicketInfo>();
            ct.ThrowIfCancellationRequested();
            MetaCall<JoinTicketInfo> call = await _client.RequestJoinTicketAsync(
                _token, roomId ?? "", _identity.Handshake.BuildHash, ct);
            ct.ThrowIfCancellationRequested();
            NotifyIfAuthLost(call.Failure);
            return call;
        }

        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId = 0, int expectedPlayers = 0,
            CancellationToken ct = default)
            => CreateRoomTicketAsync(mapId, expectedPlayers, NullIfEmpty(_template), NullIfEmpty(_gmKey), ct);

        public async UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId, int expectedPlayers,
            string template, string gmKey, CancellationToken ct = default)
        {
            if (!IsConfigured) return NotConfigured<JoinTicketInfo>();
            if (_token == null) return NotLoggedIn<JoinTicketInfo>();
            ct.ThrowIfCancellationRequested();
            MetaCall<JoinTicketInfo> call = await _client.CreateRoomAsync(_token, _identity.Handshake.BuildHash,
                mapId, expectedPlayers, template, gmKey, ct);
            ct.ThrowIfCancellationRequested();
            NotifyIfAuthLost(call.Failure);
            return call;
        }

        public async UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecordsAsync(int limit)
        {
            if (!IsConfigured) return NotConfigured<IReadOnlyList<MatchRecordInfo>>();
            if (_token == null) return NotLoggedIn<IReadOnlyList<MatchRecordInfo>>();
            MetaCall<IReadOnlyList<MatchRecordInfo>> call = await _client.ListRecentMatchesAsync(_token, limit);
            NotifyIfAuthLost(call.Failure);
            return call;
        }

        public void Logout(string reason) => _token = null;

        private bool IsConfigured => _client != null;

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>401 → 构造失败后离栈调度一次重入；调用方返回后不得再触碰本实例。</summary>
        private void NotifyIfAuthLost(MetaFailure failure)
        {
            if (_reentry == null || failure == null || !failure.IsAuthLost) return;
            ReenterAsync("meta." + Reason(failure)).ForgetLogged("MetaAccountSession");
        }

        private async UniTask ReenterAsync(string reason)
        {
            await UniTask.Yield();
            if (_reentry == null) return;
            try { await _reentry.ReturnToLoginAsync(reason); }
            catch (Exception ex) { Log.Error("账号重入失败:" + ex.Message, LogTag.Meta); }
        }

        private static MetaCall<T> NotConfigured<T>()
            => MetaCall<T>.Failed(MetaFailure.Rejected(0, "entry.not-configured", "未配置服务器地址（-meta.url）"));

        private static MetaCall<T> NotLoggedIn<T>()
            => MetaCall<T>.Failed(MetaFailure.Rejected(0, "entry.not-logged-in", "未登录"));

        private static string Reason(MetaFailure failure)
        {
            if (failure == null) return "Internal";
            switch (failure.Kind)
            {
                case MetaFailureKind.Network: return "Network";
                case MetaFailureKind.InvalidResponse: return "InvalidResponse";
                default:
                    return string.IsNullOrEmpty(failure.Code) ? "HTTP" + failure.HttpStatus : failure.Code;
            }
        }
    }
}
