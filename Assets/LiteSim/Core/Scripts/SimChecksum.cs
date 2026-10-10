using System;

namespace LiteSim
{
    /// <summary>
    /// 全量状态校验（《状态同步实施方案》§3.6）：FNV-1a 32 位，遍历顺序恒定。
    /// 覆盖 Frame / RngState / 全部槽位逻辑字段 / Globals / CustomData / 武器/动作/状态/局内包/资源运行态 / Match——
    /// 与 SimWorldState 同源，结构上杜绝漏字段（§3.1 固定布局的红利）。
    /// 不覆盖：Cmds/Events（帧内瞬态，§3.7）；_versions/_nextFree（分配器状态，非逻辑字段）。
    /// 开发期每帧算；release 每 10 帧（调用频率由驱动/沙盒决定）。
    /// 定位：本地调试与复查重放的一致性裁判（不上服务器对账）。
    ///
    /// **两套口径**（《状态同步专项设计》§5.2 快照分层 + 《游戏业务系统总设计》§1 阻塞项）：
    /// - <see cref="ComputeChecksum"/>/<see cref="ComputeStateChecksum"/>：**全量口径**——确定性基线、
    ///   同种子重放对账、归档复查用（回滚环/服务器内部比较）。任何进判定的新状态必须加进
    ///   <see cref="MixFullState"/>（漏一个 = 重放对账漏检）。
    /// - <see cref="ComputePublicChecksum"/>：**公共口径**——只覆盖"全量快照可重建 + 客户端可预测"的层
    ///   （实体公共面 + 主动作摘要 + Match + 活体位图）。线上 StateSnapshot.checksum 携带此值，
    ///   客户端和解用它比对（RollbackSim.OnAuthoritativeSnapshot）——私有面（他人弹药/技能 CD/背包/资源、
    ///   RngState、状态效果明细）客户端**永远无法重建**，进比对口径只会制造永假和解。
    /// </summary>
    public static class SimChecksum
    {
        private const uint FnvOffset = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>
        /// 全量口径（不含 Frame）：供"跨实例同步位"判定使用
        /// （差分器的全局状态探针与用例断言：两个同种子同输入的世界应逐位同步，
        /// 但各跑各的帧号——拿含 Frame 的 checksum 比会永远不等）。
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
                // 实体公共面：字段清单不在此手写——由 [StateLayer] 标注生成
                // （EntitySlot.Sync.g.cs，scripts/codegen/gen-sync-code.ps1）。
                // 加字段只改 EntitySlot 一处，此处自动跟上（漏改 = 编译期可见）。
                h = EntitySlot.MixPublic(h, in s.Entities[i]);

                ref ActionRuntime a = ref s.Actions[i * SimConfig.ActionSlotsPerEntity];   // 主动作槽摘要
                h = MixInt32(h, a.ActionId);
                h = MixInt32(h, a.StartFrame);
                h = MixByte(h, (byte)a.Phase);
            }

            h = MixMatchState(h, in s.Match);

            // 分型表公共面（《Sim组织专项设计》§3）：道具/投掷物/区域是世界可见面（全端可重建），
            // 与活体位图同判——线上和解口径必须覆盖，漏了 = 该层分叉但和解不报的静默漂移。
            // 字段清单由 [StateLayer] 标注生成（EntitySlot.Sync.g.cs）——加字段只改结构体一处。
            ItemState[] items = s.Items;
            for (int i = 0; i < items.Length; i++)
                h = ItemState.MixPublic(h, in items[i]);

            ProjectileState[] projectiles = s.Projectiles;
            for (int i = 0; i < projectiles.Length; i++)
                h = ProjectileState.MixPublic(h, in projectiles[i]);

