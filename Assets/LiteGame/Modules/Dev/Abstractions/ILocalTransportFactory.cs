using System;
using LiteNet.Transport;
using LiteSim;
using LiteGame.Modules.Map;

namespace LiteGame.Modules.Dev
{
    /// <summary>
    /// 本地（开发）传输工厂端口：消费方经 <c>IDevAssembly.LocalTransport</c> 取件（发布形态为 null）。
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
