using System;
using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 输入打包/解包（§4.2 冗余：每包带**未确认段**输入——<see cref="MaxRedundancy"/> 帧封顶；
    /// frames[0] = frame 最新帧，早帧在后）。段起点由客户端锚在"最近快照帧 + 1"上（见 `RoomClient.SendInput`）：
    /// 预测领先是常态，固定"最近 N 帧"窗会被领先甩出，服务器要的帧号随之永远取不到
    /// （整包判越界丢弃、输入链锁死）。服务器解包按"帧号 → 冗余窗口偏移"取帧：丢一包仍能从后续包补帧。
    /// SimInputFrame ↔ Proto.InputFrame 字段一一对应——protobuf float = IEEE 32 位，位级精确往返
    /// （和解机制依赖位级一致；EntityId 由服务器按会话覆写，打包侧原样携带）。
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
        /// 解包**整批**进调用方复用数组（proto → 纯数据，零分配）：拷 <c>min(条数, <see cref="MaxRedundancy"/>)</c>
        /// 条到 <paramref name="into"/>，返回**原始条数**（调用方按原始条数填 <c>Count</c>——
        /// 超冗余窗的整条拒绝由闸门判定，形状纪律不在 App 预筛，避免两处口径漂移）。
        ///
        /// **两端宿主唯一映射路径**（P0-3）：服务端宿主 <c>ServerHost.HandleInput</c> 与离线本地服
        /// <c>LocalServerTransport.HandleInput</c> 曾各自手抄一份逐字段拷贝，AimPoint 六字段已真实漂移过一次
        /// （真服链路断点而本地服照常——本地绿掩盖真服红）。字段面归本方法后，加/删字段只改一处，
        /// 该类漂移在结构上不可能再发生。
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
