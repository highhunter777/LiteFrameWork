namespace RoomServer.Application
{
    /// <summary>
    /// Join 信令字段的 UTF-8 字节上限（P2 单源）：房号 / 票据 / 构建哈希。
    ///
    /// **为什么要有它**：这三个数字此前分别钉在宿主侧 <c>JoinAdmissionGate</c> 与共享侧
    /// <see cref="HmacJoinTicketValidator"/> 两处，票据上限更是配成 2048 而准入门先拦 256
    /// （第二道闸永远够不着，且"上限到底是多少"有两个答案）。上限归本类后：
    /// - 准入门（<c>JoinAdmissionGate</c>）与验签器（<see cref="HmacJoinTicketValidator"/>）**引用同值**；
    /// - 宿主的公开常量 <c>ServerHost.Max*Bytes</c> 保持为别名（既有调用方/测试零改动）。
    ///
    /// 取值口径（P0-3"解析前限制长度"）：按 UTF-8 **字节数**，越小越安全——
    /// 票据是 128 bit nonce + 绑定字段的 base64 形态，256 字节对合法票据有两倍余量。
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
