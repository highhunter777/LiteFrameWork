using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 登录态控制端口（账号重入单点依赖它；生产实现 = <see cref="MetaEntryService"/>）：
    /// 登出（令牌/账号即弃）与离场（在途对局/流程回 Main）。零引擎——测试替身直替。
    /// </summary>
    public interface IAccountSessionControl
    {
        /// <summary>当前登录账号（null = 未登录）。</summary>
        string AccountId { get; }

        /// <summary>是否已登录（令牌在手）。</summary>
        bool IsLoggedIn { get; }

        /// <summary>清登录态（不落日志原文/不重放旧令牌）。</summary>
        void Logout(string reason);

        /// <summary>离场请求：在途对局/阶段取消并回 Main；已在 Main = no-op（false）。</summary>
        bool LeaveMatch();
    }

    /// <summary>
    /// 登录页展示端口（账号重入的 UI 侧；生产实现 = <see cref="PageFlow"/>）。
    /// 只表达"登录页在不在 / 把它换成登录页"，导航仍由导航单写者执行。
    /// </summary>
    public interface ILoginSurface
    {
        /// <summary>登录页是否已在场（幂等判据——重复重入不再导航）。</summary>
        bool IsLoginOpen { get; }

        /// <summary>把页面栈替换为登录页（收掉在开页面；忽略启动落点的一次性守卫）。</summary>
        void ShowLogin(string reason);
    }

    /// <summary>账号重入端口（恢复链/会话面调用的**单点入口**）。</summary>
    public interface IAccountReentry
    {
        /// <summary>清登录态 → 取消在途对局 → 回登录页；返回 true = 本次发生了重入。</summary>
        UniTask<bool> ReturnToLoginAsync(string reason, CancellationToken ct = default);
    }

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
