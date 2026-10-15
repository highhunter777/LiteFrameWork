using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 开火窗口 → wire（单向映射；远端开火火光的公共面源）：
    /// 把房间内核的纯数据 <see cref="FireRecordWindow"/> 灌进快照的比赛状态层（<c>MatchStateDelta.recent_fires</c>）。
    ///
    /// **为什么单列**：wire 映射是 App 层职责（RoomServer.Runtime 禁见 proto——R11）；
    /// 快照广播（<see cref="SnapshotPipeline"/>）与重连响应（<see cref="ReconnectResponder"/>）两条路共用本件。
    /// </summary>
    public static class FireSignalPacker
    {
        /// <summary>清空并重灌最近开火（幂等：<paramref name="window"/> 为 null 时不动）。</summary>
        public static void Fill(Proto.MatchStateDelta match, FireRecordWindow window)
        {
            if (match == null || window == null) return;

            match.RecentFires.Clear();
            for (int i = 0; i < window.Count; i++)
            {
                RoomFireRecord r = window.Get(i);
                match.RecentFires.Add(new Proto.FireRecord
                {
                    Shooter = r.Shooter,
                    Frame = r.Frame,
                });
            }
        }
    }
}
