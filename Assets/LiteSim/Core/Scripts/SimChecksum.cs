using System;

namespace LiteSim
{
    /// <summary>
    /// 全量状态校验（《状态同步实施方案》§3.6 + M8 决策 #15）：FNV-1a 32 位，遍历顺序恒定。
    /// 覆盖 Frame / RngState / 全部槽位逻辑字段 / Globals / CustomData / 武器/动作/状态/局内包/资源运行态 / Match——
    /// 与 SimWorldState 同源，结构上杜绝漏字段（§3.1 固定布局的红利）。
    /// 不覆盖：Cmds/Events（帧内瞬态，§3.7）；_versions/_nextFree（分配器状态，非逻辑字段）。
    /// 开发期每帧算；release 每 10 帧（M8 由驱动/沙盒决定调用频率）。
    /// v3 定位：本地调试与复查重放的一致性裁判（不上服务器对账）。
    ///
    /// **P0 起两套口径**（《状态同步专项设计》§5.2 快照分层 + 《游戏业务系统总设计》§1 阻塞项）：
    /// - <see cref="ComputeChecksum"/>/<see cref="ComputeStateChecksum"/>：**全量口径**——确定性基线、
    ///   同种子重放对账、归档复查用（回滚环/服务器内部比较）。任何进判定的新状态必须加进
    ///   <see cref="MixFullState"/>（漏一个 = 重放对账漏检）。
    /// - <see cref="ComputePublicChecksum"/>：**公共口径**——只覆盖"全量快照可重建 + 客户端可预测"的层
    ///   （实体公共面 + 主动作摘要 + Match + 活体位图）。线上 StateSnapshot.checksum 携带此值，
    ///   客户端和解用它比对（RollbackSim.OnAuthoritativeSnapshot）——私有面（他人弹药/技能 CD/背包/资源、
    ///   RngState、状态效果明细）客户端**永远无法重建**，进比对口径只会制造永假和解（P0 定案）。
    /// </summary>
    public static class SimChecksum
    {
        private const uint FnvOffset = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>
        /// 全量口径（不含 Frame）：供"跨实例同步位"判定使用
        /// （M10 批③ 差分器的全局状态探针与用例断言：两个同种子同输入的世界应逐位同步，
        /// 但各跑各的帧号——拿含 Frame 的 checksum 比会永远不等，实测踩过）。
        /// </summary>
        public static uint ComputeStateChecksum(in SimWorldState s)
        {
            return MixFullState(FnvOffset, in s);
        }

        public static uint ComputeChecksum(in SimWorldState s)
        {
            uint h = FnvOffset;
            h = MixUInt32(h, (uint)s.Frame);
            return MixFullState(h, in s);
        }

        /// <summary>
        /// **公共口径**（线上和解锚点）：覆盖"全量快照可重建"的层——
        /// Frame + 活体位图 + 实体公共面（SlotDelta 全字段）+ 主动作摘要 + Match。
        /// 与 <see cref="SnapshotCodec"/>/<see cref="SnapshotReassembler"/> 的公共面字段清单逐项对齐
        /// （漏一个 = 该层分叉但和解不报，静默漂移）。
        /// </summary>
        public static uint ComputePublicChecksum(in SimWorldState s)
        {
            uint h = FnvOffset;
            h = MixUInt32(h, (uint)s.Frame);

            uint[] alive = s.AliveBitmap;
            for (int i = 0; i < alive.Length; i++) h = MixUInt32(h, alive[i]);

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                ref EntitySlot e = ref s.Entities[i];
                h = MixInt64(h, e.Id);
                h = MixFloat(h, e.Pos.X);
                h = MixFloat(h, e.Pos.Y);
                h = MixFloat(h, e.Pos.Z);
                h = MixFloat(h, e.Vel.X);
                h = MixFloat(h, e.Vel.Y);
                h = MixFloat(h, e.Vel.Z);
                h = MixFloat(h, e.Yaw);
                h = MixInt32(h, e.Hp);
                h = MixUInt32(h, e.Flags);
                h = MixInt32(h, e.Shield);
                h = MixInt32(h, e.Kills);
                h = MixInt32(h, e.Deaths);
                h = MixInt32(h, e.SelectedWeapon);

                ref ActionRuntime a = ref s.Actions[i * SimConfig.ActionSlotsPerEntity];   // 主动作槽摘要
                h = MixInt32(h, a.ActionId);
                h = MixInt32(h, a.StartFrame);
                h = MixByte(h, (byte)a.Phase);
            }

