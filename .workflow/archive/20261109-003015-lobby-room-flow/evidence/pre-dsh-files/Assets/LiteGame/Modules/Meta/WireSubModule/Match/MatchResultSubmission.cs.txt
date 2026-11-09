using System.Collections.Generic;

namespace MetaWire
{
    /// <summary>
    /// 对局结算提交（RoomServer → Meta）：一条提交同时承载 逐玩家受验证的奖励增量（账本面）与对局结果事实（归档面）。
    /// </summary>
    public class MatchResultSubmission
    {
        public string RequestId { get; set; }
        public string MatchId { get; set; }
        public long Seed { get; set; }
        public int FinalFrame { get; set; }
        public int EndReason { get; set; }
        public int GameplayEndReason { get; set; }
        public long WinnerEntityId { get; set; }

        /// <summary>逐玩家结算项（1..64 条；同一 playerId 在同一提交内只允许出现一次）。</summary>
        public List<PlayerResultEntry> Players { get; set; }
    }
}
