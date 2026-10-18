using System;
using System.Security.Cryptography;
using System.Text;
using MetaServer.Contracts.Lobby;
using MetaServer.Contracts.Validation;

namespace MetaServer.Modules.Lobby
{
    /// <summary>
    /// Join Ticket 签发用例（《Meta 服务专项设计》§7"Lobby 完成分配后签发票据，与
    /// roomId/matchId 一并下发"；分配 = 容量驱动 + 版本准入）。
    ///
    /// 流程：字段边界 → 分配实例（<see cref="InstanceRegistry.TryAllocate"/>）→ 按实例构建哈希
    /// 盖章签发（<see cref="LobbyTicketSigner"/>；票据哈希 = 实例上报事实，房间端将与本服比对）。
    ///
    /// - **结果型返回**（不抛业务异常——端点按结果映射 HTTP，与游客登录同款）。
    /// - 调用方已通过访问令牌鉴权：<paramref name="accountId"/> 来自令牌声明；
    ///   游客模型下 playerId = accountId（席位身份由房间分配，票据字段是 Meta 侧身份）。
    /// - **票据与账号标识不写日志**（§11.1；本类不持日志面）。
    /// </summary>
    public sealed class JoinTicketIssueUseCase
    {
        /// <summary>requestId/accountId 的 UTF-8 字节上限（**别名**：上限表单源 <see cref="FieldLimits.ShortIdBytes"/>）。</summary>
        public const int MaxFieldBytes = FieldLimits.ShortIdBytes;

        /// <summary>房间号 UTF-8 字节上限（**别名** <see cref="FieldLimits.BusinessKeyBytes"/>；与房间端
        /// JoinAdmissionGate.MaxRoomIdBytes 同值——跨端各自钉死，不互相引用）。</summary>
        public const int MaxRoomIdBytes = FieldLimits.BusinessKeyBytes;

        /// <summary>声明构建哈希的 UTF-8 字节上限（**别名** <see cref="FieldLimits.BusinessKeyBytes"/>）。</summary>
        public const int MaxBuildHashBytes = FieldLimits.BusinessKeyBytes;

        private readonly InstanceRegistry _registry;
        private readonly LobbyTicketSigner _signer;
        private readonly string _defaultRoomId;
        private readonly long _ticketTtlMs;
        private readonly Func<long> _nowMs;

        /// <summary>
        /// 测试房口令（联机测试房批）：非空才允许签发**带 template 的票据**；空 = 测试房功能关闭
        /// （请求带 template 一律 403）。值来自机器私有环境变量，**不进仓库、不回显、不落日志**。
        /// </summary>
        private readonly string _gmRoomKey;

        /// <param name="defaultRoomId">请求未带房间号时的缺省；空 = 必须由请求给出。</param>
        /// <param name="ticketTtlMs">票据有效期（毫秒；短期——MetaConfig 范围校验把守）。</param>
        /// <param name="nowMs">单调毫秒时钟（注入——签发时刻与过期时刻同源，§5.2 以服务端时钟为准）。</param>
        /// <param name="gmRoomKey">测试房口令（可空 = 功能关闭）；校验在 <see cref="Issue"/> 单点做。</param>
        public JoinTicketIssueUseCase(InstanceRegistry registry, LobbyTicketSigner signer,
            string defaultRoomId, long ticketTtlMs, Func<long> nowMs, string gmRoomKey = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _signer = signer ?? throw new ArgumentNullException(nameof(signer));
            _defaultRoomId = defaultRoomId ?? string.Empty;
            if (ticketTtlMs <= 0) throw new ArgumentOutOfRangeException(nameof(ticketTtlMs), ticketTtlMs, "票据有效期必须 > 0");
            _ticketTtlMs = ticketTtlMs;
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            _gmRoomKey = gmRoomKey ?? string.Empty;
        }

        /// <summary>签发结果（端点映射：Accepted→200；Rejected→按码；NoCapacity→503）。</summary>
        public abstract class Result
        {
            private Result() { }

