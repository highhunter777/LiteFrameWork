using LiteSim;

using LiteSync.Input;
namespace RoomServer.Runtime
{
    /// <summary>
    /// 客户端输入包的纯数据载荷（R1：《商业级通用服务端框架总设计》§8.1——RoomRuntime 禁止触达 proto 类型，App 解码 <c>InputMessage</c> 后填充本结构）。
    /// </summary>
    public struct ClientInputBatch
    {
        /// <summary>
        /// 冗余窗口帧数上限 = <c>LiteSync.Input.InputPacker.MaxRedundancy</c>（16）。
        /// </summary>
        public const int MaxFrames = 16;

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
