namespace LiteFramework
{
    /// <summary>
    /// 日志关联上下文（Session/Match/Frame/Player）。用 <see cref="ILog.BeginScope"/> 压栈，
    /// 代替静态 SetMatchContext/SetFrame 的散落写入；空串/0/-1 表示沿用外层/默认。
    /// </summary>
    public readonly struct LogScope
    {
        public readonly string SessionId;
        public readonly string MatchId;
        public readonly int Frame;
        public readonly int PlayerId;

        public LogScope(string sessionId, string matchId, int frame, int playerId)
        {
            SessionId = sessionId;
            MatchId = matchId;
            Frame = frame;
            PlayerId = playerId;
        }

        public LogScope WithFrame(int frame)
            => new LogScope(SessionId, MatchId, frame, PlayerId);
    }
}