            public sealed class Accepted : Result
            {
                public string InstanceId;
                public string Address;
                public string RoomId;
                public string Ticket;
                public long ExpiresAtMs;
            }

            public sealed class Rejected : Result
            {
                public string ErrorCode;
                public string MessageKey;
            }

            public sealed class NoCapacity : Result
            {
            }
        }

        public Result Issue(string accountId, JoinTicketCommand command)
        {
            if (!FieldValidation.IsValid(accountId, MaxFieldBytes)
                || !FieldValidation.IsValid(command?.RequestId, MaxFieldBytes))
                return Invalid();

            string roomId = string.IsNullOrEmpty(command.RoomId) ? _defaultRoomId : command.RoomId;
            if (!FieldValidation.IsValid(roomId, MaxRoomIdBytes))
                return Invalid();
            if (!string.IsNullOrEmpty(command.BuildHash)
                && !FieldValidation.IsValid(command.BuildHash, MaxBuildHashBytes))
                return Invalid();
            if (command.MapId < 0)
                return Invalid();   // 负值 = 请求错误（0 = 未指定合法；值域上界/是否有此行归房间端表校验）
            if (command.ExpectedPlayers < 0)
                return Invalid();   // 同口径：负值 = 请求错误（0 = 未指定；上界归房间端按内核席位容量校验）

            // 房间模板（联机测试房批）：**授权只在这里做**——join-ticket 与 create-room 共用本用例，
            // 放外层会漏掉"直连 join-ticket 带 template"这条旁路。
            string template = command.Template ?? string.Empty;
            if (template.Length > 0)
            {
                if (!IsValidTemplateName(template)) return Invalid();
                if (!MatchesGmRoomKey(command.GmKey))
                {
                    return new Result.Rejected
                    {
                        ErrorCode = LobbyErrorCodes.Forbidden,
                        MessageKey = LobbyErrorCodes.MessageKeyForbidden,
                    };
                }
            }

            if (!_registry.TryAllocate(command.BuildHash, out InstanceRegistry.Entry chosen, out bool versionConflict))
            {
                // 版本不符与无容量分开：前者换客户端构建/等新实例，后者等待或扩容——处置不同（§7 版本准入）
                return versionConflict
                    ? new Result.Rejected
                    {
                        ErrorCode = LobbyErrorCodes.VersionMismatch,
                        MessageKey = LobbyErrorCodes.MessageKeyVersionMismatch,
                    }
                    : new Result.NoCapacity();
            }

            string ticket = _signer.Issue(accountId, accountId, roomId, chosen.BuildHash, command.MapId,
                command.ExpectedPlayers, template, _nowMs(), _ticketTtlMs, out long expiresAtMs);
            return new Result.Accepted
            {
                InstanceId = chosen.InstanceId,
                Address = chosen.Address,
                RoomId = roomId,
                Ticket = ticket,
                ExpiresAtMs = expiresAtMs,
            };
        }

        /// <summary>模板名文法：1..32 位、小写字母/数字/下划线/短横线（与房间服模板键同形；不放行空格/点）。</summary>
        private static bool IsValidTemplateName(string template)
        {
            if (template.Length == 0 || template.Length > 32) return false;
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>口令比对（恒定时间；任一为空即 false——**空口令 = 测试房关闭**，不靠"比对通过"兜底）。</summary>
        private bool MatchesGmRoomKey(string presented)
        {
            if (_gmRoomKey.Length == 0 || string.IsNullOrEmpty(presented)) return false;
            byte[] given = Encoding.UTF8.GetBytes(presented);
            byte[] expected = Encoding.UTF8.GetBytes(_gmRoomKey);
            return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
        }

        private static Result.Rejected Invalid()
        {
            return new Result.Rejected
            {
                ErrorCode = LobbyErrorCodes.InvalidRequest,
                MessageKey = LobbyErrorCodes.MessageKeyInvalidRequest,
            };
        }

    }
}
