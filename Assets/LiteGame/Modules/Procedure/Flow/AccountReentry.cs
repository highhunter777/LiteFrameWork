using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Meta.Abstractions;
using LiteGame.Modules.Meta.Abstractions.Account;

namespace LiteGame
{
    /// <summary>
    /// 账号重入单点（《客户端依赖注入专项设计》§6.2「重新登录」）。
    /// </summary>
    public sealed class AccountReentry : IAccountReentry
    {
        private readonly IAccountLoginControl _account;
        private readonly IRoomFlow _rooms;
        private readonly IAccountDomain _domain;
        private readonly ILoginSurface _surface;
        private bool _returning;
        private bool _navigating;

        public AccountReentry(IAccountLoginControl account, ILoginSurface surface)
            : this(account, null, null, surface)
        {
        }

        public AccountReentry(IAccountLoginControl account, IRoomFlow rooms, IAccountDomain domain,
            ILoginSurface surface)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _rooms = rooms;
            _domain = domain;
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        public async UniTask<bool> ReturnToLoginAsync(string reason, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested || _returning) return false;
            if (_navigating)
            {
                if (_surface.IsLoginOpen) _navigating = false;   // 已落地：清记忆位
                return false;                                     // 开面在途：不重复导航
            }
            if (_surface.IsLoginOpen) return false;   // 已在登录页：不重复导航

            _returning = true;
            try
            {
                _account.Logout(reason);                                   // ① 令牌即弃
                bool leftMatch = _rooms != null ? await _rooms.LeaveMatchAsync(ct) : true;
                if (leftMatch) _domain?.Close(AccountCloseReason.Logout);   // ③ 仅在可安全关闭时硬释放
                _navigating = true;
                _surface.ShowLogin(reason);                                // ④ 单写者替换为登录页
                if (_surface.IsLoginOpen) _navigating = false;             // 同步落地：无需记忆位
                Log.Info($"账号重入：已回登录页（reason={reason}, leftMatch={leftMatch}）", LogTag.Meta);
                return true;
            }
            finally
            {
                _returning = false;
            }
        }
    }
}
