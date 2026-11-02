using System;

namespace LiteSim
{
    /// <summary>
    /// 字段的**同步层次**（《状态同步专项设计》§5.2 快照分层）——公开/私有边界从注释搬进类型系统。
    ///
    /// **为什么需要显示标注**（本工程实证）：该边界原本只写在注释里，注释随之漂移——
    /// <c>EntitySlot.FireStanceFrames</c> 的注释至今写着"私有面"，而它的 proto 字段
    /// （<c>fire_stance_frames = 19</c>）与 <see cref="SimChecksum.ComputePublicChecksum"/> 都表明
    /// 它**早已公共化**；<c>SimChecksum</c> 内部两处注释更是互相矛盾（全量口径那行说"私有面"）。
    /// 注释漂移的代价是实打实的：<c>CorpseFrames</c> 曾因"注释说公共面、wire 却没有"导致
    /// 尸体期内每帧假和解。**标注 + 守卫测试**把这类漂移变成编译期/CI 期可见。
    ///
    /// **用法**：标在<b>字段</b>上（<c>EntitySlot</c>、运行态与分型表结构体的字段）。
    /// 未标注 = 不参与同步的纯本地/瞬态字段（如 <c>EntitySlot.FaceExitTurning</c>——
    /// 由输入历史派生、可重放重建，故不占协议字段号）。
    ///
    /// **不改变任何运行时行为**：纯元数据，守卫测试与同步代码生成器经反射读取。
    /// 故对 Sim 判定零影响（确定性、checksum、回滚全部不变）。
    ///
    /// <paramref name="flatten"/> / <paramref name="wireType"/> 是**给生成器**的信息：
    /// 前者声明"复合值拆成哪几个标量分量"，后者声明"线上承载类型"（如 <c>byte</c> 走 <c>uint32</c>）。
    /// 不写则按类型机械推断（int/long/uint/byte/float 直通，枚举按其底层整型）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class StateLayerAttribute : Attribute
    {
        public StateLayer Layer { get; }

        /// <summary>复合值的标量分量名（null = 叶子字段，不展开）。
        /// 例：<c>Pos</c> 声明 <c>new[]{"X","Y","Z"}</c> → 生成 <c>Pos.X</c>/<c>Pos.Y</c>/<c>Pos.Z</c> 三段。</summary>
        public string[] Flatten { get; }

        /// <summary>线上承载类型名（null = 按本字段类型推断）。
        /// 例：<c>byte FireStanceFrames</c> 声明 <c>"uint32"</c> 以匹配 proto 字段类型。</summary>
        public string WireType { get; }

        /// <summary>
        /// **子槽索引条件**（-1 = 无条件，任何索引都按本标注的层次处理）。
        ///
        /// 用途：<c>ActionRuntime</c> 是**按数组索引分层的混合体**——同一个结构体的同一个字段，
        /// 槽 0（主动作槽）是公共摘要，槽 1..3（Skill1..3）是本人私有面。
        /// 声明 <c>onlySlotIndex: 0</c> 表示"仅当子槽索引为 0 时才归 <see cref="Layer"/> 声明的层次"，
        /// 其余索引用 <see cref="OtherwiseLayer"/>。
        ///
        /// 这种"同字段按容器位置分层"的语义**无法机械推断**，必须显式声明——
        /// 它正是生成器不能只看类型的原因。
        /// </summary>
        public int OnlySlotIndex { get; }

        /// <summary>
        /// 当子槽索引 ≠ <see cref="OnlySlotIndex"/> 时的层次（仅 <see cref="OnlySlotIndex"/> ≥ 0 时有意义）。
        /// 例：<c>ActionId</c> 声明 <c>onlySlotIndex: 0, otherwise: Private</c> ⇒
        /// 槽 0 进公共摘要，槽 1..3 退化为私有面。
        /// </summary>
        public StateLayer OtherwiseLayer { get; }

        public StateLayerAttribute(StateLayer layer, string[] flatten = null, string wireType = null,
            int onlySlotIndex = -1, StateLayer otherwiseLayer = StateLayer.Private)
        {
            Layer = layer;
            Flatten = flatten;
            WireType = wireType;
            OnlySlotIndex = onlySlotIndex;
            OtherwiseLayer = otherwiseLayer;
        }
    }

    /// <summary>同步层次取值（与《状态同步专项设计》§5.2 快照分层的三层对应）。</summary>
    public enum StateLayer
    {
        /// <summary>
        /// **公共面**：进公共快照（wire 的 SlotDelta）**且**进公共口径 checksum。
        /// 客户端可重建 ⇒ 参与线上和解比对。新增此类字段必须同步扩 proto 与
        /// <see cref="SimChecksum.ComputePublicChecksum"/>——守卫测试会卡住漏改。
        /// </summary>
        Public = 0,

        /// <summary>
        /// **私有面**：不进公共快照、不进公共口径 checksum；但**进全量口径 checksum**
        /// （回滚/重放对账的确定性状态）。私有面随每份快照全量发**本人**
        /// （<c>PrivateStateDelta</c>），他人不可见。
        /// 典型：弹药、技能冷却/充能、背包、资源。
        /// </summary>
        Private = 1,

        /// <summary>
        /// **仅确定性内部态**：不进任何快照（不上 wire）、不进公共口径 checksum，
        /// 但**必须进全量口径 checksum**——它参与 Sim 判定，漏了会让重放对账失效。
        /// 典型：<c>EntitySlot.FaceExitTurning</c>（改写 Yaw 的过渡标记，由输入历史可重放重建，
        /// 故不需要上线，但重放对账时必须逐位一致）。
        /// </summary>
        Internal = 2,
    }
}
