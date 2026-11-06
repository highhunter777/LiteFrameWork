namespace RoomServer.Application
{
    /// <summary>
    /// 验证通过的票据主体（《商业级通用服务端框架总设计》§7"建议最小接口"）。
    /// </summary>
    public sealed class JoinPrincipal
    {
        /// <summary>席位身份（Meta 侧 playerId）。</summary>
        public string PlayerId;

        /// <summary>账号身份（可与 PlayerId 相同；两者都由 Meta 签发时绑定）。</summary>
        public string AccountId;

        /// <summary>票据绑定的房间（验证时已与 <see cref="JoinContext.RoomId"/> 比对通过）。</summary>
        public string RoomId;

        /// <summary>票据绑定的对局。</summary>
        public string MatchId;

        /// <summary>受众（验证时已与 <see cref="JoinContext.Audience"/> 比对通过）。</summary>
        public string Audience;

        /// <summary>一次性 nonce（重放判定的键；已在本服消费窗口中登记）。</summary>
        public string Nonce;

        /// <summary>过期时刻（单调毫秒）。</summary>
        public long ExpiresAtMs;

        /// <summary>票据盖章的地图行键（多图批；0 = 未指定——房间按模板/宿主默认图建房）。
        /// 建房时消费：既有房间不一致 → 拒绝进房（<c>InboundJoinHandlers.Handle</c>）。</summary>
        public int MapId;

        /// <summary>票据盖章的**期望人数**（开房参数批；0 = 未指定＝按房间模板）。**只作用于新建房**
        /// （既有房间以房事实为准，请求值不参与判定——回执见 <c>JoinAck.expected_players</c>）；
        /// 越界（超出内核席位容量）→ 拒绝建房。</summary>
        public int ExpectedPlayers;

        /// <summary>票据盖章的**房间模板名**（联机测试房批；空 = 未指定＝房间服 `default_template`）。
        /// **授权在 Meta 侧完成**（测试房口令）——本字段只决定"用哪个模板"，房间端按名查表、
        /// 不存在即拒绝建房（不静默回落）。只作用于新建房；既有房间以房事实为准。</summary>
        public string Template;

        /// <summary>拒绝原因；<see cref="JoinTicketRejection.None"/> = 验证通过。</summary>
        public JoinTicketRejection Rejection;

        /// <summary>是否验证通过。**唯一**可信判据（其余字段仅在通过后有值）。</summary>
        public bool IsValid
        {
            get { return Rejection == JoinTicketRejection.None; }
        }

        /// <summary>构造一个拒绝结果（不含任何票据字段——拒绝路径不留身份残留）。</summary>
        public static JoinPrincipal Rejected(JoinTicketRejection reason)
        {
            return new JoinPrincipal { Rejection = reason };
        }
    }
}
