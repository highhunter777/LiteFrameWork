namespace MetaServer.Contracts.Validation
{
    /// <summary>
    /// 字段 UTF-8 字节上限**统一上限表**（P2 单源）：此前 128/64/256 这些数字钉在各模块的私有常量里， 同一类字段在不同模块可能取到不同值，且"这个字段为什么是 64"无处可查。
    /// </summary>
    public static class FieldLimits
    {
        /// <summary>短标识：requestId / deviceId / accountId（1..128 UTF-8 字节）。</summary>
        public const int ShortIdBytes = MetaWire.MetaWireLimits.ShortIdBytes;

        /// <summary>业务键：matchId / playerId / instanceId / roomId / buildHash（1..64）。</summary>
        public const int BusinessKeyBytes = MetaWire.MetaWireLimits.BusinessKeyBytes;

        /// <summary>可达地址 <c>host:port</c>（1..128）。</summary>
        public const int AddressBytes = MetaWire.MetaWireLimits.AddressBytes;

        /// <summary>幂等操作标识 operationId（1..256——允许带前缀的复合键）。</summary>
        public const int OperationIdBytes = MetaWire.MetaWireLimits.OperationIdBytes;
    }
}
