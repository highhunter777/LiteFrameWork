namespace RoomServer.Application
{
    /// <summary>
    /// 票据拒绝分类（fail-closed：《Meta 服务专项设计》§6.2"任何校验失败即作废该票据，不降级为'仅警告'"）。
    /// 分类进 Ops 计数与诊断日志——**票据原文与字段值不进日志**（Meta 专项 §13.1 禁写 token/票据/IP 全量值）。
    /// </summary>
    public enum JoinTicketRejection
    {
        /// <summary>未拒绝（验证通过的唯一取值）。</summary>
        None = 0,

        /// <summary>票据缺失或空。</summary>
        Missing,

        /// <summary>结构不合法（分段数/编码/字段超限/必填缺失）。</summary>
        Malformed,

        /// <summary>签名不匹配（篡改）。</summary>
        BadSignature,

        /// <summary>已过期（以**服务端时钟**为准，Meta 专项 §6.2"客户端时钟回拨"）。</summary>
        Expired,

        /// <summary>尚未生效（nbf 在未来——客户端时钟回拨或伪造）。</summary>
        NotYetValid,

        /// <summary>audience 不符（票据发给别的受众）。</summary>
        AudienceMismatch,

        /// <summary>roomId 不符（票据绑定的是别的房间）。</summary>
        RoomMismatch,

        /// <summary>buildHash 不符（版本红线：票据签发时的构建 ≠ 本服构建）。</summary>
        BuildHashMismatch,

        /// <summary>协议/Sim 版本不符。</summary>
        VersionMismatch,

        /// <summary>重放：nonce 已在消费窗口内用过（一次性）。</summary>
        Replayed,

        /// <summary>kid 未知——签发密钥不在本服公钥环内（含密钥轮换后旧 kid 已下线）。</summary>
        UnknownKey,
    }
}
