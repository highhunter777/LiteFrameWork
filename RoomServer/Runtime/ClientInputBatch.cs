using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 客户端输入包的纯数据载荷（R1：《商业级通用服务端框架总设计》§8.1——RoomRuntime 禁止触达
    /// proto 类型，App 解码 <c>InputMessage</c> 后填充本结构）。
    ///
    /// <see cref="Frames"/> 为 **App 持有的复用数组**（热路径零分配）：元素按"帧号升序"填充
    /// （Frames[0] = <see cref="Frame"/> 最新帧，往回连续），App 每包覆写前 <see cref="Count"/> 个元素；
    /// Runtime 只读。EntityId 字段在 App 层**不校验、不覆写**——防伪覆写是闸门职责（<see cref="InputGate"/>）。
    /// </summary>
    public struct ClientInputBatch
    {
        /// <summary>
        /// 冗余窗口帧数上限 = <c>LiteNet.Protocol.InputPacker.MaxRedundancy</c>（4）。
        /// Runtime 层不得引用 LiteNet（R1 纯化红线），故在此**复述常量**；两端一致性由
        /// L1 契约用例钉死（`ClientInputBatch.MaxFrames == InputPacker.MaxRedundancy`）——改一处必红另一处。
        /// </summary>
        public const int MaxFrames = 4;

        /// <summary>客户端声明的最新帧号（窗口最新端；InputMessage.Frame）。</summary>
        public int Frame;

        /// <summary>客户端已收最新快照帧号（InputMessage.AckSnapshot；闸门只记账钳位，语义验证在会话层）。</summary>
        public int AckSnapshot;

        /// <summary>开火视点帧（InputMessage.ViewFrame；回溯对齐参考）。</summary>
        public int ViewFrame;

        /// <summary>Frames 有效长度（0..MaxFrames；越界即恶意包，闸门整条拒绝）。</summary>
        public int Count;

        /// <summary>冗余窗口输入（App 复用数组；[0] = Frame 帧，往回连续）。</summary>
        public SimInputFrame[] Frames;
    }
}
