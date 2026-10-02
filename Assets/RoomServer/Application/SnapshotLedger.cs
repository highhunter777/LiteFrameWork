namespace RoomServer.Application
{
    /// <summary>
    /// 快照发送 ledger（《商业级通用服务端框架总设计》§5 P0-4"可信 ACK 与背压"）：
    /// 有界环，每条 = (帧号, 该帧发送完成后的**累计**字节数)。
    ///
    /// 释放语义：ACK 帧 F 命中 ledger → <c>AckedBytes = 条目.累计值</c>（一次到位，天然单调——
    /// 条目累计值随帧号单调增）。累计口径让"环内旧条目被覆盖"无损：客户端只有两条路径——
    /// ① ack 落后太多（条目被覆盖）：按陈旧忽略，水位保持保守（该客户端已被 NeedsFull/背压降级照顾）；
    /// ② ack 正常：必然命中仍在环内的近期条目。
    ///
    /// 容量推导：30Hz 快照下 128 条 ≈ 4.3s 窗口；NeedsFull 的 ack 落后阈值为 30 帧（1s），
    /// 覆盖窗口 ≥ 4 倍判据窗口，正常链路永不越界。
    /// </summary>
    public sealed class SnapshotLedger
    {
        /// <summary>环容量（30Hz 下 ≈ 4.3 秒；推导见类注释）。</summary>
        public const int Capacity = 128;

        private readonly int[] _frames = new int[Capacity];
        private readonly long[] _cumulativeAfter = new long[Capacity];
        private int _next;      // 环形写入游标

        /// <summary>记录一次快照发送（覆盖最老条目——累计口径下覆盖无损，见类注释）。</summary>
        public void Record(int frame, long cumulativeAfter)
        {
            _frames[_next] = frame;
            _cumulativeAfter[_next] = cumulativeAfter;
            _next = (_next + 1) % Capacity;
        }

        /// <summary>按帧号查累计值（ACK 释放的唯一依据）；不在环内（从未发送/已被覆盖）返回 false。</summary>
        public bool TryGet(int frame, out long cumulativeAfter)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_frames[i] != frame) continue;
                cumulativeAfter = _cumulativeAfter[i];
                return true;
            }

            cumulativeAfter = 0;
            return false;
        }

        /// <summary>清空环（重绑换连接：新连接没有历史发送记录，等价于全新 ledger）。</summary>
        public void Clear()
        {
            System.Array.Clear(_frames, 0, Capacity);
            System.Array.Clear(_cumulativeAfter, 0, Capacity);
            _next = 0;
        }
    }
}
