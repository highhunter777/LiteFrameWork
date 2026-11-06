using System;
using LiteSim;
using Proto = LiteNet.Proto;

using LiteSync.Client;
using LiteSync.Input;
namespace LiteSync.Input
{
    /// <summary>
    /// 输入打包/解包（§4.2 冗余：每包带**未确认段**输入——<see cref="MaxRedundancy"/> 帧封顶； frames[0] = frame 最新帧，早帧在后）。
    /// </summary>
    public static class InputPacker
    {
        /// <summary>冗余窗口帧数上限（§4.2）：未确认段长度封顶（含本帧）。</summary>
        public const int MaxRedundancy = 16;

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

        /// <summary>
        /// 解包**整批**进调用方复用数组（proto → 纯数据，零分配）：拷 <c>min(条数, <see cref="MaxRedundancy"/>)</c> 条到 <paramref name="into"/>。
        /// </summary>
        public static int UnpackFrames(Proto.InputMessage msg, SimInputFrame[] into)
        {
            int raw = msg.Frames.Count;
            int copy = Math.Min(raw, MaxRedundancy);
            for (int i = 0; i < copy; i++) into[i] = FromProto(msg.Frames[i]);
            return raw;
        }

        public static Proto.InputFrame ToProto(in SimInputFrame src)
        {
            return new Proto.InputFrame
            {
                EntityId = src.EntityId, MoveX = src.MoveX, MoveZ = src.MoveZ, Buttons = src.Buttons,
                AimPointX = src.AimPointX, AimPointY = src.AimPointY, AimPointZ = src.AimPointZ,
                SelectedWeaponSlot = src.SelectedWeaponSlot, TargetEntityId = src.TargetEntityId, ActionSeq = src.ActionSeq,
            };
        }

        public static SimInputFrame FromProto(Proto.InputFrame src)
        {
            return new SimInputFrame
            {
                EntityId = src.EntityId, MoveX = src.MoveX, MoveZ = src.MoveZ, Buttons = src.Buttons,
                AimPointX = src.AimPointX, AimPointY = src.AimPointY, AimPointZ = src.AimPointZ,
                SelectedWeaponSlot = src.SelectedWeaponSlot, TargetEntityId = src.TargetEntityId, ActionSeq = src.ActionSeq,
            };
        }
    }
}
