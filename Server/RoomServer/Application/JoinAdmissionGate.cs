using System.Threading;
using LiteNet.Proto;
using LiteNet.Diagnostics;   // DiagStage / DiagCode（诊断段落与分类码单源，跨模块共享词表）

namespace RoomServer.Application
{
    /// <summary>
    /// 入站准入门（Join admission）：**进房请求在消耗任何权威资源之前的全部拒绝判据**
    /// （《商业级通用服务端框架总设计》§P0-6 / 《框架先行》§8「可诊断」；分层限流见
    /// <see cref="RateLimitSettings"/>）。
    ///
    /// **为什么从 ServerHost 抽出**（单一职责）：准入是一条**独立可枚举、可独立测试**的判定链——
    /// 频率 → 字段边界 → token 红线 → 版本红线 → 票据验签 → 账号频率。任一条不通过都必须
    /// "拒绝且不留副作用"。它与房间生命周期、Worker 调度、快照广播无因果关系；
    /// 留在宿主里时只能经完整建房路径才能验证。
    ///
    /// **链序不可交换**（每步注释写了"为什么在这个位置"——这是本类存在的核心价值）：
    /// <list type="number">
    /// <item>IP 入场频率：先于验签与建房——频率校验在昂贵操作之前；</item>
    /// <item>字段字节边界：先于一切语义；超长包已由入站层按 PacketOversized/PacketRejects 归类，
    ///       不该在排空计数里再记一次；</item>
    /// <item>token 红线：空即拒绝；</item>
    /// <item>版本红线（buildHash）：身份判定，走 <c>Build</c> 诊断段（带两端哈希对照）；</item>
    /// <item>票据验签：<b>先于建房</b>——否则任何人拿垃圾 token 打不同 roomId 就能把
    ///       房间表撑到容量上限（拒绝服务）；</item>
    /// <item>账号入场频率：验签通过后才有 AccountId 可比对。</item>
    /// </list>
    ///
    /// **本类不做**（职责分离）：不建房、不入队命令、不写 Session 可变字段、不做拒绝分类计数——
    /// 那是 <see cref="ServerHost"/> 的事。本类只回答"准不准进"，并给出**可诊断的拒绝理由**。
    ///
    /// **无状态**：判据全部来自构造注入的口径 + 每次调用显式传入的请求事实；无缓存、无计数器。
    /// 拒绝分类计数归 <see cref="Ops"/>（宿主持有），本类只产出分类枚举。
    /// </summary>
    internal sealed class JoinAdmissionGate
    {
        private readonly IJoinTicketValidator _tickets;
        private readonly RateLimiter _rateLimit;
        private readonly IRoomRemoteAddress _remoteAddress;
        private readonly string _serverBuildHash;
        private readonly string _audience;

        /// <summary>
        /// Join 字段 UTF-8 字节上限（P0-3：所有字符串限制 UTF-8 字节数——防超长串打爆日志/内存）。
        /// **本类是这三个上限的唯一权威**（原在 ServerHost，判定链下沉时一并迁入）。
        /// </summary>
        public const int MaxRoomIdBytes = 64;
        public const int MaxTokenBytes = 256;
        public const int MaxBuildHashBytes = 128;

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
        /// 准入结论：通过时 <see cref="Principal"/> 是已验签的身份（未装配验证器时为 null）；
        /// 拒绝时 <see cref="Stage"/>/<see cref="Code"/>/<see cref="Reason"/> 构成诊断三段，
        /// 与客户端 <c>RoomClient</c> 的进房记录同形同键（<c>DiagTrace.JoinKey</c> 单源构造）。
        /// </summary>
        internal readonly struct Decision
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
        /// 跑完整准入链。<paramref name="nowMs"/> 是单调毫秒（限流与票据重放窗口共用）。
        /// 调用方须在任何建房/入队/写 Session **之前**调用——本方法自身无副作用，
        /// 但结论必须被当真执行（否则"拒绝"形同虚设）。
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

    /// <summary>远端地址探测窄口（宿主适配 Transport；准入只依赖"能不能拿到地址"这一件事）。</summary>
    internal interface IRoomRemoteAddress
    {
        /// <summary>取连接的远端地址；探测不到返回 null（调用方按"限不了看不见的地址"处理）。</summary>
        string GetRemoteAddress(int connectionId);
    }
}
