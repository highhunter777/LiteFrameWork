using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 输入设备的抽象（《角色状态与动作专项设计》§3 输入三件之一）：
    /// **键鼠与触屏输出同一份 Move/Aim/Buttons**——上层协调者与 Sim 都不认识设备。
    ///
    /// 契约（实现者必须遵守，消费方据此断言）：
    /// - <see cref="Sample"/> 在**渲染帧**调用（多久调一次由协调者决定，追帧时不重复采）；
    /// - 返回值移动/瞄准分量长度 ≤ 1（<see cref="SimInputFrame"/> 的采集侧契约）；
    /// - 只产出**意图数据**，不产生位移、不写 Sim（《状态同步专项设计》§1 原则 3）；
    /// - 不自行裁决"该不该动"——上下文门与帧边界门归协调者
    ///   （《角色状态与动作专项设计》§3 第 2/3 件），设备源只回答"按键现在是什么"；
    /// - **不自缓存位置**：瞄准类换算要的参照原点由调用方逐帧给出（见
    ///   <see cref="Sample"/> 参数），避免设备侧留一份与裁决不同帧的陈旧位置。
    /// </summary>
    public interface IIntentSource
    {
        /// <summary>设备名（诊断/切换用；同机多设备的前缀标识）。</summary>
        string Name { get; }

        /// <summary>
        /// 采集本渲染帧的原始意图。**不填 EntityId / ActionSeq**（分别由会话与采集层补齐）；
        /// 返回结构里除 Move/Aim/Buttons/SelectedWeaponSlot/TargetEntityId 之外的字段一律视为无效。
        /// </summary>
        /// <param name="localPos">本地玩家当前**预测**世界位置（瞄准类设备的参照原点；
        /// 不需要位置换算的设备可忽略）。</param>
        SimInputFrame Sample(in SimVector3 localPos);
    }
}
