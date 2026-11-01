using System;

namespace LiteGame
{
    /// <summary>
    /// 账号会话端口（会话域产物）：流程段持有/移交的**唯一会话句柄**——把三个已端口化的面拼成一份契约：
    /// 对局材料与 Account 资源域（<see cref="IAccountRuntime"/>，Battle 侧声明）、进房与传输泵
    /// （<see cref="IMatchJoin"/>）、生命周期（<see cref="IDisposable"/>：释放 Account Scope，LIFO）。
    ///
    /// 为什么需要：流程在 Match→Battle 之间**移交所有权**（<c>ProcedureArgs</c> 携带）。参数若是具体类
    /// <c>AccountSession</c>，流程域就被钉在会话实现件上；收成端口后，移交只经契约，实现件仍住会话域。
    /// </summary>
    public interface IAccountSession : IAccountRuntime, IMatchJoin, IDisposable
    {
    }
}
