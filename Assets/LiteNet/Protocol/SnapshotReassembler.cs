using System;
using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 客户端快照镜像（《M10实施指导》决策 7 "客户端侧" + 《状态同步实施方案》§5.5；
    /// P0 分层应用：《状态同步专项设计》§5.2）：
    /// 维护"最近收到的快照"并**重建完整状态**供和解使用（<c>RollbackSim.OnAuthoritativeSnapshot</c> 的输入形态）。
    ///
    /// **持久镜像语义**：调用方应持有一份常驻 <see cref="SimWorldState"/>（本类只提供应用原语）——
    /// 增量快照只覆盖指到的槽位，"缺席 = 未变化"无法表达槽位全集；每包新建镜像会把未携带槽位清零
    /// （= 权威实体被抹掉，直到下一全量才回来）。重连/丢包追帧由全量快照整体重建。
    ///
    /// 重建语义：全量 = 缺席槽位一律视为死亡（权威事实，清位图），并清空全部运行态数组；
    /// 增量 = 只覆盖 SlotDelta 指到的槽位。槽位应用同时写**主动作摘要**（Actions[slot*4+0]——
    /// 摘要属公共面）；比赛状态与本人私有面随每份快照应用。
    /// 分配器版本由 <c>Id >> 16</c> 重建、<c>_nextFree</c> 取"已用槽数"——两者都**不是逻辑字段**（不进 checksum），
    /// 只保证重建态继续 Spawn 时不与既有 Id 撞车。帧头取快照帧号（**不是**本地帧号——
    /// 和解要的是"权威帧号 + 该帧权威状态"这一对）。
    ///
    /// 副作用即设计：客户端镜像只用于和解（覆盖本地预测态）。本地 Sim 由 RollbackSim 自持。
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
        /// **无和解快照的覆盖原语**：和解未触发（RollbackSim.OnAuthoritativeSnapshot == false，预测正确）时
        /// 本地态不会被权威覆盖——但比赛状态与**本人私有面**（弹药/技能 CD/背包/资源，客户端不预测、
        /// 本地 Sim 推不出来的量）仍须随每份快照刷新到最新。幂等：已和解路径（镜像已 CopyTo）重复调用无副作用。
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
            };
        }
    }
}
