namespace RoomServer
{
    /// <summary>
    /// 结算奖励换算（《游戏业务系统总设计》§342 `RewardDelta{exp,currency,items,consumptionDelta}`； 《状态同步专项设计》§"结算只输出签名 RewardDelta"。）。
    /// </summary>
    public interface ISettlementRewardPolicy
    {
        /// <summary>
        /// 该玩家本局的奖励增量（账本 `Delta`；0 = 无经济/不发奖）。
        /// 入参是本局**受验证的冻结事实**（席位 playerId + 击杀/死亡），不含客户端上报值。
        /// </summary>
        long RewardDelta(int playerId, int kills, int deaths);
    }

    /// <summary>
    /// 无经济实现（当前口径）：delta 恒 0——账本 `revision` 仍推进、余额不变。
    /// 判定理由见 <see cref="ISettlementRewardPolicy"/> 的类注释（设计未定口径时不得自造数字）。
    /// </summary>
    public sealed class NoEconomyRewardPolicy : ISettlementRewardPolicy
    {
        /// <summary>无状态单例（装配缺省值与测试共用）。</summary>
        public static readonly NoEconomyRewardPolicy Instance = new NoEconomyRewardPolicy();

        public long RewardDelta(int playerId, int kills, int deaths) => 0L;
    }
}
