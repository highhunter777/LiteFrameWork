using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 输入设备的抽象（《角色状态与动作专项设计》§3 输入三件之一）：
    /// **键鼠与触屏输出同一份 Move/Aim/Buttons**——上层协调者与 Sim 都不认识设备。
    ///
    /// <see cref="Sample"/> 的返回是 <see cref="IntentSample"/> 而非裸 <see cref="SimInputFrame"/>：
    /// 多出一个 <c>Handled</c> 位。**非空语义要显性**——"这一帧没有采样（设备未就绪/无设备/被平台
    /// 限制）"与"采样了，结果是空意图（真的没按键）"必须可分辨：前者应保留上一次的有效意图
    /// （否则玩家动作无端冻结），后者才是"这一帧用户什么都没按"。用 <c>default(SimInputFrame)</c>
    /// 表达前者会让两种语义塌缩成一个（历史教训：门控/采样的静默失效从"分不清空与无"开始）。
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
        /// 采集本渲染帧的原始意图。**不填 EntityId**（由会话补齐）；离散意图的 <c>ActionSeq</c>
        /// 由设备源产生（按键沿检测，见 <see cref="IntentSample"/> 的说明）。
        /// 返回 <c>Handled = false</c> 表示本帧**没有采样到**（设备未就绪/无输入设备）。
        /// </summary>
        /// <param name="localPos">本地玩家当前**预测**世界位置（瞄准类设备的参照原点；
        /// 不需要位置换算的设备可忽略）。</param>
        IntentSample Sample(in SimVector3 localPos);
    }

    /// <summary>
    /// 一次设备采样的结果：意图 + **是否真的采到了**。
    /// <see cref="Frame"/> 的字段语义与 <see cref="SimInputFrame"/> 一致（移动/瞄准长度 ≤ 1 由设备侧保证）。
    /// </summary>
    public readonly struct IntentSample
    {
        /// <summary>本帧没有采样到（设备未就绪/无设备）——调用方应保留上一次有效意图。</summary>
        public static IntentSample None => default;

        /// <summary>采样到的意图数据。</summary>
        public readonly SimInputFrame Frame;

        /// <summary>本帧是否真的采到了意图。</summary>
        public readonly bool Handled;

        public IntentSample(SimInputFrame frame)
        {
            Frame = frame;
            Handled = true;
        }
    }
}
