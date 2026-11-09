using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>登录域（Meta 侧）：登录开、登出/401/宿主关闭关；持每账户会话与 Account 服务域。</summary>
    public interface IAccountDomain : IDisposable
    {
        bool IsOpen { get; }
        string AccountId { get; }

        /// <summary>部署是否配置了联机入口（默认 true；生产实现读部署事实）。</summary>
        bool IsConfigured => true;
        ClientScope Scope { get; }
        ServiceContainer Services { get; }
        IMetaAccountSession Account { get; }

        void Open(string accountId, string accessToken);
        void Revoke(AccountCloseReason reason);
        void Close(AccountCloseReason reason);
    }
}
