namespace LiteSim
{
    /// <summary>
    /// 局内背包单格（《游戏业务系统总设计》§7.1：固定 12 格、4 快捷栏，不放对象引用）。
    /// 数组布局：<c>MatchBag[slot * <see cref="SimConfig.MatchBagSlotsPerEntity"/> + i]</c>。
    /// 私有面：**只发给本人**（PrivateStateSnapshot.bag）——不进公共 SlotDelta（§1 P0 阻塞项：
    /// 局内包曾无承载位，塞 Globals/CustomData 会重连分叉）。
    /// ItemDefId = 0 表示空格。
    /// </summary>
    public partial struct MatchBagSlot
    {
        /// <summary>物品定义 Id（Luban <c>tb_item</c>；0 = 空格）。**私有面**（本人局内背包）。</summary>
        [StateLayer(StateLayer.Private)]
        public int ItemDefId;

        /// <summary>堆叠数量。**私有面**（同上）。</summary>
        [StateLayer(StateLayer.Private)]
        public int Count;

        /// <summary>快捷栏绑定（0 = 未绑定；1..4）。**私有面**（同上）。</summary>
        [StateLayer(StateLayer.Private)]
        public int QuickSlot;
    }

    /// <summary>
    /// 比赛状态（§3.1 固定布局 MatchState；room 级单实例）。
    /// 《状态同步专项设计》§5.2：阶段、倒计时、比分、回合和胜者——比分/回合并的正式契约化
    /// （Globals 只留版本化扩展，见 §1 P0 阻塞项第 3 条）。
    /// 公共面：随每份快照全量下发（MatchStateSnapshot 层），全部客户端可见。
    /// 字段取舍：FFA 个人比分/KDA 在 <see cref="EntitySlot.Kills"/>/<see cref="EntitySlot.Deaths"/>；
    /// <c>Score</c> 只承载团队模式的单值比分（Team=1 时生效）。
    /// </summary>
    public struct MatchStateData
    {
        /// <summary>比赛阶段（0=未开始 1=进行 2=结算；取值域 P1 MatchSystem 冻结）。</summary>
        public int Phase;

        /// <summary>队伍模式（0=FFA 死亡竞赛；1=团队）。</summary>
        public int Team;

        /// <summary>团队比分（Team=1 时生效；FFA 恒 0）。</summary>
        public int Score;

        /// <summary>倒计时剩余帧（局长 3 分钟 = 10800 帧；0 = 不计时/已结束）。</summary>
        public int Timer;

        /// <summary>回合号（从 1 起；0 = 未开赛）。</summary>
        public int Round;

        /// <summary>胜者实体 Id（FFA）/队伍 Id（Team）；0 = 未定或终局平局。</summary>
        public long Winner;

        /// <summary>固定房间规则（公共快照）；0 = 不限击杀。</summary>
        public int KillLimit;
        public int RespawnDelayFrames;
        public int SpawnProtectionFrames;
        public MatchEndReason EndReason;
    }
}
