using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 一个活跃特效实例（《VFX服务实施指导》§2.2 第 1 件）：句柄 + 定义 + 挂点 + 到期时刻。
    /// **到期时刻走世界时钟**——时停即冻结（表现层与判定的时钟分轨，见《动效设计方案》§0）。
    /// </summary>
    public sealed class VfxInstance
    {
        public int Id;
        public VfxDef Def;

        /// <summary>挂点（null = 世界原点容器）。</summary>
        public Transform Attach;

        /// <summary>true = 跟随挂点（SetParent 挂点）；false = 落世界容器（不随实体移动）。</summary>
        public bool Follow;

        /// <summary>显式世界落点（<c>PlayAt</c> 用；<see cref="HasPosition"/> = false 时忽略——
        /// 旧 <c>Play(follow:false)</c> 形态不带位，保持池内原位）。</summary>
        public UnityEngine.Vector3 Position;

        /// <summary>是否带显式世界落点（落位时写一次；非跟随特效的"打哪留哪"）。</summary>
        public bool HasPosition;

        public float Scale;

        /// <summary>实例对象；**null = 加载在途**（pending）。</summary>
        public GameObject Go;

        /// <summary>到期时刻（世界时钟秒；pending 时 = +∞）。</summary>
        public float ExpireAt;

        /// <summary>宿主已回收：异步加载的续体据此**丢弃而不实例化**（闭合"加载在途 × 宿主回收"竞态）。</summary>
        public bool Cancelled;
    }
}
