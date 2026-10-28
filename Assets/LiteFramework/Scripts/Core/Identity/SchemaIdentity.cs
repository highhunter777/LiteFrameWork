namespace LiteFramework
{
    /// <summary>
    /// 结构身份（SchemaIdentity）：协议/Sim/配置/存档 schema 版本。
    /// 0 = 未声明（当前发布描述未声明该轴；CompatibilityMode.Off 下不参与数值比较）。
    /// </summary>
    public readonly struct SchemaIdentity
    {
        public int ProtocolVersion { get; }
        public int SimVersion { get; }
        public int ConfigSchemaVersion { get; }
        public int SaveSchemaVersion { get; }

        public SchemaIdentity(int protocolVersion, int simVersion, int configSchemaVersion, int saveSchemaVersion)
        {
            ProtocolVersion = protocolVersion;
            SimVersion = simVersion;
            ConfigSchemaVersion = configSchemaVersion;
            SaveSchemaVersion = saveSchemaVersion;
        }
    }
}
