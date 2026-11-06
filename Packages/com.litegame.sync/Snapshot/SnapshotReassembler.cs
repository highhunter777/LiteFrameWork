using System;
using LiteSim;
using Proto = LiteNet.Proto;

using LiteSync.Snapshot;
namespace LiteSync.Snapshot
{
    /// <summary>
    /// 客户端快照镜像（《M10实施指导》决策 7 "客户端侧" + 《状态同步实施方案》§5.5。）。
    /// </summary>
    public static class SnapshotReassembler
    {
        /// <summary>把快照套用到 <paramref name="dst"/>（持久镜像；全量原地重建，增量覆盖变化槽位）。
        /// <paramref name="checksumOut"/> = 快照携带的权威校验值（公共口径）。</summary>
        public static void Apply(Proto.StateSnapshot msg, SimWorldState dst, out uint checksumOut)
        {
            checksumOut = msg.Checksum;

            if (msg.IsFull)
            {
                Array.Clear(dst.AliveBitmap, 0, dst.AliveBitmap.Length);
                for (int i = 0; i < SimConfig.MaxEntities; i++) dst.Entities[i] = default;
                Array.Clear(dst.Globals, 0, dst.Globals.Length);
                Array.Clear(dst.CustomData, 0, dst.CustomData.Length);
                Array.Clear(dst.Weapons, 0, dst.Weapons.Length);
                Array.Clear(dst.Actions, 0, dst.Actions.Length);
                Array.Clear(dst.Status, 0, dst.Status.Length);
                Array.Clear(dst.MatchBag, 0, dst.MatchBag.Length);
                Array.Clear(dst.Resources, 0, dst.Resources.Length);
                dst.Match = default;
            }

            int highest = -1;
            for (int i = 0; i < msg.Slots.Count; i++)
            {
                Proto.SlotDelta d = msg.Slots[i];
                if (d.Slot < 0 || d.Slot >= SimConfig.MaxEntities) continue;   // 越界槽位丢弃（坏包零容忍）
                SnapshotCodec.FromDelta(d, out int slot, out EntitySlot e);
                dst.Entities[slot] = e;
                dst.AliveBitmap[slot >> 5] |= 1u << (slot & 31);
                dst.Actions[slot * SimConfig.ActionSlotsPerEntity] = SnapshotCodec.ActiveActionFromDelta(d);   // 主动作摘要（公共面）
                if (slot > highest) highest = slot;
            }

            // 帧头 + 分配器重建**必须先于私有面应用**：ApplyPrivate 经 TryResolve 定位本人槽位，
            // 而版本表由全量 Id>>16 反推（SetAllocatorIdleSlot）——顺序颠倒 = 解析永远失败、私有面静默不落地
            dst.Frame = msg.Frame;
            dst.SetAllocatorIdleSlot(highest + 1);

            // 比赛状态层 + 本人私有面（全量/增量都随包应用——自带丢包收敛，无需等待全量）
            if (msg.Match != null) dst.Match = FromProto(msg.Match);
            SnapshotCodec.ApplyPrivate(msg.PrivateState, dst);

            // RngState：快照不携带（服务器随机数只用于伤害浮动，客户端预测开火不预测）。保持原值——
            // 和解重放用的是本地历史输入，随机数消费点与服务器不同源也无妨（M10 不预测开火，见决策 9）。
        }

        /// <summary>
        /// 从 wire 快照提取**增量和解材料**：携带槽位列表 + 逐槽 wire 校验值（升序，两缓冲区平行）。
        /// </summary>
        public static int Collect(Proto.StateSnapshot msg, int[] slotBuf, uint[] checksumBuf)
        {
            int count = 0;
            for (int i = 0; i < msg.Slots.Count && count < slotBuf.Length; i++)
            {
                Proto.SlotDelta d = msg.Slots[i];
                slotBuf[count] = d.Slot;
                checksumBuf[count] = d.Checksum;
                count++;
            }
            return count;
        }

        /// <summary>
        /// **无和解快照的覆盖原语**：和解未触发（RollbackSim.OnAuthoritativeSnapshot == false。）。
        /// </summary>
        public static void OverlayPrivateAndMatch(Proto.StateSnapshot msg, SimWorldState dst)
        {
            if (msg.Match != null) dst.Match = FromProto(msg.Match);
            SnapshotCodec.ApplyPrivate(msg.PrivateState, dst);
        }

        /// <summary>wire Match → Sim（与 <see cref="SnapshotCodec.PackMatch"/> 字段清单单源对齐）。</summary>
        public static MatchStateData FromProto(Proto.MatchStateDelta d)
        {
            return new MatchStateData
            {
                Phase = d.Phase,
                Team = d.Team,
                Score = d.Score,
                Timer = d.Timer,
                Round = d.Round,
                Winner = d.Winner,
                KillLimit = d.KillLimit,
                RespawnDelayFrames = d.RespawnDelayFrames,
                SpawnProtectionFrames = d.SpawnProtectionFrames,
                EndReason = (MatchEndReason)d.EndReason,
                GmFlags = d.GmFlags,              // 联机测试房批：权威规则位（客户端据此同值预测）
            };
        }
    }
}
