using System;
using LiteGame.Modules.Meta.Call;

namespace LiteGame.Modules.Meta.Abstractions.Room
{
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
}
