#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using Unity.Profiling;

namespace LiteGame
{
    /// <summary>帧间隔统计快照（环窗内均值 FPS/最差帧/p50/p95/p99/长帧计数）。</summary>
    public readonly struct PerfSnapshot
    {
        public readonly int Fps;
        public readonly float WorstMs;
        public readonly float P50Ms;
        public readonly float P95Ms;
        public readonly float P99Ms;
        public readonly int LongFrames;
        public readonly int SampleCount;

        public PerfSnapshot(int fps, float worstMs, float p50Ms, float p95Ms, float p99Ms, int longFrames, int sampleCount)
        {
            Fps = fps;
            WorstMs = worstMs;
            P50Ms = p50Ms;
            P95Ms = p95Ms;
            P99Ms = p99Ms;
            LongFrames = longFrames;
            SampleCount = sampleCount;
        }
    }

    /// <summary>性能段完整读模型：帧间隔统计 + 引擎 recorder 读数（GC/绘制）+ HUD 自聚合耗时。</summary>
    public readonly struct PerfReading
    {
        /// <summary>环窗内已有帧样本（预热期/未开帧时为 false，整段不显示）。</summary>
        public readonly bool HasFrames;
        public readonly PerfSnapshot Frames;
        public readonly bool GcAvailable;
        public readonly long GcBytes;
        public readonly bool DrawCallsAvailable;
        public readonly int DrawCalls;
        public readonly bool BatchesAvailable;
        public readonly int Batches;
        public readonly float HudBuildMs;

        public PerfReading(bool hasFrames, PerfSnapshot frames,
            bool gcAvailable, long gcBytes, bool drawCallsAvailable, int drawCalls,
            bool batchesAvailable, int batches, float hudBuildMs)
        {
            HasFrames = hasFrames;
            Frames = frames;
            GcAvailable = gcAvailable;
            GcBytes = gcBytes;
            DrawCallsAvailable = drawCallsAvailable;
            DrawCalls = drawCalls;
            BatchesAvailable = batchesAvailable;
            Batches = batches;
            HudBuildMs = hudBuildMs;
        }
    }

    /// <summary>
    /// 帧样本环（240 帧 ≈ 4s@60fps）＋ p50/p95/p99 ＋ GC/绘制调用 ProfilerRecorder。
    /// 性能读模型住内核（《GM 与调试工具分层专项设计》§7.2 读模型"性能样本"行）：
    /// 投影只喂 <see cref="SampleFrame"/> 的时钟差并读 <see cref="Read"/>，不另拼读数。
    /// ProfilerRecorder 随内核构造启动、进程级存活（本件只在开发三宏内编译；三个查询句柄，
    /// 开销可忽略，不做停启状态机）。marker 缺失时 recorder invalid（读数显示 n/a），不阻断。
    /// </summary>
    public sealed class DebugPerfSampler
    {
        private const float LongFrameSeconds = 0.1f;
        private const int Capacity = 240;

        private readonly float[] _frames = new float[Capacity];
        private readonly float[] _sort = new float[Capacity];
        private readonly ProfilerRecorder _gcRecorder;
        private readonly ProfilerRecorder _drawRecorder;
        private readonly ProfilerRecorder _batchRecorder;
        private int _head;
        private int _count;
        private int _warmupFrames = 30;   // 跳过启动/场景加载的超长帧，否则 p50 会被首帧 9s 支配
        private float _hudBuildMs;        // ≤0 = 尚未有回填

        public DebugPerfSampler()
        {
            try { _gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"); }
            catch (Exception) { _gcRecorder = default; }
            try { _drawRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"); }
            catch (Exception) { _drawRecorder = default; }
            try { _batchRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count"); }
            catch (Exception) { _batchRecorder = default; }
        }

        public void SampleFrame(float dt)
        {
            if (_warmupFrames > 0)
            {
                _warmupFrames--;
                return;
            }
            _frames[_head] = dt;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        public void RecordHudBuildMs(float ms)
        {
            _hudBuildMs = _hudBuildMs <= 0f ? ms : _hudBuildMs + (ms - _hudBuildMs) * 0.2f;
        }

        /// <summary>刷新 recorder 读数并返回整段读模型（统计部为纯逻辑，可脱离 Unity 运行时单测）。</summary>
        public PerfReading Read()
        {
            bool hasFrames = TryGet(out PerfSnapshot snapshot);
            return new PerfReading(hasFrames, snapshot,
                _gcRecorder.Valid, _gcRecorder.Valid ? Math.Max(0, _gcRecorder.LastValue) : 0,
                _drawRecorder.Valid, _drawRecorder.Valid ? (int)Math.Max(0, _drawRecorder.LastValue) : 0,
                _batchRecorder.Valid, _batchRecorder.Valid ? (int)Math.Max(0, _batchRecorder.LastValue) : 0,
                _hudBuildMs);
        }

        public bool TryGet(out PerfSnapshot snapshot)
        {
            if (_count == 0)
            {
                snapshot = default;
                return false;
            }

            float sum = 0f, worst = 0f;
            int longFrames = 0;
            for (int i = 0; i < _count; i++)
            {
                float dt = _frames[i];
                sum += dt;
                if (dt > worst) worst = dt;
                if (dt > LongFrameSeconds) longFrames++;
                _sort[i] = dt;
            }
            Array.Sort(_sort, 0, _count);

            float p50 = Percentile(0.50f);
            float p95 = Percentile(0.95f);
            float p99 = Percentile(0.99f);
            int fps = sum > 0.0001f ? (int)MathF.Round(_count / sum) : 0;
            snapshot = new PerfSnapshot(fps, worst * 1000f, p50 * 1000f, p95 * 1000f, p99 * 1000f, longFrames, _count);
            return true;
        }

        private float Percentile(float q)
        {
            int index = Math.Clamp((int)MathF.Ceiling(q * (_count - 1)), 0, _count - 1);
            return _sort[index];
        }
    }
}
#endif
