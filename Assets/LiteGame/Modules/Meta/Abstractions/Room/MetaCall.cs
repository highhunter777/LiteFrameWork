using System;

namespace LiteGame
{
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
}
