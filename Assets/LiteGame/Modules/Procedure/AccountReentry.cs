using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 账号重入单点（《客户端依赖注入专项设计》§6.2「重新登录」；《商业级通用客户端框架总设计》§7.2
    /// Recoverable 的"重新登录"动作）：
    ///
    /// 登录失效（Meta 401 / 令牌过期）后，**唯一**的重入路径——顺序固定：
    /// ① 登出（令牌即弃，后续 Meta 调用一律 NotLoggedIn，不重放旧令牌）；
    /// ② 离场（在途 Match/Battle 阶段取消并回 Main，资源由各自 finally 收尾）；
    /// ③ 回登录页（导航单写者替换页面栈）。
    ///
    /// 幂等：登录页已在场 = no-op；重入进行中并发调用只走一次（并发 401 不重复导航）。
    /// 零引擎依赖（端口注入）——L1 直测顺序与幂等，Unity 面只提供实现。
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
