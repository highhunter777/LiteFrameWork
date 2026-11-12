using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>单个实体的快照位姿（帧号 + 位置 + 朝向 + 生命代次）。</summary>
    public readonly struct PoseSample
    {
        public PoseSample(int frame, Vector3 position, float yaw, int lifeSerial)
        {
            Frame = frame;
            Position = position;
            Yaw = yaw;
            LifeSerial = lifeSerial;
        }

        public int Frame { get; }
        public Vector3 Position { get; }
        public float Yaw { get; }
        public int LifeSerial { get; }
    }

    /// <summary>
    /// 远端插值缓冲：快照按帧号有序入缓冲，渲染帧按 tick 匀速推进并落后最新快照 RenderDelayFrames；
    /// 快照停摆时停在最新（不外推）；换代/跳变直接取后帧。
    /// </summary>
    public sealed class SnapshotInterpolationBuffer
    {
        private const int Capacity = 8;

        private readonly Dictionary<long, List<PoseSample>> _byEntity = new Dictionary<long, List<PoseSample>>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private float _renderFrame;
        private bool _hasRenderFrame;

        /// <summary>渲染相对最新快照的落后帧数（0 = 即时模式，测试用）。</summary>
        public int RenderDelayFrames { get; set; } = 3;

        /// <summary>tick 频率（Hz），用于渲染帧推进。</summary>
        public float TickRate { get; set; } = 60f;

        /// <summary>当前渲染帧（-1 = 尚无快照）。</summary>
        public int RenderFrame => _hasRenderFrame ? Mathf.FloorToInt(_renderFrame) : -1;

        /// <summary>已缓冲的实体数。</summary>
        public int EntityCount => _byEntity.Count;

        /// <summary>入一份实体位姿；帧号不递增的样本被丢弃（乱序保护）。</summary>
        public void Push(long entityId, in PoseSample sample)
        {
            if (!_byEntity.TryGetValue(entityId, out List<PoseSample> list))
            {
                list = new List<PoseSample>(Capacity);
                _byEntity.Add(entityId, list);
            }
            if (list.Count > 0 && sample.Frame <= list[list.Count - 1].Frame) return;
            list.Add(sample);
            while (list.Count > Capacity) list.RemoveAt(0);

            if (!_hasRenderFrame)
            {
                _renderFrame = sample.Frame;
                _hasRenderFrame = true;
            }
        }

        /// <summary>推进渲染帧：按 dt×TickRate 前进，封顶「最新帧 − 延迟」，快照停摆即停。</summary>
        public void Advance(float deltaTime)
        {
            if (!_hasRenderFrame || _byEntity.Count == 0) return;
            int newest = NewestFrame();
            if (newest < 0) return;

            float cap = newest - RenderDelayFrames;
            float next = _renderFrame + deltaTime * TickRate;
            _renderFrame = next > cap ? Mathf.Max(_renderFrame, cap) : next;
        }

        /// <summary>取实体在渲染帧的插值位姿；无数据返回 false。</summary>
        public bool TrySample(long entityId, out Vector3 position, out float yaw)
        {
            position = Vector3.zero;
            yaw = 0f;
            if (!_byEntity.TryGetValue(entityId, out List<PoseSample> list) || list.Count == 0) return false;

            PoseSample from = list[0];
            PoseSample to = list[0];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Frame > _renderFrame) break;
                from = list[i];
                to = list[i];
            }
            int nextIndex = IndexOf(from.Frame, list) + 1;
            if (nextIndex > 0 && nextIndex < list.Count) to = list[nextIndex];

            // 换代（复活/重入）或跨度异常：直接取后帧，不插值。
            if (from.LifeSerial != to.LifeSerial || to.Frame <= from.Frame)
            {
                position = to.Position;
                yaw = to.Yaw;
                return true;
            }

            float alpha = Mathf.Clamp01((_renderFrame - from.Frame) / (to.Frame - from.Frame));
            position = Vector3.Lerp(from.Position, to.Position, alpha);
            yaw = Mathf.LerpAngle(from.Yaw, to.Yaw, alpha);
            return true;
        }

        /// <summary>实体是否在缓冲中。</summary>
        public bool Has(long entityId) => _byEntity.ContainsKey(entityId);

        /// <summary>清空（换局/重连）。</summary>
        public void Reset()
        {
            _byEntity.Clear();
            _seen.Clear();
            _renderFrame = 0f;
            _hasRenderFrame = false;
        }

        private int NewestFrame()
        {
            int newest = -1;
            foreach (List<PoseSample> list in _byEntity.Values)
                if (list.Count > 0 && list[list.Count - 1].Frame > newest)
                    newest = list[list.Count - 1].Frame;
            return newest;
        }

        private static int IndexOf(int frame, List<PoseSample> list)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Frame == frame) return i;
            return -1;
        }
    }
}
