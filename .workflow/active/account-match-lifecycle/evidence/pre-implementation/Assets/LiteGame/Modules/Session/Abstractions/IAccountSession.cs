using System;

namespace LiteGame
{
    /// <summary>
    /// 账号会话端口（会话域产物）：流程段持有/移交的**唯一会话句柄**——把三个已端口化的面拼成一份契约： 对局材料与 Account 资源域（IAccountRuntime，Battle 侧声明）。
    /// </summary>
    public interface IAccountSession : IAccountRuntime, IMatchJoin, IDisposable
    {
    }
}
