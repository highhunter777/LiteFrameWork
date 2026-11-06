using System.Threading;
using LiteNet.Proto;
using LiteNet.Diagnostics;   // DiagStage / DiagCode（诊断段落与分类码单源，跨模块共享词表）

using LiteSync.Client;
namespace RoomServer.Application
{
    /// <summary>
    /// 入站准入门（Join admission）：**进房请求在消耗任何权威资源之前的全部拒绝判据** （《商业级通用服务端框架总设计》§P0-6 / 《框架先行》§8「可诊断」。）。
    /// </summary>
    public sealed class JoinAdmissionGate
    {
        private readonly IJoinTicketValidator _tickets;
        private readonly RateLimiter _rateLimit;
        private readonly IRoomRemoteAddress _remoteAddress;
        private readonly string _serverBuildHash;
        private readonly string _audience;

        /// <summary>
        /// Join 字段 UTF-8 字节上限（P0-3：所有字符串限制 UTF-8 字节数——防超长串打爆日志/内存）。
        /// </summary>
        public const int MaxRoomIdBytes = JoinFieldLimits.RoomIdBytes;
        public const int MaxTokenBytes = JoinFieldLimits.TokenBytes;
        public const int MaxBuildHashBytes = JoinFieldLimits.BuildHashBytes;

        /// <param name="ticketValidator">票据验证器；null = 未装配（退回原型级非空校验，生产必须装配）。</param>
        /// <param name="rateLimit">分层限流器（IP/账号两个入场维度）。</param>
        /// <param name="remoteAddress">远端地址探测口（IP 维度限流需要；探测不到即跳过该维度）。</param>
        /// <param name="serverBuildHash">本服版本锚点（buildHash 逐字比对）。</param>
        /// <param name="audience">本服受众标识（票据 audience 比对；空 = 不校验受众）。</param>
        public JoinAdmissionGate(
            IJoinTicketValidator ticketValidator,
            RateLimiter rateLimit,
            IRoomRemoteAddress remoteAddress,
            string serverBuildHash,
            string audience)
        {
            _tickets = ticketValidator;
            _rateLimit = rateLimit ?? throw new System.ArgumentNullException(nameof(rateLimit));
            _remoteAddress = remoteAddress;
            _serverBuildHash = serverBuildHash ?? throw new System.ArgumentNullException(nameof(serverBuildHash));
            _audience = audience ?? string.Empty;
        }

        /// <summary>
        /// 准入结论：通过时 <see cref="Principal"/> 是已验签的身份（未装配验证器时为 null）。
        /// </summary>
        public readonly struct Decision
        {
            /// <summary>是否准予进房。</summary>
            public bool Admitted { get; }

            /// <summary>已验签身份（仅准予且装配了验证器时非 null）。</summary>
            public JoinPrincipal Principal { get; }

            /// <summary>诊断段落（<c>DiagStage.Build</c> / <c>DiagStage.Room</c>）。</summary>
            public string Stage { get; }

            /// <summary>分类码（<c>DiagCode.JoinRejected*</c>）。</summary>
            public string Code { get; }

            /// <summary>票据拒绝分类（仅票据验签失败时有效；其余 null）。</summary>
            public JoinTicketRejection? TicketRejection { get; }

            /// <summary>拒绝理由（不回显 token/payload——P0-3 与《Meta 专项》§13.1）。</summary>
            public string Reason { get; }

            private Decision(bool admitted, JoinPrincipal principal, string stage, string code,
                JoinTicketRejection? ticketRejection, string reason)
            {
                Admitted = admitted;
                Principal = principal;
                Stage = stage;
                Code = code;
                TicketRejection = ticketRejection;
                Reason = reason;
            }

            public static Decision Admit(JoinPrincipal principal)
                => new Decision(true, principal, null, null, null, null);

            public static Decision Reject(string stage, string code, string reason,
                JoinTicketRejection? ticketRejection = null)
                => new Decision(false, null, stage, code, ticketRejection, reason);
        }

        /// <summary>
        /// 跑完整准入链。
        /// </summary>
        public Decision Evaluate(in JoinRequest request, int connectionId, long nowMs)
        {
            // ① IP 入场频率：先于验签与建房。地址探测不到 → 放行（限不了看不见的地址）。
            if (!_rateLimit.TryAcquireIpEntry(RemoteAddressOf(connectionId), nowMs))
                return Decision.Reject(DiagStage.Room, DiagCode.JoinRejectedAdmission, "IP 入场频率超限");

            // ② P0-3 字符串边界：UTF-8 字节数上限（先于一切语义）
            if (OverByteLimit(request.RoomId, MaxRoomIdBytes)
                || OverByteLimit(request.Token, MaxTokenBytes)
                || OverByteLimit(request.BuildHash, MaxBuildHashBytes))
                return Decision.Reject(DiagStage.Room, DiagCode.JoinRejectedAdmission, "Join 字段超长");

            // ③ token 红线：空即拒绝
            if (string.IsNullOrEmpty(request.Token))
                return Decision.Reject(DiagStage.Room, DiagCode.JoinRejectedAdmission, "token 缺失");

            // ④ 版本红线：Sim/协议版本比对不符拒绝进房（**Build 段**——带两端哈希对照）
            if (request.BuildHash != _serverBuildHash)
                return Decision.Reject(DiagStage.Build, DiagCode.JoinRejectedBuildHash,
                    $"buildHash 不符：{request.BuildHash} != {_serverBuildHash}");

            // ⑤ 票据验签（**先于建房**，见类注释链序）
            JoinPrincipal validated = null;
            if (_tickets != null)
            {
                JoinPrincipal principal = _tickets.Validate(request.Token, new JoinContext(
                    request.RoomId, _serverBuildHash, _audience, nowMs));
                if (principal == null || !principal.IsValid)
                {
                    JoinTicketRejection reason = principal == null
                        ? JoinTicketRejection.Malformed
                        : principal.Rejection;
                    // 拒绝原因只打分类，**不打票据原文与字段值**
                    return Decision.Reject(DiagStage.Room, DiagCode.JoinRejectedTicket,
                        $"票据拒绝：{reason}", reason);
                }
                validated = principal;

                // ⑥ 账号入场频率：验签通过后才有 AccountId（nonce 已在验签中消费——
                //    此处被限即该票据作废，合法客户端远够余量，见 RateLimitSettings.Default）。
                if (!_rateLimit.TryAcquireAccountEntry(principal.AccountId, nowMs))
                    return Decision.Reject(DiagStage.Room, DiagCode.JoinRejectedAdmission, "账号入场频率超限");
            }

            return Decision.Admit(validated);
        }

        /// <summary>远端地址探测（IP 维度限流的输入；探测不到 → null → 限流器按"跳过该维度"处理）。</summary>
        private string RemoteAddressOf(int connectionId)
            => _remoteAddress == null ? null : _remoteAddress.GetRemoteAddress(connectionId);

        /// <summary>UTF-8 字节数上限（不是字符数——防多字节字符绕过）。</summary>
        private static bool OverByteLimit(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return System.Text.Encoding.UTF8.GetByteCount(value) > maxBytes;
        }
    }

    /// <summary>远端地址探测窄口（宿主适配 Transport；准入只依赖"能不能拿到地址"这一件事）。
    /// 批3 随 <see cref="JoinAdmissionGate"/> 公共化——构造注入端口进公共签名。</summary>
    public interface IRoomRemoteAddress
    {
        /// <summary>取连接的远端地址；探测不到返回 null（调用方按"限不了看不见的地址"处理）。</summary>
        string GetRemoteAddress(int connectionId);
    }
}
