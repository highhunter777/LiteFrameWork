namespace LiteSim
{
    /// <summary>
    /// 比赛状态（§3.1 固定布局 MatchState；room 级单实例）。
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

        /// <summary>
        /// 权威侧 GM 规则位（联机测试房批；1 = 免死 / 2 = 无限弹药）。
        /// </summary>
        public int GmFlags;
    }
}
