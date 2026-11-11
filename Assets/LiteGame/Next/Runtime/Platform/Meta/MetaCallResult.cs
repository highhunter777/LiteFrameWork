using System;

namespace LiteGameNext.Platform.Meta
{
    /// <summary>Meta 调用失败分类：网络不可达 / HTTP 非 2xx / 契约解析失败。</summary>
    public enum MetaFailureKind
    {
        Network,
        Http,
        Protocol,
    }

    /// <summary>Meta 失败事实（不可变）：状态码、稳定错误码、可展示消息。</summary>
    public sealed class MetaError
    {
        /// <summary>构造失败事实。</summary>
        public MetaError(MetaFailureKind kind, long statusCode, string code, string message)
        {
            Kind = kind;
            StatusCode = statusCode;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public MetaFailureKind Kind { get; }

        public long StatusCode { get; }

        public string Code { get; }

        public string Message { get; }

        public override string ToString() => Kind + "(" + StatusCode + "," + Code + "): " + Message;
    }

    /// <summary>Meta 调用结果：成功带值，失败带 <see cref="MetaError"/>；业务失败不抛异常。</summary>
    public sealed class MetaCallResult<T>
    {
        private MetaCallResult(T value, MetaError error)
        {
            Value = value;
            Error = error;
        }

        public bool Success => Error == null;

        public T Value { get; }

        public MetaError Error { get; }

        public static MetaCallResult<T> Ok(T value) => new MetaCallResult<T>(value, null);

        public static MetaCallResult<T> Failed(MetaError error)
            => new MetaCallResult<T>(default, error ?? throw new ArgumentNullException(nameof(error)));
    }
}
