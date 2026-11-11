using System;

namespace LiteGame.Modules.Meta.Client.Account
{
    /// <summary>账户事实（AccountId + 令牌）；令牌不出公开面，Dispose 幂等清空。</summary>
    internal sealed class AccountContext : IDisposable
    {
        private string _token;

        public AccountContext(string accountId, string accessToken)
        {
            AccountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
            _token = accessToken;
        }

        public string AccountId { get; }
        public bool IsLoggedIn => _token != null;
        public string AccessToken => _token;

        public void Dispose() => _token = null;
    }
}
