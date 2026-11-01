using System;
using LiteFramework;
using LiteNet.Proto;
using LiteNet.Transport;
using LiteSync.Client;

namespace LiteGame
{
    /// <summary>
    /// Account 域会话工厂（《客户端依赖注入专项设计》§6 服务作用域）：把动态进房参数变成可移交的
    /// <see cref="IAccountSession"/>——**类型化工厂**（不用裸 new + 手工拼作用域散落进流程）；
    /// 返回端口而非实现件，流程只认契约。
    /// 传输选择经构造注入的委托由**产品装配根**决定（本地测试模式/真 KCP 的裁决归
    /// <see cref="ProductTransportFactory.CreateTransport"/>——本类不认识测试开关语义）。
    ///
    /// **零引擎依赖**（ClientScope/ServiceContainer/IBattleClient/IClientTransport 皆 L1 可编）：
    /// 本件被 .NET 测试工程**源链接编译**——工厂行为在 L1 直测（离场/失败/再进房无残留），
    /// 不靠 Unity 才能验证。
    /// </summary>
    public interface IAccountSessionFactory
    {
        /// <summary>建 Account 会话：Account Scope（资源域）+ Account 服务域 + 会话客户端端口
        /// <c>IBattleClient</c>（经 <c>IBattleClientFactory</c> 创建，已发 Join）。
        /// 失败就地回滚（作用域与服务域 Dispose 后重抛）。</summary>
        IAccountSession Create(string host, int port, string roomId, string token, bool testRoom);
    }
}
