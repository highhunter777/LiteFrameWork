using System;
using LiteNet.Protocol;
using LiteSim;

namespace LiteNet
{
    /// <summary>
    /// 输入冗余窗口（RoomClient 的行内件，M2 拆分）：记录本帧输入并生成"本帧 + 往回连续历史帧"
    /// 的打包窗口——只向前、只取连续段；跳帧绝不用陈旧帧冒充缺失帧。
    ///
    /// 窗口下界锚在"最近快照帧 + 1"（服务器已广播确认的帧之后都可能还没被消费），上界双约束：
    /// 环内连续可用帧数与 <see cref="InputPacker.MaxRedundancy"/>。
    /// </summary>
    internal sealed class InputWindow
    {
        private readonly SimInputFrame[] _ring = new SimInputFrame[InputPacker.MaxRedundancy];
        private readonly SimInputFrame[] _window = new SimInputFrame[InputPacker.MaxRedundancy];
        private int _recentFrame = -1;      // 最近记录的逻辑帧（-1 = 尚未记录）
        private int _recentCount;           // 从 _recentFrame 往回**连续**可用的帧数（跳帧即重置为 1）

        /// <summary>最近一包实际携带的冗余帧数（未确认段长度，诊断/测试用）。</summary>
        public int LastWindowCount { get; private set; }

        /// <summary>清空窗口（新一次 Join）。</summary>
        public void Reset()
        {
            _recentFrame = -1;
            _recentCount = 0;
            LastWindowCount = 0;
        }

        /// <summary>
        /// 记录本帧输入并维护冗余窗口（**只向前**、**只取连续段**）：
        /// - 重发同帧（frame == _recentFrame）：只覆盖内容，不推进窗口；
        /// - 更早的帧：忽略（窗口语义是"最近若干帧"，回退无意义）；
        /// - 跳帧（frame &gt; _recentFrame + 1）：连续性断裂 → 窗口重置为 1。
        /// </summary>
        public void Record(int frame, in SimInputFrame input)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame), frame, "逻辑帧号不能为负");

            if (_recentFrame >= 0 && frame <= _recentFrame)
            {
                if (frame == _recentFrame) _ring[RingIndex(frame)] = input;   // 重发：覆盖即可
                return;
            }

            if (_recentFrame < 0) _recentCount = 1;
            else if (frame == _recentFrame + 1) _recentCount = Math.Min(InputPacker.MaxRedundancy, _recentCount + 1);
            else _recentCount = 1;                                            // 跳帧 → 断连续

            _ring[RingIndex(frame)] = input;
            _recentFrame = frame;
        }

        /// <summary>
        /// 生成本帧的打包窗口（连续段，长度受未确认段与窗口容量双约束）。
        /// 返回的 span 直接交给 <see cref="InputPacker.Pack"/>（零拷贝，调用后不可再 Record）。
        /// </summary>
        public ReadOnlySpan<SimInputFrame> Build(int frame, int lastSnapshotFrame)
        {
            // 未确认段帧数（frame .. 最近快照帧+1）；无快照（-1）时退化为"最近连续可用段"。
            int span = frame - lastSnapshotFrame;
            int count = span < _recentCount ? span : _recentCount;
            if (count < 1) count = 1;
            if (count > InputPacker.MaxRedundancy) count = InputPacker.MaxRedundancy;
            LastWindowCount = count;

            for (int i = 0; i < count; i++)
                _window[i] = _ring[RingIndex(frame - i)];

            return new ReadOnlySpan<SimInputFrame>(_window, 0, count);
        }

        private static int RingIndex(int frame) => ((frame % InputPacker.MaxRedundancy) + InputPacker.MaxRedundancy)
                                                   % InputPacker.MaxRedundancy;
    }
}
