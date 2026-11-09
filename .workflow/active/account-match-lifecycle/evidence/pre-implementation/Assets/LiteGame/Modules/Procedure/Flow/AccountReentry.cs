using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 账号重入单点（《客户端依赖注入专项设计》§6.2「重新登录」）。
    /// </summary>
    public sealed class AccountReentry : IAccountReentry
    {
        private readonly IAccountSessionControl _account;
        private readonly ILoginSurface _surface;
        private bool _returning;

        public AccountReentry(IAccountSessionControl account, ILoginSurface surface)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        public UniTask<bool> ReturnToLoginAsync(string reason, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested || _returning) return UniTask.FromResult(false);
            if (_surface.IsLoginOpen) return UniTask.FromResult(false);   // 已在登录页：不重复导航

            _returning = true;
            try
            {
                _account.Logout(reason);                                   // ① 令牌即弃
                bool leftMatch = _account.LeaveMatch();                    // ② 在途对局/阶段回 Main
                _surface.ShowLogin(reason);                                // ③ 单写者替换为登录页
                Log.Info($"账号重入：已回登录页（reason={reason}, leftMatch={leftMatch}）", LogTag.Meta);
                return UniTask.FromResult(true);
            }
            finally
            {
                _returning = false;
            }
        }
    }
}
