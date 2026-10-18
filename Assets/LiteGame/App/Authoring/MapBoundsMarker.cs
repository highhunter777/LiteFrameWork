using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 地图场景作者标注（烘焙器 `SimMapBaker` 的单源读取面；运行时零消费者）：判定边界与地面高度。
    /// 地图场景约定拓扑：`MapCollision` 代理树 ＋ `spawn_*` 出生标记 ＋ 本件（挂场景对象 `map_bounds`）。
    /// 值随地图场景入库——烘焙产物 tbmap 的 ground_y/half_width/half_depth 由此而来。
    /// </summary>
    public sealed class MapBoundsMarker : MonoBehaviour
    {
        public float GroundY;

        public float HalfWidth = 100f;

        public float HalfDepth = 100f;
    }
}
