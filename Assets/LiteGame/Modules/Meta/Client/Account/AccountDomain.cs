using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>登录域：登录开、登出/401/宿主关闭关；持每账户会话与 Account 服务域。</summary>
    public sealed class AccountDomain : IAccountDomain
    {
        private readonly ClientScope _rootScope;
        private readonly ServiceContainer _rootServices;
        private readonly IAccountReentry _reentry;
        private readonly Func<IAccountReentry> _reentryProvider;
        private readonly string _template;
        private readonly string _gmKey;
        private readonly IMetaConfigFacts _facts;

        private ClientScope _scope;
        private ServiceContainer _services;
        private AccountContext _context;
        private IMetaAccountSession _account;
        private bool _revoked;
        private bool _disposed;

        public AccountDomain(ClientScope rootScope, ServiceContainer rootServices,
            IAccountReentry reentry = null, string template = null, string gmKey = null,
            Func<IAccountReentry> reentryProvider = null)
        {
            _rootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
            _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
            _reentry = reentry;
            _reentryProvider = reentryProvider;
            _template = template ?? string.Empty;
            _gmKey = gmKey ?? string.Empty;
            _rootServices.TryResolve(out _facts);
        }

        public bool IsConfigured => _facts == null || _facts.IsConfigured;
        public bool IsOpen => _scope != null && !_revoked;
        public string AccountId => _account?.AccountId;
        public ClientScope Scope => _scope;
        public ServiceContainer Services => _services;
        public IMetaAccountSession Account => _account;

        public void Open(string accountId, string accessToken)
        {
            if (accountId == null) throw new ArgumentNullException(nameof(accountId));
            if (_disposed) throw new ObjectDisposedException(nameof(AccountDomain));
            Close(AccountCloseReason.Relogin);

            IMetaClient client = null;
            IdentityContext identity = null;
            _rootServices.TryResolve(out client);
            _rootServices.TryResolve(out identity);
            if (identity == null)
                throw new InvalidOperationException("IdentityContext 未登记，无法开启账户域");

            _scope = _rootScope.CreateChild("Account");
            _services = (ServiceContainer)_rootServices.CreateScope("Account");
            _context = new AccountContext(accountId, accessToken);
            _account = new MetaAccountSession(client, identity, accountId, accessToken,
                _template, _gmKey, ReentryProvider());
            _services.RegisterInstance<IMetaAccountSession>(_account);
            _revoked = false;
        }

        public void Revoke(AccountCloseReason reason)
        {
            if (_revoked) return;
            _revoked = true;
            _account?.Logout(reason.ToString());
        }

        public void Close(AccountCloseReason reason)
        {
            ClientScope scope = _scope;
            ServiceContainer services = _services;
            AccountContext context = _context;
            IMetaAccountSession account = _account;
            _scope = null;
            _services = null;
            _context = null;
            _account = null;
            _revoked = false;

            account?.Logout(reason.ToString());
            context?.Dispose();
            services?.Dispose();
            scope?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close(AccountCloseReason.Shutdown);
        }

        private IAccountReentry ReentryProvider()
        {
            if (_reentry != null) return _reentry;
            if (_reentryProvider != null) return _reentryProvider();
            IAccountReentry resolved = null;
            _rootServices.TryResolve(out resolved);
            return resolved;
        }
    }
}
