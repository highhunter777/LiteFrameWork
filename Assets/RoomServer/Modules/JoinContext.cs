namespace RoomServer.Application
{
    /// <summary>
    /// Join 验证上下文（《商业级通用服务端框架总设计》§P0-6）：验证器据此比对**绑定项**。
    /// 由宿主在收到 Join 时构造——房号/buildHash 取自本服**实际**配置，不由票据自称。
    /// </summary>
    public sealed class JoinContext
    {
        /// <summary>本服房间号（票据 rid 必须等于它）。</summary>
        public string RoomId;

        /// <summary>本服构建哈希（票据签发的 buildHash 必须等于它——版本红线）。</summary>
        public string BuildHash;

        /// <summary>期望受众（本服/本集群标识）。空 = 不做受众校验。</summary>
        public string Audience;

        /// <summary>期望 Sim/协议版本。空 = 不做版本校验（buildHash 已覆盖代码身份，此项留给玩法版本分离后启用）。</summary>
        public string Version;

        /// <summary>判定时刻（服务端单调时钟毫秒）。**过期判定只用它**，不用客户端时间。</summary>
        public long NowMs;

        public JoinContext(string roomId, string buildHash, string audience, long nowMs)
        {
            RoomId = roomId;
            BuildHash = buildHash;
            Audience = audience;
            NowMs = nowMs;
        }
    }
}
