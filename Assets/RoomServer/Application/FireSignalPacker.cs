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

    /// <summary>
    /// 命中窗口 → wire（单向映射；命中/伤害数字/受击特效/命中音效的公共面源）：
    /// 把房间内核的纯数据 <see cref="HitRecordWindow"/> 灌进 <c>MatchStateDelta.recent_hits</c>。
    /// 快照广播与重连响应两条路共用本件；同 (shooter,target,frame) 已在服务端聚合。
    /// </summary>
    public static class HitFeedPacker
    {
        /// <summary>清空并重灌最近命中（幂等：<paramref name="window"/> 为 null 时不动）。</summary>
        public static void Fill(Proto.MatchStateDelta match, HitRecordWindow window)
        {
            if (match == null || window == null) return;

            match.RecentHits.Clear();
            for (int i = 0; i < window.Count; i++)
            {
                RoomHitRecord r = window.Get(i);
                match.RecentHits.Add(new Proto.HitRecord
                {
                    Shooter = r.Shooter,
                    Target = r.Target,
                    Frame = r.Frame,
                    Value = r.Value,
                    PosX = r.Pos.X,
                    PosY = r.Pos.Y,
                    PosZ = r.Pos.Z,
                    Crit = r.Crit,
                });
            }
        }
    }
}
