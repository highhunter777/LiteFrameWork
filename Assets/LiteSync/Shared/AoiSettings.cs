using System;

namespace LiteSync.Shared
{
    /// <summary>
    /// AOI 广播裁剪参数（《状态同步实施方案》§4.5-7）：**只影响广播，不影响判定与回滚重放**——
    /// 服务器权威 Sim 永远算全量，本组参数只决定"这一帧给谁发哪些实体"。
    ///
    /// **实例化**：参数按房间持有（<c>RoomConfig.Aoi</c>），<see cref="LiteSync.Snapshot.AoiFilter"/>
    /// 每个实例一份网格并只在构造时派生格数——不存在进程级静态缓存，多房间/多实例互不串味。
    /// 构造即校验（越界抛 <see cref="ArgumentOutOfRangeException"/>）：坏配置拒启动，不静默兜底。
    /// </summary>
    public sealed class AoiSettings
    {
        /// <summary>缺省格边长（米）。</summary>
        public const float DefaultCellSize = 10f;

        /// <summary>缺省广播半径（米）；<c>0</c> = 关闭 AOI（全图广播，等价性验收用）。</summary>
        public const float DefaultRadius = 30f;

        /// <summary>缺省栅格覆盖半径（米，2.5D XZ 平面）。</summary>
        public const float DefaultGridExtentMeters = 640f;

        /// <summary>
        /// 缺省参数组（与迁移前的广播旋钮同值）：未按房覆盖 AOI 时的取值。
        /// 只读引用不可变实例，进程内共享安全。
        /// </summary>
        public static readonly AoiSettings Default =
            new AoiSettings(DefaultCellSize, DefaultRadius, DefaultGridExtentMeters);

        /// <summary>格边长（米）：网格按该边长切分，视点格 ± <c>ceil(半径/格边长)</c> 邻域整格可见。</summary>
        public readonly float CellSize;

        /// <summary>广播半径（米）：<c>0</c> = 关闭 AOI（全部活体可发）。</summary>
        public readonly float Radius;

        /// <summary>
        /// 栅格**覆盖半径**（米）：栅格按 <c>±本值</c> 建立（格数 = 派生，不手写）。
        /// 必须 ≥ 地图半宽/半深——越界实体仍按视点距离兜底（不漏发），但会失去网格裁剪的带宽意义。
        /// </summary>
        public readonly float GridExtentMeters;

        /// <summary>
        /// 背压档 2 的收缩半径（= <see cref="Radius"/> 的一半）：由基础半径派生，不另立常量。
        /// <see cref="Radius"/> 为 0 时同为 0（仍保持 AOI 关闭）。
        /// </summary>
        public float ThrottleRadius
        {
            get { return Radius * 0.5f; }
        }

        /// <summary>
        /// 构造并校验：<paramref name="cellSize"/> &gt; 0；<paramref name="radius"/> ≥ 0（0 = 关闭 AOI）；
        /// <paramref name="gridExtentMeters"/> &gt; 0 且 ≥ <paramref name="cellSize"/>。越界即抛。
        /// </summary>
        public AoiSettings(float cellSize, float radius, float gridExtentMeters)
        {
            if (!(cellSize > 0f) || float.IsInfinity(cellSize))
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize,
                    "AOI 格边长必须为有限正值（米）");
            if (!(radius >= 0f) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException(nameof(radius), radius,
                    "AOI 半径必须为有限非负值（0 = 关闭 AOI）");
            if (!(gridExtentMeters > 0f) || float.IsInfinity(gridExtentMeters))
                throw new ArgumentOutOfRangeException(nameof(gridExtentMeters), gridExtentMeters,
                    "AOI 栅格覆盖半径必须为有限正值（米）");
            if (gridExtentMeters < cellSize)
                throw new ArgumentOutOfRangeException(nameof(gridExtentMeters), gridExtentMeters,
                    "AOI 栅格覆盖半径必须 ≥ 格边长");

            CellSize = cellSize;
            Radius = radius;
            GridExtentMeters = gridExtentMeters;
        }
    }
}
