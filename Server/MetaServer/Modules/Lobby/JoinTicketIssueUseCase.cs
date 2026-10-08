using System;
using System.Text;
using MetaServer.Contracts.Lobby;

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
        /// <summary>requestId/accountId 的 UTF-8 字节上限（与游客登录同口径，§P0-3）。</summary>
        public const int MaxFieldBytes = 128;

        /// <summary>房间号 UTF-8 字节上限（与房间端 JoinAdmissionGate.MaxRoomIdBytes 同值——跨端各自钉死，不互相引用）。</summary>
        public const int MaxRoomIdBytes = 64;

        /// <summary>声明构建哈希的 UTF-8 字节上限（同房间端 MaxBuildHashBytes 数量级）。</summary>
        public const int MaxBuildHashBytes = 64;

        private readonly InstanceRegistry _registry;
        private readonly LobbyTicketSigner _signer;
        private readonly string _defaultRoomId;
        private readonly long _ticketTtlMs;
        private readonly Func<long> _nowMs;

        /// <param name="defaultRoomId">请求未带房间号时的缺省；空 = 必须由请求给出。</param>
        /// <param name="ticketTtlMs">票据有效期（毫秒；短期——MetaConfig 范围校验把守）。</param>
        /// <param name="nowMs">单调毫秒时钟（注入——签发时刻与过期时刻同源，§5.2 以服务端时钟为准）。</param>
        public JoinTicketIssueUseCase(InstanceRegistry registry, LobbyTicketSigner signer,
            string defaultRoomId, long ticketTtlMs, Func<long> nowMs)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _signer = signer ?? throw new ArgumentNullException(nameof(signer));
            _defaultRoomId = defaultRoomId ?? string.Empty;
            if (ticketTtlMs <= 0) throw new ArgumentOutOfRangeException(nameof(ticketTtlMs), ticketTtlMs, "票据有效期必须 > 0");
            _ticketTtlMs = ticketTtlMs;
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
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
            if (!ValidField(accountId, MaxFieldBytes) || !ValidField(command?.RequestId, MaxFieldBytes))
                return Invalid();

            string roomId = string.IsNullOrEmpty(command.RoomId) ? _defaultRoomId : command.RoomId;
            if (!ValidField(roomId, MaxRoomIdBytes))
                return Invalid();
            if (!string.IsNullOrEmpty(command.BuildHash) && !ValidField(command.BuildHash, MaxBuildHashBytes))
                return Invalid();

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

            string ticket = _signer.Issue(accountId, accountId, roomId, chosen.BuildHash,
                _nowMs(), _ticketTtlMs, out long expiresAtMs);
            return new Result.Accepted
            {
                InstanceId = chosen.InstanceId,
                Address = chosen.Address,
                RoomId = roomId,
                Ticket = ticket,
                ExpiresAtMs = expiresAtMs,
            };
        }

        private static Result.Rejected Invalid()
        {
            return new Result.Rejected
            {
                ErrorCode = LobbyErrorCodes.InvalidRequest,
                MessageKey = LobbyErrorCodes.MessageKeyInvalidRequest,
            };
        }

        private static bool ValidField(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return Encoding.UTF8.GetByteCount(value) <= maxBytes;
        }
    }
}
