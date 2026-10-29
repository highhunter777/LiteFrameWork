using LiteNet.Diagnostics;

namespace LiteNet
{
    /// <summary>
    /// RoomClient 的会话诊断发射点（M3-6 拆分的行内件）：进房尝试/未获应答两条记录——
    /// 与服务器 RejectJoin 的拒绝记录**同键**（<see cref="DiagTrace.JoinKey"/> 单源），
    /// 一次失败可把 版本/内容/事务/会话/房间 的记录串起来（《框架先行》§8「可诊断」）。
    ///
    /// 本类只写诊断事实，不参与会话状态与判定。
    /// </summary>
    internal static class RoomClientDiagnostics
    {
        /// <summary>Join 已发出（等待应答）。`contentContext` = 组合根注入的内容/事务上下文（空 = 未注入）。</summary>
        public static void JoinAttempt(string roomId, int playerId, string buildHash, string contentContext)
            => DiagTrace.Emit(DiagStage.Session, DiagCode.JoinAttempt,
                DiagTrace.JoinKey(roomId, playerId, buildHash), ContentDetail(contentContext));

        /// <summary>Join 未获应答即断开（客户端侧事实；与服务器记录同键）。</summary>
        public static void JoinNoAckDisconnected(string roomId, int playerId, string buildHash, string contentContext)
            => DiagTrace.Emit(DiagStage.Session, DiagCode.JoinNoAckDisconnected,
                DiagTrace.JoinKey(roomId, playerId, buildHash), ContentDetail(contentContext));

        private static string ContentDetail(string contentContext)
            => "content=" + (string.IsNullOrEmpty(contentContext) ? "-" : contentContext);
    }
}
