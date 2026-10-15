namespace LiteNet
{
    /// <summary>
    /// 网络质量观测（应用层探针口径——<see cref="RoomClient"/> 的 RTT/丢包推断值，供 HUD 展示）。
    /// **不是判定输入**：只作表现/诊断，不参与任何游戏逻辑或准入。
    /// </summary>
    public readonly struct NetworkQuality
    {
        /// <summary>无样本（尚未收到回显/尚未发过探针）。</summary>
        public const int NoSample = -1;

        /// <summary>往返时延（毫秒；<see cref="NoSample"/> = 尚无回显样本）。</summary>
        public readonly int RttMs;

        /// <summary>丢包千分比（<see cref="NoSample"/> = 尚无探针样本；0..1000）。</summary>
        public readonly int LossPermille;

        /// <summary>会话连接是否存活（探针仅在其存活时发——离线期不产生新样本）。</summary>
        public readonly bool Connected;

        public NetworkQuality(int rttMs, int lossPermille, bool connected)
        {
            RttMs = rttMs;
            LossPermille = lossPermille;
            Connected = connected;
        }

        /// <summary>无样本形态（未进房/断线）。</summary>
        public static NetworkQuality None => new NetworkQuality(NoSample, NoSample, false);

        /// <summary>是否已有可用样本（RTT 与丢包都有值）。</summary>
        public bool HasSample => RttMs >= 0 && LossPermille >= 0;
    }
}
