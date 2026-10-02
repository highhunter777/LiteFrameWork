using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 快照编解码（《状态同步专项设计》§5.2 快照分层）：
    /// - **公共面**（SlotDelta ↔ EntitySlot + 主动作摘要）：位级精确（协议单源约定）——量化整型是带宽调优项，
    ///   引入即破坏位级和解机制，需专项评估后另行落。
    /// - **比赛状态层**（<see cref="PackMatch"/>）：room 级，随每份快照全量携带。
    /// - **私有面**（<see cref="PackPrivate"/>/<see cref="ApplyPrivate"/>）：只发本人（弹药/技能 CD/状态明细/局内包/资源）。
    /// 全量快照 = 全部活体槽位 + Match（+ 重连/单测路径按会话附 Private）；
    /// 客户端可由 SlotDelta.Id 重建分配器 version（Id >> 16），槽位缺席 = 死亡/未生成。
    /// checksum 口径：**公共口径**（<see cref="SimChecksum.ComputePublicChecksum"/>）——
    /// 客户端可重建层的和解锚点；私有面不进比对（见 SimChecksum 类注释）。
    /// </summary>
    public static class SnapshotCodec
    {
        // ---- 公共面 ----
        // 含**开火驻留窗**（fire_stance_frames=19）：限速+朝准星语境的判定输入——
        // 回滚基线/差分基线/和解锚点必须能重建它，缺失 ⇒ 窗内预测分叉 ⇒ 逐快照纠偏（橡皮筋）。

        public static Proto.SlotDelta ToDelta(int slot, in EntitySlot e, in ActionRuntime activeAction)
        {
            return new Proto.SlotDelta
            {
                Slot = slot,
                Id = e.Id,
                PosX = e.Pos.X, PosY = e.Pos.Y, PosZ = e.Pos.Z,
                VelX = e.Vel.X, VelY = e.Vel.Y, VelZ = e.Vel.Z,
                Yaw = e.Yaw,
                Hp = e.Hp,
                Flags = e.Flags,
                Shield = e.Shield,
                Kills = e.Kills,
                Deaths = e.Deaths,
                SelectedWeapon = e.SelectedWeapon,
                FireStanceFrames = e.FireStanceFrames,     // 开火驻留窗（回滚基线/重放重建面）
                ActionId = activeAction.ActionId,
                ActionPhase = (int)activeAction.Phase,
                ActionStartFrame = activeAction.StartFrame,
            };
        }

        /// <summary>快捷重载：槽位只含实体字段（主动作摘要清零）——诊断/位级往返用例用；
        /// 广播路径必须走带 state 的重载（主动作摘要是公共面的一部分）。</summary>
        public static Proto.SlotDelta ToDelta(int slot, in EntitySlot e)
        {
            return ToDelta(slot, in e, default(ActionRuntime));
        }

        public static void FromDelta(Proto.SlotDelta d, out int slot, out EntitySlot e)
        {
            slot = d.Slot;
            e = new EntitySlot
            {
                Id = d.Id,
                Pos = new SimVector3(d.PosX, d.PosY, d.PosZ),
                Vel = new SimVector3(d.VelX, d.VelY, d.VelZ),
                Yaw = d.Yaw,
                Hp = d.Hp,
                Flags = d.Flags,
                Shield = d.Shield,
                Kills = d.Kills,
                Deaths = d.Deaths,
                SelectedWeapon = d.SelectedWeapon,
                FireStanceFrames = (byte)d.FireStanceFrames,   // 开火驻留窗（FromDelta 还原进 EntitySlot——回滚基线携带）
            };
        }

        /// <summary>主动作摘要（SlotDelta 动作字段 → ActionRuntime 主动作槽形态；
        /// 技能槽的冷却/充能不在此——那是本人私有面）。Phase 取值域见 <see cref="ActionPhase"/>。</summary>
        public static ActionRuntime ActiveActionFromDelta(Proto.SlotDelta d)
        {
            return new ActionRuntime
            {
                ActionId = d.ActionId,
                StartFrame = d.ActionStartFrame,
                Phase = (ActionPhase)d.ActionPhase,
            };
        }

        /// <summary>打包全量快照（全部活体槽位 + 主动作摘要 + Match + 公共口径 checksum + ackInput）。
        /// 私有面按会话另附（广播：<see cref="SnapshotDiffer.BuildFor"/>；重连：ServerHost.HandleReconnect）。</summary>
        public static Proto.StateSnapshot PackFull(int frame, SimWorldState s, int ackInput)
        {
            var msg = new Proto.StateSnapshot
            {
                Frame = frame,
                IsFull = true,
                Checksum = SimChecksum.ComputePublicChecksum(s),
                AckInput = ackInput,
                Match = PackMatch(s),
            };
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;
                msg.Slots.Add(ToDelta(i, s.Entities[i], s.Actions[i * SimConfig.ActionSlotsPerEntity]));
            }
            return msg;
        }

        // ---- 比赛状态层 ----

        /// <summary>Match → wire（字段清单单源；客户端应用见 <see cref="SnapshotReassembler"/>）。</summary>
        public static Proto.MatchStateDelta PackMatch(SimWorldState s)
        {
            return new Proto.MatchStateDelta
            {
                Phase = s.Match.Phase,
                Team = s.Match.Team,
                Score = s.Match.Score,
                Timer = s.Match.Timer,
                Round = s.Match.Round,
                Winner = s.Match.Winner,
            };
        }

        // ---- 私有面 ----

        /// <summary>本人私有状态打包（只发本人；《游戏业务系统总设计》§1——禁止泄漏他人库存）。
        /// <paramref name="entityId"/> 失效（已死/未生成）返回 null——本人快照里不再携带私有段。</summary>
        public static Proto.PrivateStateDelta PackPrivate(SimWorldState s, long entityId)
        {
            if (!s.TryResolve(entityId, out int slot)) return null;

            var msg = new Proto.PrivateStateDelta { EntityId = entityId, Resource = s.Resources[slot] };

            int wbase = slot * SimConfig.WeaponSlotsPerEntity;
            for (int w = 0; w < SimConfig.WeaponSlotsPerEntity; w++)
            {
                ref WeaponRuntime r = ref s.Weapons[wbase + w];
                msg.Weapons.Add(new Proto.WeaponDelta
                {
                    WeaponDefId = r.WeaponDefId,
                    MagAmmo = r.MagAmmo,
                    ReserveAmmo = r.ReserveAmmo,
                    State = (int)r.State,
                    NextFireFrame = r.NextFireFrame,
                    ReloadEndFrame = r.ReloadEndFrame,
                    EquipEndFrame = r.EquipEndFrame,
                    ShotSeq = r.ShotSeq,
                });
            }

            int abase = slot * SimConfig.ActionSlotsPerEntity;
            for (int a = 1; a < SimConfig.ActionSlotsPerEntity; a++)   // 1..3 = Skill1..3（0 = 主动作槽走公共面）
            {
                ref ActionRuntime r = ref s.Actions[abase + a];
                msg.Skills.Add(new Proto.SkillDelta
                {
                    ActionId = r.ActionId,
                    CooldownEnd = r.CooldownEnd,
                    Charges = r.Charges,
                });
            }

            int sbase = slot * SimConfig.StatusSlotsPerEntity;
            for (int i = 0; i < SimConfig.StatusSlotsPerEntity; i++)
            {
                ref StatusSlotData r = ref s.Status[sbase + i];
                msg.Status.Add(new Proto.StatusDelta
                {
                    EffectId = r.EffectId,
                    EndFrame = r.EndFrame,
                    Param = r.Param,
                });
            }

            int bbase = slot * SimConfig.MatchBagSlotsPerEntity;
            for (int i = 0; i < SimConfig.MatchBagSlotsPerEntity; i++)
            {
                ref MatchBagSlot r = ref s.MatchBag[bbase + i];
                msg.Bag.Add(new Proto.BagSlotDelta
                {
                    ItemDefId = r.ItemDefId,
                    Count = r.Count,
                    QuickSlot = r.QuickSlot,
                });
            }

            return msg;
        }

        /// <summary>私有面应用（wire → <paramref name="dst"/> 本人槽位段）：
        /// 快照镜像重建与"无和解快照的私有/比赛覆盖"（<see cref="SnapshotReassembler.OverlayPrivateAndMatch"/>）共用。
        /// 按 EntityId 解析槽位——解析失败静默忽略（本人已死：下份快照的公共层即权威事实）。</summary>
        public static void ApplyPrivate(Proto.PrivateStateDelta msg, SimWorldState dst)
        {
            if (msg == null || !dst.TryResolve(msg.EntityId, out int slot)) return;

            dst.Resources[slot] = msg.Resource;

            int wbase = slot * SimConfig.WeaponSlotsPerEntity;
            for (int w = 0; w < SimConfig.WeaponSlotsPerEntity && w < msg.Weapons.Count; w++)
            {
                Proto.WeaponDelta d = msg.Weapons[w];
                dst.Weapons[wbase + w] = new WeaponRuntime
                {
                    WeaponDefId = d.WeaponDefId,
                    MagAmmo = d.MagAmmo,
                    ReserveAmmo = d.ReserveAmmo,
                    State = (WeaponSlotState)d.State,
                    NextFireFrame = d.NextFireFrame,
                    ReloadEndFrame = d.ReloadEndFrame,
                    EquipEndFrame = d.EquipEndFrame,
                    ShotSeq = d.ShotSeq,
                };
            }

            // 技能槽 1..3（主动作槽 0 的摘要走公共面，不覆盖）
            int abase = slot * SimConfig.ActionSlotsPerEntity;
            for (int a = 1; a < SimConfig.ActionSlotsPerEntity && a - 1 < msg.Skills.Count; a++)
            {
                Proto.SkillDelta d = msg.Skills[a - 1];
                ref ActionRuntime r = ref dst.Actions[abase + a];
                r.ActionId = d.ActionId;
                r.CooldownEnd = d.CooldownEnd;
                r.Charges = d.Charges;
            }

            int sbase = slot * SimConfig.StatusSlotsPerEntity;
            for (int i = 0; i < SimConfig.StatusSlotsPerEntity && i < msg.Status.Count; i++)
            {
                Proto.StatusDelta d = msg.Status[i];
                dst.Status[sbase + i] = new StatusSlotData { EffectId = d.EffectId, EndFrame = d.EndFrame, Param = d.Param };
            }

            int bbase = slot * SimConfig.MatchBagSlotsPerEntity;
            for (int i = 0; i < SimConfig.MatchBagSlotsPerEntity && i < msg.Bag.Count; i++)
            {
                Proto.BagSlotDelta d = msg.Bag[i];
                dst.MatchBag[bbase + i] = new MatchBagSlot { ItemDefId = d.ItemDefId, Count = d.Count, QuickSlot = d.QuickSlot };
            }
        }
    }
}
