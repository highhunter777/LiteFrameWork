using LiteSim;

namespace LiteGame.Modules.Input
{
    /// <summary>
    /// 输入设备的抽象（《角色状态与动作专项设计》§3 输入三件之一）： **键鼠与触屏输出同一份 Move/Aim/Buttons**——上层协调者与 Sim 都不认识设备。
    /// </summary>
    public interface IIntentSource
    {
        /// <summary>设备名（诊断/切换用；同机多设备的前缀标识）。</summary>
        string Name { get; }

        /// <summary>
        /// 采集本渲染帧的原始意图。
        /// </summary>
        /// <param name="localPos">本地玩家当前**预测**世界位置（瞄准类设备的参照原点；
        IntentSample Sample(in SimVector3 localPos);
    }

    /// <summary>
    /// **瞄准世界注入口**（可选能力，设备源按需实现）。
    /// </summary>
    public interface IAimWorldSink
    {
        /// <summary>
        /// 注入本地预测世界与本地玩家槽位（供瞄准解算求交用）。
        /// </summary>
        void SetAimWorld(SimWorldState world, int localSlot);
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
