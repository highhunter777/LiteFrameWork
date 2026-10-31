using LiteFramework;
using LiteSim;
using cfg;

namespace LiteGame.Modules.Config
{
    /// <summary>Luban 专用的配置服务视图（Tables 是 Luban 生成类型）；消费者必须本来就认识 Luban。</summary>
    public interface IConfigService : IConfigTableSource
    {
        cfg.Tables Tables { get; }

        /// <summary>玩法数据身份基摘要（判定面表字节 + 规则位 → 两端 ConfigHash 对账用）。</summary>
        uint GameplayDigestBase { get; }

        /// <summary>
        /// 表装载的判定数值（**不含覆写**）：判定参数覆写的合并源。
        /// 进房单点从这里出发、按门合并一次（<see cref="SimParamsActivation.Resolve"/>），
        /// 客户端发布面与本地服房间数值由同一结果派生——两端逐字段同值。
        /// 未装载即访问抛（与 <see cref="Tables"/> 同判据）。
        /// </summary>
        CombatValues TableCombatValues { get; }
    }
}
