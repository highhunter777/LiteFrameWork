using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim;
using cfg;

namespace LiteGame.Modules.Config
{
    /// <summary>Luban 专用的配置服务视图（Tables 是 Luban 生成类型）；消费者必须本来就认识 Luban。</summary>
    public interface IConfigService
    {
        /// <summary>表是否已装载并发布。</summary>
        bool Loaded { get; }

        /// <summary>候选构建→校验→原子发布；完成即放行（Preload 尾部放行判据）。</summary>
        UniTask LoadAsync(CancellationToken ct);

        cfg.Tables Tables { get; }

        /// <summary>玩法数据身份基摘要（判定面表字节 + 规则位 → 两端 ConfigHash 对账用）。</summary>
        uint GameplayDigestBase { get; }

        /// <summary>B3 战斗策略投影（规则/排行/伤害数字参数；表装载后构建，缺行回落 C# 默认）。
        /// 未装载即访问抛——装配序上消费点（对局/本地测试房/战斗只读事实）恒在 Preload 之后。</summary>
        BattleStrategyProfile Strategy { get; }

        /// <summary>
        /// 表装载的判定数值（不含覆写）：进房单点按门合并一次，客户端发布面与本地服同源。
        /// 未装载即访问抛（与 <see cref="Tables"/> 同判据）。
        /// </summary>
        CombatValues TableCombatValues { get; }
    }
}