            ZoneState[] zones = s.Zones;
            for (int i = 0; i < zones.Length; i++)
                h = ZoneState.MixPublic(h, in zones[i]);
            return h;
        }

        /// <summary>
        /// 全量口径的公共体（两个全量变体共用同一份字段清单——各自手抄必漂移）。
        /// 顺序恒定：RngState → 实体（含公共战斗面）→ Globals → CustomData → 武器 → 动作 → 状态 → 局内包 → 资源 → Match。
        /// </summary>
        private static uint MixFullState(uint h, in SimWorldState s)
        {
            h = MixUInt64(h, s.RngState);

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                // 实体槽全量面（公共 + 私有 + 仅内部确定性态）：字段清单由 [StateLayer] 标注生成，
                // 不在此手写——加字段只改 EntitySlot 一处（见 EntitySlot.Sync.g.cs）。
                h = EntitySlot.MixFull(h, in s.Entities[i]);
            }

            byte[] globals = s.Globals;
            for (int i = 0; i < globals.Length; i++) h = MixByte(h, globals[i]);

            byte[] custom = s.CustomData;
            for (int i = 0; i < custom.Length; i++) h = MixByte(h, custom[i]);

            WeaponRuntime[] weapons = s.Weapons;
            for (int i = 0; i < weapons.Length; i++)
                h = WeaponRuntime.MixFull(h, in weapons[i]);

            // 运行态数组：字段清单同样由 [StateLayer] 标注生成（见 EntitySlot.Sync.g.cs）。
            // 加字段只改结构体一处。
            ActionRuntime[] actions = s.Actions;
            for (int i = 0; i < actions.Length; i++)
                h = ActionRuntime.MixFull(h, in actions[i], i % SimConfig.ActionSlotsPerEntity);

            StatusSlotData[] status = s.Status;
            for (int i = 0; i < status.Length; i++)
                h = StatusSlotData.MixFull(h, in status[i]);

            MatchBagSlot[] bag = s.MatchBag;
            for (int i = 0; i < bag.Length; i++)
                h = MatchBagSlot.MixFull(h, in bag[i]);

            int[] resources = s.Resources;
            for (int i = 0; i < resources.Length; i++) h = MixInt32(h, resources[i]);

            // 分型表（《Sim组织专项设计》§3：三个确定性面缺一即隐形分叉——这里是第一面）。
            // 字段清单由 [StateLayer] 标注生成（见 EntitySlot.Sync.g.cs）——加字段只改结构体一处。
            ItemState[] items = s.Items;
            for (int i = 0; i < items.Length; i++)
                h = ItemState.MixFull(h, in items[i]);

            ProjectileState[] projectiles = s.Projectiles;
            for (int i = 0; i < projectiles.Length; i++)
                h = ProjectileState.MixFull(h, in projectiles[i]);

            ZoneState[] zones = s.Zones;
            for (int i = 0; i < zones.Length; i++)
                h = ZoneState.MixFull(h, in zones[i]);

            h = MixMatchState(h, in s.Match);
            return h;
        }

        /// <summary>Match 混合段（全量与公共口径共用——公共面含 Match，字段清单单源）。</summary>
        internal static uint MixMatchState(uint h, in MatchStateData m)
        {
            h = MixInt32(h, m.Phase);
            h = MixInt32(h, m.Team);
            h = MixInt32(h, m.Score);
            h = MixInt32(h, m.Timer);
            h = MixInt32(h, m.Round);
            h = MixInt64(h, m.Winner);
            h = MixInt32(h, m.KillLimit);
            h = MixInt32(h, m.RespawnDelayFrames);
            h = MixInt32(h, m.SpawnProtectionFrames);
            h = MixInt32(h, (int)m.EndReason);
            return h;
        }

        // ---- FNV-1a 逐字节混合（浮点经位型逐位确定，非容差） ----

        internal static uint MixByte(uint h, byte b)
        {
            return (h ^ b) * FnvPrime;
        }

        internal static uint MixUInt32(uint h, uint v)
        {
            h = MixByte(h, (byte)(v & 0xFFu));
            h = MixByte(h, (byte)((v >> 8) & 0xFFu));
            h = MixByte(h, (byte)((v >> 16) & 0xFFu));
            h = MixByte(h, (byte)((v >> 24) & 0xFFu));
            return h;
        }

        internal static uint MixInt32(uint h, int v)
        {
            return MixUInt32(h, (uint)v);
        }

        internal static uint MixInt64(uint h, long v)
        {
            return MixUInt64(h, (ulong)v);
        }

        internal static uint MixUInt64(uint h, ulong v)
        {
            h = MixUInt32(h, (uint)(v & 0xFFFFFFFFu));
            h = MixUInt32(h, (uint)(v >> 32));
            return h;
        }

        internal static uint MixFloat(uint h, float v)
        {
            // 位型哈希：跨运行时逐位一致（取位走 BitUtil.Bits——位型转换唯一入口）
            return MixUInt32(h, (uint)BitUtil.Bits(v));
        }
    }
}
