using System;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// 验证通过的票据主体（《商业级通用服务端框架总设计》§7"建议最小接口"）。
    ///
    /// **形态由设计钉死**：<c>JoinPrincipal Validate(string ticket, JoinContext context)</c>——
    /// 因此失败不走异常也不走 bool，而是返回 <see cref="Rejection"/> != None 的主体。
    /// 调用方**必须先查 <see cref="IsValid"/>**，不得只看非空。
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

    /// <summary>
    /// Join 票据验证端口（《商业级通用服务端框架总设计》§P0-6/§7；《框架先行》§5-4）。
    ///
    /// **归属 RoomServer 侧**（设计 §7 工程结构 + Meta 专项 §15"R2：RoomServer 侧 Join Ticket 本地验签"）：
    /// Meta/Lobby 用**私钥签发**，RoomServer 用**本地公钥验签**；对局热路径**不查询** Profile 数据库。
    ///
    /// **实现约定**：
    /// - 必须 **fail-closed**——任何异常/缺失/不确定一律返回拒绝，不得降级放行；
    /// - 必须**无状态可注入时钟**（<see cref="JoinContext.NowMs"/> 由调用方给），不得自读系统时钟——
    ///   否则 L1 无法构造过期/回拨用例（与 <see cref="IMonotonicClock"/> 同一纪律）；
    /// - 必须**不查库**（设计明文），密钥/公钥环在进程内；
    /// - 重放判定由实现维护**有界**一次性消费窗口（设计 §20 完成定义第 4 条：任何队列/缓存/票据窗口
    ///   都必须有显式容量与清理策略）。
    ///
    /// <see cref="JoinPrincipalValidate"/> 不得抛出——验证失败是**预期路径**，不是异常路径。
    /// </summary>
    public interface IJoinTicketValidator
    {
        JoinPrincipal Validate(string ticket, JoinContext context);
    }

    /// <summary>
    /// 验签密钥（kid 允许密钥轮换——§16 安全测试清单"密钥轮换"；算法固定 HMAC-SHA256，**不自创密码算法**）。
    /// 密钥**不入库**，由部署注入（Meta 专项 §13.1：密钥与票据原文都不得进日志）。
    /// </summary>
    public sealed class JoinTicketKey
    {
        /// <summary>密钥标识（票据明文携带；未知 kid 直接拒绝）。</summary>
        public readonly string Kid;

        private readonly byte[] _secret;

        public JoinTicketKey(string kid, byte[] secret)
        {
            if (string.IsNullOrEmpty(kid)) throw new ArgumentException("kid 不能为空", nameof(kid));
            if (secret == null || secret.Length == 0) throw new ArgumentException("secret 不能为空", nameof(secret));
            if (!KidPattern.IsMatch(kid)) throw new ArgumentException("kid 只允许 [A-Za-z0-9_-]{1,32}", nameof(kid));
            Kid = kid;
            _secret = (byte[])secret.Clone();   // 防御性拷贝：外部改不到内部密钥
        }

        /// <summary>从 base64 装配（宿主配置形态）。</summary>
        public static JoinTicketKey FromBase64(string kid, string base64Secret)
        {
            if (string.IsNullOrEmpty(base64Secret)) throw new ArgumentException("密钥不能为空", nameof(base64Secret));
            return new JoinTicketKey(kid, Convert.FromBase64String(base64Secret));
        }

        internal byte[] Secret
        {
            get { return _secret; }
        }

        /// <summary>kid 形状约束：**保证不含分隔符 <c>.</c>**（含则票据被拆成错误段数）。</summary>
        private static readonly Regex KidPattern = new Regex(@"^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);
    }
}
