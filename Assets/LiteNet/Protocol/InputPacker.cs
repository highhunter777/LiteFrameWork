using System;
using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 输入打包/解包（§4.2 冗余：每包带最近 ≤4 帧输入；frames[0] = frame 最新帧，早帧在后）。
    /// 服务器解包按"帧号 → 冗余窗口偏移"取帧：丢一包仍能从后续包补帧。
    /// SimInputFrame ↔ Proto.InputFrame 字段一一对应——protobuf float = IEEE 32 位，位级精确往返
    /// （M9 和解机制依赖位级一致；EntityId 由服务器按会话覆写，打包侧原样携带）。
    /// </summary>
    public static class InputPacker
    {
        /// <summary>冗余帧数上限（§4.2）。</summary>
        public const int MaxRedundancy = 4;

        /// <summary>打包：recent[0] = frame 的输入，recent[i] = frame - i（调用方维护最近帧环形序列）。</summary>
        public static Proto.InputMessage Pack(int frame, ReadOnlySpan<SimInputFrame> recent, int ackSnapshot, int viewFrame)
        {
            var msg = new Proto.InputMessage { Frame = frame, AckSnapshot = ackSnapshot, ViewFrame = viewFrame };
            int count = Math.Min(recent.Length, MaxRedundancy);
            for (int i = 0; i < count; i++) msg.Frames.Add(ToProto(recent[i]));
            return msg;
        }

        /// <summary>按帧号从冗余窗口取输入（窗口外/帧号倒挂返回 false）。</summary>
        public static bool TryGetFrame(Proto.InputMessage msg, int frame, out SimInputFrame input)
        {
            input = default;
            int offset = msg.Frame - frame;
            if (offset < 0 || offset >= msg.Frames.Count) return false;
            input = FromProto(msg.Frames[offset]);
            return true;
        }

        public static Proto.InputFrame ToProto(in SimInputFrame src)
        {
            return new Proto.InputFrame
            {
                EntityId = src.EntityId, MoveX = src.MoveX, MoveZ = src.MoveZ, AimX = src.AimX, AimZ = src.AimZ, Buttons = src.Buttons,
                SelectedWeaponSlot = src.SelectedWeaponSlot, TargetEntityId = src.TargetEntityId, ActionSeq = src.ActionSeq,
            };
        }

        public static SimInputFrame FromProto(Proto.InputFrame src)
        {
            return new SimInputFrame
            {
                EntityId = src.EntityId, MoveX = src.MoveX, MoveZ = src.MoveZ, AimX = src.AimX, AimZ = src.AimZ, Buttons = src.Buttons,
                SelectedWeaponSlot = src.SelectedWeaponSlot, TargetEntityId = src.TargetEntityId, ActionSeq = src.ActionSeq,
            };
        }
    }
}
