using System;
using LiteNet.Transport;
using LiteSim;
using LiteGame.Modules.Map;

namespace LiteGame
{
    /// <summary>
    /// 本地（开发）传输工厂端口（路线图 M0 接缝 5）：<see cref="ProductTransportFactory"/> 经本端口创建
    /// "进程内本地服"传输，App 程序集因此**不引用** <c>LiteGame.Dev</c> / <c>RoomServer.*</c>。
    ///
    /// 实现住开发装配层（`LiteGame.Dev`，仅编辑器/开发包编译）；发布形态未装订时
    /// <see cref="LocalTransportRegistry"/> 为空，本地测试模式显性失败而不是静默回落真网络。
    /// </summary>
    public interface ILocalTransportFactory
    {
        /// <summary>创建本地测试房传输（<paramref name="maps"/> 提供判定图——与客户端逐位同图；
        /// <paramref name="roomValues"/> 是本局判定数值——装配根在进房时点解析，与客户端发布面同一结果：
        /// 本地服建房与客户端预测必须逐字段同值）。</summary>
        IClientTransport Create(bool testRoom, MatchTestRules rules, CombatValues roomValues,
            Func<uint> gameplayDigestBase, IMapProvider maps, MatchRules? roomRules = null);
    }
}
