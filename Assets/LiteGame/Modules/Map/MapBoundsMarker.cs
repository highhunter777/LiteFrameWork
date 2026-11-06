using UnityEngine;

namespace LiteGame.Modules.Map
{
    /// <summary>
    /// 地图场景作者标注（烘焙器 `SimMapBaker` 的单源读取面）。
    /// </summary>
    public sealed class MapBoundsMarker : MonoBehaviour
    {
        public float GroundY;

        /// <summary>边界半宽（**必填**）：缺省 0 会被烘焙器按"半宽/半深须为正"拒载——防新图漏赋值时
        /// 静默落到与场地无关的默认边界（旧默认 ±100 让边界钳制成为死代码、摘要失真，见审计登记）。</summary>
        public float HalfWidth;

        /// <summary>边界半深（**必填**）：同 <see cref="HalfWidth"/>。</summary>
        public float HalfDepth;
    }

    /// <summary>
    /// 地图场景拓扑约定名（全图共用，场景制作方与消费方同守）：运行时/Editor 共享单源， Editor 烘焙侧（BakeTargets）别名引用本处——禁两侧各写一份字面量（常量双写即 审计红线「靠注释人肉同步」）。
    /// </summary>
    public static class MapTopology
    {
        /// <summary>判定代理树根对象名（地图场景根下唯一）：烘焙采集（SimMapBaker）读取。</summary>
        public const string MapCollisionRoot = "MapCollision";
    }
}
