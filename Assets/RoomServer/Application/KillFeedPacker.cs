using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 击杀窗口 → wire（单向映射；《战斗HUD层级设计》§2 击杀提示面）：
    /// 把房间内核的纯数据 <see cref="KillRecordWindow"/> 灌进快照的比赛状态层（<c>MatchStateDelta.recent_kills</c>）。
    ///
    /// **为什么单列**：wire 映射是 App 层职责（RoomServer.Runtime 禁见 proto——R11）；
    /// 快照广播（<see cref="SnapshotPipeline"/>）与重连响应（<see cref="ReconnectResponder"/>）两条路共用本件。
    /// </summary>
    public static class KillFeedPacker
    {
        /// <summary>清空并重灌最近击杀（幂等：<paramref name="window"/> 为 null 时不动）。</summary>
        public static void Fill(Proto.MatchStateDelta match, KillRecordWindow window)
        {
            if (match == null || window == null) return;

            match.RecentKills.Clear();
            for (int i = 0; i < window.Count; i++)
            {
                RoomKillRecord r = window.Get(i);
                match.RecentKills.Add(new Proto.KillRecord
                {
                    Killer = r.Killer,
                    Victim = r.Victim,
                    Frame = r.Frame,
                    WeaponSlot = r.WeaponSlot,
                });
            }
        }
    }
}
