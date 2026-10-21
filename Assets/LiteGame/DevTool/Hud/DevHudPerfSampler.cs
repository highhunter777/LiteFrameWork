#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using Unity.Profiling;
using UnityEngine;

namespace LiteGame
{
    internal readonly struct PerfSnapshot
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

    /// <summary>
    /// 帧样本环（240 帧 ≈ 4s@60fps）＋ p50/p95/p99 ＋ GC/绘制调用 ProfilerRecorder。
    /// ProfilerRecorder 在 marker 缺失时返回 invalid（读数显示 n/a），不阻断 HUD。
    /// </summary>
    internal sealed class DevHudPerfSampler : IDisposable
    {
        private const float LongFrameSeconds = 0.1f;
        private const int Capacity = 240;

        private readonly float[] _frames = new float[Capacity];
        private readonly float[] _sort = new float[Capacity];
        private int _head;
        private int _count;
        private int _warmupFrames = 30;   // 跳过启动/场景加载的超长帧，否则 p50 会被首帧 9s 支配
        private ProfilerRecorder _gcRecorder;
        private ProfilerRecorder _drawRecorder;
        private ProfilerRecorder _batchRecorder;

        public DevHudPerfSampler()
        {
            try { _gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"); }
            catch (Exception) { _gcRecorder = default; }
            try { _drawRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"); }
            catch (Exception) { _drawRecorder = default; }
            try { _batchRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count"); }
            catch (Exception) { _batchRecorder = default; }
        }

        public bool GcAvailable => _gcRecorder.Valid;

        public bool DrawCallsAvailable => _drawRecorder.Valid;

        public bool BatchesAvailable => _batchRecorder.Valid;

        public long GcBytes { get; private set; }

        public int DrawCalls { get; private set; }

        public int Batches { get; private set; }

        /// <summary>HUD 自身一轮聚合耗时（ms），由 DevHUD 回填。</summary>
        public float HudBuildMs { get; set; }

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

        public void UpdateRecorders()
        {
            if (_gcRecorder.Valid) GcBytes = Math.Max(0, _gcRecorder.LastValue);
            if (_drawRecorder.Valid) DrawCalls = (int)Math.Max(0, _drawRecorder.LastValue);
            if (_batchRecorder.Valid) Batches = (int)Math.Max(0, _batchRecorder.LastValue);
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
            int fps = sum > 0.0001f ? Mathf.RoundToInt(_count / sum) : 0;
            snapshot = new PerfSnapshot(fps, worst * 1000f, p50 * 1000f, p95 * 1000f, p99 * 1000f, longFrames, _count);
            return true;
        }

        private float Percentile(float q)
        {
            int index = Mathf.Clamp(Mathf.CeilToInt(q * (_count - 1)), 0, _count - 1);
            return _sort[index];
        }

        public void Dispose()
        {
            if (_gcRecorder.Valid) _gcRecorder.Dispose();
            if (_drawRecorder.Valid) _drawRecorder.Dispose();
            if (_batchRecorder.Valid) _batchRecorder.Dispose();
        }
    }
}
#endif
