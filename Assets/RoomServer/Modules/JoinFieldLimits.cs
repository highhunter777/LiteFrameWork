namespace RoomServer.Application
{
    /// <summary>
    /// Join 信令字段的 UTF-8 字节上限（P2 单源）：房号 / 票据 / 构建哈希。
    /// </summary>
    public static class JoinFieldLimits
    {
        /// <summary>房间号上限（与客户端/Lobby 的房间号上限同数量级）。</summary>
        public const int RoomIdBytes = 64;

        /// <summary>票据串上限（准入与验签**同一道闸**）。</summary>
        public const int TokenBytes = 256;

        /// <summary>构建哈希上限（16 位 hex，留足扩展余量）。</summary>
        public const int BuildHashBytes = 128;
    }
}