            h = MixMatchState(h, in s.Match);

            // 分型表公共面（《实体分型表设计》§2）：道具/投掷物/区域是世界可见面（全端可重建），
            // 与活体位图同判——线上和解口径必须覆盖，漏了 = 该层分叉但和解不报的静默漂移。
            ItemState[] items = s.Items;
            for (int i = 0; i < items.Length; i++)
            {
                ref ItemState it = ref items[i];
                h = MixInt32(h, it.ItemDefId);
                h = MixInt32(h, it.Count);
                h = MixInt64(h, it.OwnerId);
                h = MixInt32(h, it.AgeFrames);
            }

            ProjectileState[] projectiles = s.Projectiles;
            for (int i = 0; i < projectiles.Length; i++)
            {
                ref ProjectileState p = ref projectiles[i];
                h = MixInt32(h, p.ItemDefId);
                h = MixFloat(h, p.Speed);
                h = MixInt32(h, p.DetonateFrame);
                h = MixInt64(h, p.OwnerId);
            }

            ZoneState[] zones = s.Zones;
            for (int i = 0; i < zones.Length; i++)
            {
                ref ZoneState z = ref zones[i];
                h = MixInt32(h, z.ItemDefId);
                h = MixFloat(h, z.Radius);
                h = MixInt32(h, z.RemainingFrames);
                h = MixInt64(h, z.OwnerId);
            }
            return h;
        }

        /// <summary>
        /// 全量口径的公共体（两个全量变体共用同一份字段清单——各自手抄必漂移，M10 已踩过）。
        /// 顺序恒定：RngState → 实体（含公共战斗面）→ Globals → CustomData → 武器 → 动作 → 状态 → 局内包 → 资源 → Match。
        /// </summary>
        private static uint MixFullState(uint h, in SimWorldState s)
        {
            h = MixUInt64(h, s.RngState);

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                ref EntitySlot e = ref s.Entities[i];
                h = MixInt64(h, e.Id);
                h = MixFloat(h, e.Pos.X);
                h = MixFloat(h, e.Pos.Y);
                h = MixFloat(h, e.Pos.Z);
                h = MixFloat(h, e.Vel.X);
                h = MixFloat(h, e.Vel.Y);
                h = MixFloat(h, e.Vel.Z);
                h = MixFloat(h, e.Yaw);
                h = MixInt32(h, e.Hp);
                h = MixUInt32(h, e.Flags);
                h = MixInt32(h, e.Shield);
                h = MixInt32(h, e.Kills);
                h = MixInt32(h, e.Deaths);
                h = MixInt32(h, e.SelectedWeapon);
                // 开火驻留窗（Sim 内派生面，2026-09-30）：只进全量口径——远端的开火输入不可重建，
                // 进公共口径会让"快照早于开火"的每份快照必假和解（与私有面同判；SlotDelta 扩展列 §7 余项）
                h = MixByte(h, e.FireStanceFrames);
            }

            byte[] globals = s.Globals;
            for (int i = 0; i < globals.Length; i++) h = MixByte(h, globals[i]);

            byte[] custom = s.CustomData;
            for (int i = 0; i < custom.Length; i++) h = MixByte(h, custom[i]);

            WeaponRuntime[] weapons = s.Weapons;
            for (int i = 0; i < weapons.Length; i++)
            {
                ref WeaponRuntime w = ref weapons[i];
                h = MixInt32(h, w.WeaponDefId);
                h = MixInt32(h, w.MagAmmo);
                h = MixInt32(h, w.ReserveAmmo);
                h = MixInt32(h, w.NextFireFrame);
                h = MixInt32(h, w.ReloadEndFrame);
                h = MixInt32(h, w.EquipEndFrame);
                h = MixByte(h, (byte)w.State);
                h = MixInt32(h, w.ShotSeq);
            }

