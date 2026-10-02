using System;
using System.Collections.Generic;
using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// AOI 网格过滤（《M10实施指导》决策 13；《状态同步实施方案》§4.5-7 / §3.8）：
    /// **只影响广播裁剪，不影响判定与回滚重放**——服务器权威 Sim 永远算全量，AOI 只决定"这一帧给谁发哪些实体"。
    ///
    /// 实体按**所在格**归属某视点：视点格 ± <c>ceil(半径/格边长)</c> 邻域内的格全部可见（整格可见口径，非精确圆：
    /// 格边界可能多发半格，但**永不漏发**——广播少发一个实体 = 客户端静默分叉，是唯一不可接受的错）。
    /// <c>radius</c> ≤ 0 = 关闭 AOI（不过滤，全部活体可发——等价性验收的对照组）。
    ///
    /// 网格归**实例**所有（每个 <see cref="SnapshotDiffer"/>/房间一份），按 **(世界实例, 帧号)** 键失效；
    /// 栅格范围由 <see cref="SimConfig.AoiGridExtentMeters"/> 与 <see cref="SimConfig.AoiCellSize"/> 派生。
    /// 网格外实体会按**视点距离**兜底判定可见，并暴露 <see cref="OutsideCount"/> 供 Ops/测试观测
    /// （&gt;0 = 栅格未覆盖地图，应调大 <see cref="SimConfig.AoiGridExtentMeters"/>）。
    /// </summary>
    public sealed class AoiFilter
    {
        /// <summary>格边长（米）——单一来源 <see cref="SimConfig.AoiCellSize"/>。</summary>
        private static readonly float Cell = SimConfig.AoiCellSize;

        /// <summary>半宽格数：覆盖 <c>±SimConfig.AoiGridExtentMeters</c>（向上取整，宁可多覆盖）。</summary>
        private static readonly int HalfCells = Math.Max(1, (int)Math.Ceiling(SimConfig.AoiGridExtentMeters / Cell));

        /// <summary>每轴格数——由 extent/cell 派生，不手写。</summary>
        private static readonly int GridDim = HalfCells * 2 + 1;

        /// <summary>原点格号（含负数侧）。</summary>
        private static readonly int GridOrigin = -HalfCells;

        private readonly List<int>[] _buckets = CreateBuckets();
        private readonly List<int> _outside = new List<int>();   // 网格外：按视点距离兜底（见类注释）
        private SimWorldState _cachedState;
        private int _cachedFrame = int.MinValue;

        /// <summary>
        /// 最近一次建格时落在**网格外**的活体数（诊断 / Ops 汇总）。
        /// &gt;0 表示地图超出栅格覆盖范围：已按距离兜底**不漏发**，但应把
        /// <see cref="SimConfig.AoiGridExtentMeters"/> 调到 ≥ 地图半宽/半深——
        /// 否则每帧多一圈距离判定，且失去网格裁剪的带宽意义。
        /// </summary>
        public int OutsideCount { get; private set; }

        private static List<int>[] CreateBuckets()
        {
            var buckets = new List<int>[GridDim * GridDim];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<int>(4);
            return buckets;
        }

        /// <summary>帧级网格缓存（同帧重复调用零成本；换世界或换帧自动重建）。</summary>
        public void EnsureGrid(in SimWorldState s)
        {
            if (ReferenceEquals(_cachedState, s) && _cachedFrame == s.Frame) return;
            _cachedState = s;
            _cachedFrame = s.Frame;

            for (int i = 0; i < _buckets.Length; i++) _buckets[i].Clear();
            _outside.Clear();

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;

                SimVector3 pos = s.Entities[i].Pos;
                int gx = CellOf(pos.X, Cell) - GridOrigin;
                int gz = CellOf(pos.Z, Cell) - GridOrigin;
                if (gx < 0 || gx >= GridDim || gz < 0 || gz >= GridDim) _outside.Add(i);   // 网格外 → 距离兜底
                else _buckets[gz * GridDim + gx].Add(i);
            }

            OutsideCount = _outside.Count;
        }

        /// <summary>
        /// 收集视点可见的活体槽位（写入 <paramref name="result"/>，调用方负责 Clear）。
        /// <paramref name="radius"/> ≤ 0 = 关闭 AOI（全部活体）。集合按槽位升序（确定性广播序）。
        /// </summary>
        public void CollectVisible(in SimWorldState s, SimVector3 viewerPos, float radius, List<int> result)
        {
            if (radius <= 0f)
            {
                for (int i = 0; i < SimConfig.MaxEntities; i++)
                    if (s.IsAlive(i)) result.Add(i);
                return;
            }

            EnsureGrid(in s);

            float cell = SimConfig.AoiCellSize;
            int cx = CellOf(viewerPos.X, cell);
            int cz = CellOf(viewerPos.Z, cell);
            int span = (int)Math.Ceiling(radius / cell);

            for (int gz = cz - span; gz <= cz + span; gz++)
            {
                if (gz < GridOrigin || gz >= GridOrigin + GridDim) continue;
                for (int gx = cx - span; gx <= cx + span; gx++)
                {
                    if (gx < GridOrigin || gx >= GridOrigin + GridDim) continue;
                    List<int> bucket = _buckets[(gz - GridOrigin) * GridDim + (gx - GridOrigin)];
                    for (int k = 0; k < bucket.Count; k++) result.Add(bucket[k]);
                }
            }

            // 网格外兜底：按视点**实际距离**判定——宁可多发，不可漏发
            float r2 = radius * radius;
            for (int k = 0; k < _outside.Count; k++)
            {
                int slot = _outside[k];
                if (!s.IsAlive(slot)) continue;                       // 同帧理论上仍是活体（防御）
                SimVector3 p = s.Entities[slot].Pos;
                float dx = p.X - viewerPos.X;
                float dz = p.Z - viewerPos.Z;
                if (dx * dx + dz * dz <= r2) result.Add(slot);
            }

            result.Sort();   // 槽位升序（网格遍历序不是槽位序；广播序必须确定）
        }

        /// <summary>视点所在格号（Python 式 floor 取整——负数侧连续，格边界不产生空洞）。</summary>
        public static int CellOf(float v, float cell) => (int)Math.Floor(v / cell);
    }
}
