using System;
using LiteFramework;
using LiteNet.Proto;
using LiteNet.Transport;
using LiteSync.Client;
using LiteGame.Modules.Battle.Abstractions.Run;

namespace LiteGame
{
    /// <summary>
    /// Match 域会话工厂（《客户端依赖注入专项设计》§6 服务作用域）：把动态进房参数变成可移交的 IMatchSession——**类型化工厂**（不用裸 new + 手工拼作用域散落进流程）。
    /// </summary>
    public interface IMatchSessionFactory
    {
        /// <summary>建 Match 会话：Match Scope（资源域）+ Match 服务域 + 会话客户端端口
        /// <c>IBattleClient</c>（经 <c>IBattleClientFactory</c> 创建，已发 Join）。
        /// 失败就地回滚（作用域与服务域 Dispose 后重抛）。</summary>
        IMatchSession Create(string host, int port, string roomId, string token, bool testRoom);
    }
}
