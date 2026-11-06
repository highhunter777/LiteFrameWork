namespace LiteFramework
{
    /// <summary>
    /// 运行时可接受的能力下限（版本元组里"当前 Player 实际具备"的一侧）。
    /// 由装配点从编译期常量/生成物填充——**不接受运行时可变来源**（否则兼容判定可被内容影响）。
    /// </summary>
    public sealed class PlayerCapabilities
    {
        /// <summary>
        /// 数值能力位的兼容判定模式。
        /// </summary>
        public enum CapabilityMode
        {
            /// <summary>开启：逐轴比较（各轴必须是真实能力值）。</summary>
            Enforce = 0,

            /// <summary>关闭：数值能力位不做下限校验（能力生成物未接入时的显式形态）。</summary>
            Off = 1,
        }

        public CapabilityMode CompatibilityMode = CapabilityMode.Enforce;

        public string AppVersion = "";
        public string Platform = "";
        public string Channel = "";
        public int BridgeApiVersion;
        public int ProtocolVersion;
        public int SimVersion;
        public int ConfigSchemaVersion;
        public int SaveSchemaVersion;
    }

}
