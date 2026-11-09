namespace LiteFramework
{
    /// <summary>空间预检结果（稳定结果码 + 占比明细，便于定位"是谁撑爆了"。）</summary>
    public readonly struct SpaceCheckResult
    {
        public readonly bool Passed;
        public readonly long RequiredBytes;
        public readonly long AvailableBytes;
        public readonly string Detail;

        private SpaceCheckResult(bool passed, long required, long available, string detail)
        {
            Passed = passed;
            RequiredBytes = required;
            AvailableBytes = available;
            Detail = detail;
        }

        internal static SpaceCheckResult Pass(long required, long available)
            => new SpaceCheckResult(true, required, available, null);

        internal static SpaceCheckResult Fail(long required, long available, string detail)
            => new SpaceCheckResult(false, required, available, detail);

        public override string ToString()
            => Passed ? $"预检通过（需 {RequiredBytes} / 可用 {AvailableBytes}）"
                      : $"预检不足（需 {RequiredBytes} / 可用 {AvailableBytes}）: {Detail}";
    }
}
