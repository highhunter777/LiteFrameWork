using System;

namespace LiteSim
{
    /// <summary>
    /// 字段的**同步层次**（《状态同步专项设计》§5.2 快照分层）——公开/私有边界从注释搬进类型系统。
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
        /// </summary>
        public int OnlySlotIndex { get; }

        /// <summary>
        /// 当子槽索引 ≠ <see cref="OnlySlotIndex"/> 时的层次（仅 <see cref="OnlySlotIndex"/> ≥ 0 时有意义）。
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
        /// </summary>
        Public = 0,

        /// <summary>
        /// **私有面**：不进公共快照、不进公共口径 checksum；但**进全量口径 checksum** （回滚/重放对账的确定性状态）。
        /// </summary>
        Private = 1,

        /// <summary>
        /// **仅确定性内部态**：不进任何快照（不上 wire）、不进公共口径 checksum， 但**必须进全量口径 checksum**——它参与 Sim 判定，漏了会让重放对账失效。
        /// </summary>
        Internal = 2,
    }
}
