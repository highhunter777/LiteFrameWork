using System;
using LiteNet.Transport;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 本地（开发）传输工厂端口（路线图 M0 接缝 5）：<see cref="ProcedureMatch"/> 经本端口创建
    /// "进程内本地服"传输，App 程序集因此**不引用** <c>LiteGame.DevLocalServer</c> / <c>RoomServer.*</c>。
    ///
    /// 实现住开发装配层（`LiteGame.Dev`，仅编辑器/开发包编译）；发布形态未装订时
    /// <see cref="LocalTransportRegistry"/> 为空，本地测试模式显性失败而不是静默回落真网络。
    /// </summary>
    public interface ILocalTransportFactory
    {
        /// <summary>创建本地测试房传输（<paramref name="maps"/> 提供判定图——与客户端逐位同图）。</summary>
        IClientTransport Create(bool testRoom, MatchTestRules rules, Func<uint> gameplayDigestBase, IMapProvider maps);
    }
}
