using System.Threading;
using Cysharp.Threading.Tasks;

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
}
