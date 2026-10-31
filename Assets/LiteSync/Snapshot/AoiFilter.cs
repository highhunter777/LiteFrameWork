using System;
using System.Collections.Generic;
using LiteSim;
using LiteSync.Shared;

using LiteSync.Snapshot;
namespace LiteSync.Snapshot
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
    /// 栅格范围由构造注入的 <see cref="AoiSettings.GridExtentMeters"/> 与 <see cref="AoiSettings.CellSize"/>
    /// 派生（实例只读字段，构造时算一次）。网格外实体会按**视点距离**兜底判定可见，
    /// 并暴露 <see cref="OutsideCount"/> 供 Ops/测试观测（&gt;0 = 栅格未覆盖地图，应调大
    /// <see cref="AoiSettings.GridExtentMeters"/>）。
    /// </summary>
    public sealed class AoiFilter
    {
        private readonly List<int>[] _buckets;
        private readonly List<int> _outside = new List<int>();   // 网格外：按视点距离兜底（见类注释）

        /// <summary>格边长（米）——实例持有，来自构造注入的 <see cref="AoiSettings"/>。</summary>
        private readonly float _cell;

        /// <summary>每轴格数——由 extent/cell 派生，不手写。</summary>
        private readonly int _gridDim;

        /// <summary>原点格号（含负数侧）。</summary>
        private readonly int _gridOrigin;

        private SimWorldState _cachedState;
        private int _cachedFrame = int.MinValue;

        /// <summary>
        /// 最近一次建格时落在**网格外**的活体数（诊断 / Ops 汇总）。
        /// &gt;0 表示地图超出栅格覆盖范围：已按距离兜底**不漏发**，但应把
        /// <see cref="AoiSettings.GridExtentMeters"/> 调到 ≥ 地图半宽/半深——
        /// 否则每帧多一圈距离判定，且失去网格裁剪的带宽意义。
        /// </summary>
        public int OutsideCount { get; private set; }

        /// <summary>
        /// 用房间级 AOI 参数构造（null 抛——参数只能显式给，不给进程级兜底）。
        /// 格数/原点在此算一次，此后只读。
        /// </summary>
        public AoiFilter(AoiSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _cell = settings.CellSize;
            // 半宽格数：覆盖 ±GridExtentMeters（向上取整，宁可多覆盖）；格数 = 派生，不手写
            int halfCells = Math.Max(1, (int)Math.Ceiling(settings.GridExtentMeters / _cell));
            _gridDim = halfCells * 2 + 1;
            _gridOrigin = -halfCells;

            _buckets = new List<int>[_gridDim * _gridDim];
            for (int i = 0; i < _buckets.Length; i++) _buckets[i] = new List<int>(4);
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
                int gx = CellOf(pos.X, _cell) - _gridOrigin;
                int gz = CellOf(pos.Z, _cell) - _gridOrigin;
                if (gx < 0 || gx >= _gridDim || gz < 0 || gz >= _gridDim) _outside.Add(i);   // 网格外 → 距离兜底
                else _buckets[gz * _gridDim + gx].Add(i);
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

            int cx = CellOf(viewerPos.X, _cell);
            int cz = CellOf(viewerPos.Z, _cell);
            int span = (int)Math.Ceiling(radius / _cell);

            for (int gz = cz - span; gz <= cz + span; gz++)
            {
                if (gz < _gridOrigin || gz >= _gridOrigin + _gridDim) continue;
                for (int gx = cx - span; gx <= cx + span; gx++)
                {
                    if (gx < _gridOrigin || gx >= _gridOrigin + _gridDim) continue;
                    List<int> bucket = _buckets[(gz - _gridOrigin) * _gridDim + (gx - _gridOrigin)];
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
