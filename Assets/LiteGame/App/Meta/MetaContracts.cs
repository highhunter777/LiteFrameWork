using System;
using System.Collections.Generic;
using System.Globalization;
using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>
    /// Meta 端点契约的客户端面（《上云测试专项设计》§2 最小闭环：游客登录 → Join Ticket → 对局 → 结算可查）。
    /// JSON 形状与服务端 `MetaServer.Contracts`（camelCase 序列化）一一对应；解析单源在 <see cref="MetaJson"/>。
    /// 本件零引擎、零 Newtonsoft——纯数据与结果形状，可直测。
    ///
    /// **结果型返回**（不抛业务异常——服务端 <c>JoinTicketIssueUseCase.Result</c> 同款纪律）：
    /// 传输/形状问题一律落 <see cref="MetaCall{T}.Failure"/>，调用方按 <see cref="MetaFailureKind"/>
    /// 分支处置（Network 可重试、Rejected 按 code 展示、InvalidResponse 确定性不重试）。
    /// </summary>
    public sealed class GuestLoginInfo
    {
        public string AccountId;
        public string AccessToken;
        public long ExpiresAtMs;
    }

    /// <summary>Join Ticket 签发结果（<c>/lobby/join-ticket</c>；Address = "host:port"，见 <see cref="RoomEndpoint"/>）。</summary>
    public sealed class JoinTicketInfo
    {
        public string InstanceId;
        public string Address;
        public string RoomId;
        public string Ticket;
        public long ExpiresAtMs;
    }

    /// <summary>房间明细（实例投影内单行：roomId/相位/在场/满员/地图）。</summary>
    public sealed class LobbyRoomEntry
    {
        public string RoomId;
        public int Phase;
        public int Players;
        public int ExpectedPlayers;

        /// <summary>本房地图行键（多图批；0 = 未标注——显示面按"未标注"回落，不作判据）。</summary>
        public int MapId;
    }

    /// <summary>实例投影（<c>/lobby/rooms/public</c> 单行：实例身份 + 该实例的房间明细列表）。</summary>
    public sealed class LobbyInstanceView
    {
        public string InstanceId;
        public string BuildHash;
        public string Address;
        public int PlayerCount;
        public bool Draining;
        public IReadOnlyList<LobbyRoomEntry> Rooms;
    }

    /// <summary>单局战绩（<c>/matches</c> 单行；结束原因值域与服务端枚举同源编号）。</summary>
    public sealed class MatchRecordInfo
    {
        public string MatchId;
        public int Kills;
        public int Deaths;
        public int EndReason;
        public int GameplayEndReason;
        public long FinishedAtMs;
    }

    /// <summary>失败分类（处置语义见各分支注释；HttpStatus=0 表示未拿到 HTTP 响应）。</summary>
    public enum MetaFailureKind
    {
        /// <summary>传输层失败（连不上/DNS/超时/中断）——暂态，可重试。</summary>
        Network = 0,

        /// <summary>服务端拒绝（非 2xx）：Code 携带 meta.* 稳定错误码（如 auth.unauthorized / lobby.no-capacity /
        /// lobby.version-mismatch），HttpStatus 携带状态码；409/401 等确定性，5xx 暂态。</summary>
        Rejected = 1,

        /// <summary>响应形状非法（2xx 但字段缺失/类型不符/坏 JSON）——确定性，重试无效。</summary>
        InvalidResponse = 2,
    }

    /// <summary>失败载荷（Kind + 状态码 + 稳定错误码 + 诊断明细；不含敏感原文）。</summary>
    public sealed class MetaFailure
    {
        public MetaFailureKind Kind;
        public long HttpStatus;
        public string Code;
        public string Detail;

        /// <summary>
        /// 是否"凭证失效"（401 / `auth.unauthorized`）：恢复链据此走**账号重入**（回登录页），
        /// 而不是普通重试或继续用旧令牌（旧令牌重放只会继续 401）。
        /// </summary>
        public bool IsAuthLost => Kind == MetaFailureKind.Rejected
            && (HttpStatus == MetaErrorCodes.HttpUnauthorized
                || string.Equals(Code, MetaErrorCodes.Unauthorized, StringComparison.Ordinal));

        public static MetaFailure Network(string detail)
            => new MetaFailure { Kind = MetaFailureKind.Network, HttpStatus = 0, Detail = detail ?? "" };

        public static MetaFailure Rejected(long httpStatus, string code, string detail)
            => new MetaFailure
            {
                Kind = MetaFailureKind.Rejected,
                HttpStatus = httpStatus,
                Code = code ?? "",
                Detail = detail ?? "",
            };

        public static MetaFailure Invalid(string detail)
            => new MetaFailure { Kind = MetaFailureKind.InvalidResponse, HttpStatus = 0, Detail = detail ?? "" };
    }

    /// <summary>单次 Meta 调用的结果（Ok=true 时 Value 有效；否则 Failure 有效）。</summary>
    public sealed class MetaCall<T>
    {
        public bool Ok { get; }
        public T Value { get; }
        public MetaFailure Failure { get; }

        private MetaCall(bool ok, T value, MetaFailure failure)
        {
            Ok = ok;
            Value = value;
            Failure = failure;
        }

        public static MetaCall<T> OkResult(T value) => new MetaCall<T>(true, value, null);

        public static MetaCall<T> Failed(MetaFailure failure)
            => new MetaCall<T>(false, default, failure ?? throw new ArgumentNullException(nameof(failure)));
    }

    /// <summary>稳定错误码的单源（与服务端 <c>AuthErrorCodes</c> 同值；客户端只比对，不发明）。</summary>
    public static class MetaErrorCodes
    {
        /// <summary>访问令牌缺失/失效（服务端 401；恢复链判"账号重入"）。</summary>
        public const string Unauthorized = "auth.unauthorized";

        /// <summary>凭证失效的 HTTP 状态码（与 <see cref="Unauthorized"/> 同判据，两端都收）。</summary>
        public const long HttpUnauthorized = 401;
    }

    /// <summary>端点路径单源（服务端 <c>AuthEndpoints/LobbyEndpoints/SettlementEndpoints.Map</c> 的挂载名——
    /// 无 /api 前缀；改动需两端同步核对）。</summary>
    public static class MetaEndpoints
    {
        public const string GuestLogin = "/auth/guest";
        public const string JoinTicket = "/lobby/join-ticket";
        public const string CreateRoom = "/lobby/create-room";
        public const string RoomsPublic = "/lobby/rooms/public";
        public const string Matches = "/matches";
    }

    /// <summary>
    /// 房间服端点（票据/投影下发的 Address="host:port" 拆解）。
    /// 解析规则与服务端 <c>LobbyRegistrationClient</c> 的 <c>AdvertiseHost + ":" + Port</c> 拼接互逆；
    /// 支持裸 IPv4/主机名与 [IPv6]:port 两种形态。纯函数可直测。
    /// </summary>
    public sealed class RoomEndpoint
    {
        public string Host;
        public int Port;

        /// <summary>解析 "host:port"；失败给 reason（消费端按确定错误态展示，不猜默认）。</summary>
        public static bool TryParse(string address, out RoomEndpoint endpoint, out string reason)
        {
            endpoint = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(address)) { reason = "地址为空"; return false; }

            string host;
            string portText;
            if (address[0] == '[')
            {
                // [IPv6]:port
                int close = address.IndexOf(']');
                if (close < 0 || close + 1 >= address.Length || address[close + 1] != ':')
                { reason = "括号形态缺端口"; return false; }
                host = address.Substring(1, close - 1);
                portText = address.Substring(close + 2);
            }
            else
            {
                int colon = address.LastIndexOf(':');
                if (colon <= 0 || colon + 1 >= address.Length) { reason = "缺端口"; return false; }
                host = address.Substring(0, colon);
                portText = address.Substring(colon + 1);
            }

            if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                || port < 1 || port > 65535)
            { reason = "端口非法"; return false; }

            endpoint = new RoomEndpoint { Host = host, Port = port };
            return true;
        }
    }
}
