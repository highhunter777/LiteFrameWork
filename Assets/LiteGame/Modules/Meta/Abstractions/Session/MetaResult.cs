namespace LiteGame
{
    /// <summary>Meta 调用结果：失败给稳定 reason 文本（消费方据此选文案键，不匹配原文）。</summary>
    public readonly struct MetaResult<T>
    {
        public readonly bool Ok;
        public readonly string Reason;
        public readonly T Value;

        private MetaResult(bool ok, T value, string reason)
        {
            Ok = ok;
            Value = value;
            Reason = reason;
        }

        public static MetaResult<T> Success(T value) => new MetaResult<T>(true, value, null);
        public static MetaResult<T> Fail(string reason) => new MetaResult<T>(false, default, reason ?? "Internal");
    }
}