            ActionRuntime[] actions = s.Actions;
            for (int i = 0; i < actions.Length; i++)
            {
                ref ActionRuntime a = ref actions[i];
                h = MixInt32(h, a.ActionId);
                h = MixInt32(h, a.StartFrame);
                h = MixByte(h, (byte)a.Phase);
                h = MixInt32(h, a.CastToken);
                h = MixInt32(h, a.CooldownEnd);
                h = MixInt32(h, a.Charges);
            }

            StatusSlotData[] status = s.Status;
            for (int i = 0; i < status.Length; i++)
            {
                ref StatusSlotData st = ref status[i];
                h = MixInt32(h, st.EffectId);
                h = MixInt32(h, st.EndFrame);
                h = MixInt32(h, st.Param);
            }

            MatchBagSlot[] bag = s.MatchBag;
            for (int i = 0; i < bag.Length; i++)
            {
                ref MatchBagSlot b = ref bag[i];
                h = MixInt32(h, b.ItemDefId);
                h = MixInt32(h, b.Count);
                h = MixInt32(h, b.QuickSlot);
            }

            int[] resources = s.Resources;
            for (int i = 0; i < resources.Length; i++) h = MixInt32(h, resources[i]);

            // 分型表（《实体分型表设计》§2：三个确定性面缺一即隐形分叉——这里是第一面）
            ItemState[] items = s.Items;
            for (int i = 0; i < items.Length; i++)
            {
                ref ItemState it = ref items[i];
                h = MixInt32(h, it.ItemDefId);
                h = MixInt32(h, it.Count);
                h = MixInt64(h, it.OwnerId);
                h = MixInt32(h, it.AgeFrames);
            }

            ProjectileState[] projectiles = s.Projectiles;
            for (int i = 0; i < projectiles.Length; i++)
            {
                ref ProjectileState p = ref projectiles[i];
                h = MixInt32(h, p.ItemDefId);
                h = MixFloat(h, p.Speed);
                h = MixInt32(h, p.DetonateFrame);
                h = MixInt64(h, p.OwnerId);
            }

            ZoneState[] zones = s.Zones;
            for (int i = 0; i < zones.Length; i++)
            {
                ref ZoneState z = ref zones[i];
                h = MixInt32(h, z.ItemDefId);
                h = MixFloat(h, z.Radius);
                h = MixInt32(h, z.RemainingFrames);
                h = MixInt64(h, z.OwnerId);
            }

            h = MixMatchState(h, in s.Match);
            return h;
        }

        /// <summary>Match 混合段（全量与公共口径共用——公共面含 Match，字段清单单源）。</summary>
        private static uint MixMatchState(uint h, in MatchStateData m)
        {
            h = MixInt32(h, m.Phase);
            h = MixInt32(h, m.Team);
            h = MixInt32(h, m.Score);
            h = MixInt32(h, m.Timer);
            h = MixInt32(h, m.Round);
            h = MixInt64(h, m.Winner);
            return h;
        }

        // ---- FNV-1a 逐字节混合（浮点经位型逐位确定，非容差） ----

        private static uint MixByte(uint h, byte b)
        {
            return (h ^ b) * FnvPrime;
        }

        private static uint MixUInt32(uint h, uint v)
        {
            h = MixByte(h, (byte)(v & 0xFFu));
            h = MixByte(h, (byte)((v >> 8) & 0xFFu));
            h = MixByte(h, (byte)((v >> 16) & 0xFFu));
            h = MixByte(h, (byte)((v >> 24) & 0xFFu));
            return h;
        }

        private static uint MixInt32(uint h, int v)
        {
            return MixUInt32(h, (uint)v);
        }

        private static uint MixInt64(uint h, long v)
        {
            return MixUInt64(h, (ulong)v);
        }

        private static uint MixUInt64(uint h, ulong v)
        {
            h = MixUInt32(h, (uint)(v & 0xFFFFFFFFu));
            h = MixUInt32(h, (uint)(v >> 32));
            return h;
        }

        private static uint MixFloat(uint h, float v)
        {
            // 位型哈希：跨运行时逐位一致（与 M7 基线同款手段）
            return MixUInt32(h, (uint)BitConverter.SingleToInt32Bits(v));
        }
    }
}
