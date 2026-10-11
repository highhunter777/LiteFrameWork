namespace RoomServer
{
    /// <summary>
    /// 结算奖励换算（《游戏业务系统总设计》§342 `RewardDelta{exp,currency,items,consumptionDelta}`；
    /// 《状态同步专项设计》§"结算只输出签名 RewardDelta"；《Meta 服务专项设计》§3.1 分工表：
    /// **奖励数值计算归 RoomServer，Meta 只接受受验证 RewardDelta、绝不重算**）。
    ///
    /// **为什么这里是一个接缝而不是一套实现（P3 判定，如实登记）**：设计规定了 RewardDelta 的
    /// **形状与归属**（谁算、谁不重算、幂等怎么兜底），**没有规定任何奖励口径**——全仓（含玩法表
    /// `Assets/GameData` 与 `CombatValues`/`WeaponTable`）grep 无 reward/奖励字段，没有任何"每杀多少
    /// 经验/货币""道具如何换算"的定义。凭空定一套数字，等于把估算值当事实写死（服务端总设计 §520/§600
    /// 红线），且会成为对局经济的事实标准，比"暂时 0"更难收回。
    ///
    /// 故 P3 交付的是**唯一注入点 + 一个显式的无经济实现**：
    /// - 现在 = <see cref="NoEconomyRewardPolicy"/>（逐玩家 delta 0：账本 revision 推进、余额不变——
    ///   封闭测试口径，与 R3-Profile 记录一致）；
    /// - 经济口径定了（P4"局外包/Loadout/RewardDelta"）→ 新增一个实现并在装配处注入，
    ///   **不触碰提交管道、载荷形状与 Meta 侧任何代码**（这也是把换算放在提交方完成的意义）。
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
