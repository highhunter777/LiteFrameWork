using System;

namespace LiteSim
{
    /// <summary>
    /// 世界状态容器（《状态同步实施方案》§3.1 固定布局）：
    /// sealed class 持定长数组——构造期一次分配、运行期零 new；EntitySlot[] 是值类型数组，
    /// 逐数组 Array.Copy 即深拷（#1：纯托管，不引 unsafe/UnsafeUtility——Core 零引擎依赖的必然推论）。
    ///
    /// 布局硬约束（#5，反射自检钉死）：进快照的字段只允许值类型/数组——
    /// Custom/Globals 落平面数组（#2/#3：struct 内放数组字段会被 Array.Copy 浅拷共享 → 回滚必错）。
    /// Cmds/Events 为帧内瞬态（§3.7）：不进快照、不进 checksum，每帧末消费清空。
    /// </summary>
    public sealed class SimWorldState
    {
        public int Frame;
        public ulong RngState;

        public readonly EntitySlot[] Entities;
        public readonly uint[] AliveBitmap;
        public readonly byte[] Globals;
        public readonly byte[] CustomData;

        // ---- P0 战斗运行态（《游戏业务系统总设计》§3.1 固定布局 + 《状态同步专项设计》§3"明确的 Weapon/Action/Status/Match 状态数组"）----
        // 纪律与 Entities 相同：值类型定长数组（布局契约 #5），寻址 slot * PerEntity + i；
        // 公开/私有承载边界见各结构体注释（PublicCombatState / PrivateStateSnapshot 两层，§5.2）。

        /// <summary>武器运行态：<c>[slot * WeaponSlotsPerEntity + w]</c>（弹药/换弹/节拍——本人私有面）。</summary>
        public readonly WeaponRuntime[] Weapons;

        /// <summary>动作/技能运行态：<c>[slot * ActionSlotsPerEntity + a]</c>（0=主动作槽；1..3=Skill1..3）。</summary>
        public readonly ActionRuntime[] Actions;

        /// <summary>状态效果槽：<c>[slot * StatusSlotsPerEntity + i]</c>（明细私有；公开投影 = EntitySlot.Shield）。</summary>
        public readonly StatusSlotData[] Status;

        /// <summary>局内背包：<c>[slot * MatchBagSlotsPerEntity + i]</c>（12 格——本人私有面）。</summary>
        public readonly MatchBagSlot[] MatchBag;

        /// <summary>技能资源/实体（本人私有面；技能消耗账本，P1 ActionSystem 消费）。</summary>
        public readonly int[] Resources;

        // ---- 分型表（《实体分型表设计》§1）----
        // 行有效 ⟺ 槽位活体且 EntityFlags.Kind* 置位（kind 位 = 迷你 archetype mask）。
        // 脊柱不动：这些是**平行行表**（每槽位一行），不是每实体子槽阵列——与 P0 运行态同一条
        // 布局纪律（定容值类型数组，#5），但寻址就是槽位索引本身。

        /// <summary>地面道具行表（<c>[slot]</c>；拾取入包后归 MatchBag，本行清零）。</summary>
        public readonly ItemState[] Items;

        /// <summary>投掷物行表（<c>[slot]</c>；手雷/闪光——ProjectileSystem 弹道结算用）。</summary>
        public readonly ProjectileState[] Projectiles;

        /// <summary>区域效果行表（<c>[slot]</c>；EMP/雷达驻留区——ZoneSystem 衰减回收用）。</summary>
        public readonly ZoneState[] Zones;

        /// <summary>比赛状态（room 级；公共面——随每份快照全量下发）。</summary>
        public MatchStateData Match;

        // 非 readonly：可变结构体字段，readonly 会触发防御性拷贝丢写（见 CommandBuffer 注释）。
        public CommandBuffer Cmds;
        public FrameEventBuffer Events;

        // 分配器状态：随快照走（保重放一致），不进 checksum（非逻辑字段，§3.6）。
        private readonly ulong[] _versions;
        private int _nextFree;

        public SimWorldState()
        {
            Entities = new EntitySlot[SimConfig.MaxEntities];
            AliveBitmap = new uint[(SimConfig.MaxEntities + 31) / 32];
            Globals = new byte[SimConfig.GlobalsBytes];
            CustomData = new byte[SimConfig.MaxEntities * SimConfig.CustomBytesPerEntity];
            Weapons = new WeaponRuntime[SimConfig.MaxEntities * SimConfig.WeaponSlotsPerEntity];
            Actions = new ActionRuntime[SimConfig.MaxEntities * SimConfig.ActionSlotsPerEntity];
            Status = new StatusSlotData[SimConfig.MaxEntities * SimConfig.StatusSlotsPerEntity];
            MatchBag = new MatchBagSlot[SimConfig.MaxEntities * SimConfig.MatchBagSlotsPerEntity];
            Resources = new int[SimConfig.MaxEntities];
            Items = new ItemState[SimConfig.MaxEntities];
            Projectiles = new ProjectileState[SimConfig.MaxEntities];
            Zones = new ZoneState[SimConfig.MaxEntities];
            _versions = new ulong[SimConfig.MaxEntities];
            Cmds = new CommandBuffer { Items = new SimCommand[CommandBuffer.Capacity] };
            Events = new FrameEventBuffer { Items = new FrameEvent[FrameEventBuffer.Capacity] };
        }

        /// <summary>槽位是否活体（以 AliveBitmap 为准，§3.1）。</summary>
        public bool IsAlive(int slotIndex)
        {
            return (AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) != 0u;
        }

        /// <summary>活体数（诊断/沙盒 HUD 用；O(MaxEntities/32) 位计数）。</summary>
        public int AliveCount()
        {
            int count = 0;
            for (int i = 0; i < AliveBitmap.Length; i++)
            {
                uint bits = AliveBitmap[i];
                while (bits != 0u) // 与常量 0 比较，非浮点精度比较
                {
                    bits &= bits - 1u; // 逐最低位清除（Brian Kernighan）
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 分配实体（#7/#8）：从 _nextFree 起环形线性找空槽，版本递增后组装 Id；
        /// 入参 slot.Id 被忽略——Id 只能由分配器组装。不做 swap-remove（打乱遍历顺序 = 破确定性）。
        /// 世界满：返回 0 且 slotIndex = -1。
        /// </summary>
        public long Spawn(in EntitySlot slot, out int slotIndex)
        {
            for (int k = 0; k < SimConfig.MaxEntities; k++)
            {
                int i = _nextFree + k;
                if (i >= SimConfig.MaxEntities) i -= SimConfig.MaxEntities;
                if (IsAlive(i)) continue;

                ulong version = (_versions[i] + 1UL) & 0xFFFFFFFFFFFFUL; // 48 位；回绕需 2^48 次复用，现实不可达
                _versions[i] = version;

                EntitySlot s = slot;
                s.Id = (long)(version << 16) | (long)(uint)i;
                Entities[i] = s;
                AliveBitmap[i >> 5] |= 1u << (i & 31);

                _nextFree = i + 1;
                if (_nextFree >= SimConfig.MaxEntities) _nextFree = 0;
                slotIndex = i;
                return s.Id;
            }

            slotIndex = -1;
            return 0L;
        }

        /// <summary>
        /// 释放槽位（§3.1：清 AliveBitmap 位）。Double-free/已死 Id 静默忽略；
        /// 槽位数据清零——**含全部每实体运行态数组段**（武器/动作/状态/局内包/资源）：
        /// 空槽校验值恒定、不随历史漂移（§3.6 全槽位参与校验的前提——上一占用者的残留弹药
        /// 会让"同种子不同历史"的重放校验值分叉）。
        /// </summary>
        public void Despawn(long id)
        {
            if (!TryResolve(id, out int slotIndex)) return;
            AliveBitmap[slotIndex >> 5] &= ~(1u << (slotIndex & 31));
            Entities[slotIndex] = default;
            Array.Clear(Weapons, slotIndex * SimConfig.WeaponSlotsPerEntity, SimConfig.WeaponSlotsPerEntity);
            Array.Clear(Actions, slotIndex * SimConfig.ActionSlotsPerEntity, SimConfig.ActionSlotsPerEntity);
            Array.Clear(Status, slotIndex * SimConfig.StatusSlotsPerEntity, SimConfig.StatusSlotsPerEntity);
            Array.Clear(MatchBag, slotIndex * SimConfig.MatchBagSlotsPerEntity, SimConfig.MatchBagSlotsPerEntity);
            Resources[slotIndex] = 0;
            Items[slotIndex] = default;          // 分型表行随槽位清零（空槽校验值恒定，§3.6 同款前提）
            Projectiles[slotIndex] = default;
            Zones[slotIndex] = default;
        }

        /// <summary>
        /// 跨帧引用唯一入口（#7）：version 校验；失效（已死/槽位复用）返回 false 而非抛——
        /// 引用失效是正常路径（目标已死）。
        /// </summary>
        public bool TryResolve(long id, out int slotIndex)
        {
            if (id == 0L)
            {
                slotIndex = -1;
                return false;
            }

            slotIndex = (int)(id & 0xFFFFL);
            if (slotIndex >= SimConfig.MaxEntities)
            {
                slotIndex = -1;
                return false;
            }

            if (!IsAlive(slotIndex))
            {
                slotIndex = -1;
                return false;
            }

            ulong version = (ulong)id >> 16;
            if (_versions[slotIndex] != version)
            {
                slotIndex = -1;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 深拷快照原语（#1，SnapshotRing 复用）：逐数组 Array.Copy。
        /// 不拷 Cmds/Events（帧内瞬态，§3.7）；
        /// _versions/_nextFree 分配器状态随快照走——否则重放期新分配的 Id 会与被恢复的旧 Id 撞车。
        /// Weapons/Actions/Status/MatchBag/Resources/Match 一并全量拷贝
        /// （《游戏业务系统总设计》§1 阻塞项 ②：新字段漏 CopyTo = 回滚/重连静默分叉）。
        /// </summary>
        public void CopyTo(SimWorldState dst)
        {
            dst.Frame = Frame;
            dst.RngState = RngState;
            dst.Match = Match;
            Array.Copy(Entities, dst.Entities, Entities.Length);
            Array.Copy(AliveBitmap, dst.AliveBitmap, AliveBitmap.Length);
            Array.Copy(Globals, dst.Globals, Globals.Length);
            Array.Copy(CustomData, dst.CustomData, CustomData.Length);
            Array.Copy(Weapons, dst.Weapons, Weapons.Length);
            Array.Copy(Actions, dst.Actions, Actions.Length);
            Array.Copy(Status, dst.Status, Status.Length);
            Array.Copy(MatchBag, dst.MatchBag, MatchBag.Length);
            Array.Copy(Resources, dst.Resources, Resources.Length);
            Array.Copy(Items, dst.Items, Items.Length);          // 分型表（《游戏业务总设计》§1 阻塞项②同款：
            Array.Copy(Projectiles, dst.Projectiles, Projectiles.Length);   // 新字段漏 CopyTo = 回滚/重连静默分叉）
            Array.Copy(Zones, dst.Zones, Zones.Length);
            Array.Copy(_versions, dst._versions, _versions.Length);
            dst._nextFree = _nextFree;
        }

        /// <summary>活体槽位零分配遍历（#9）：升序跳空槽，顺序恒定。</summary>
        public EntityIt IterateAlive()
        {
            return new EntityIt(this);
        }

        /// <summary>
        /// 外部重建态（客户端快照和解）的**分配器状态修复**：把位图/槽位设好后调用。
        /// 两件事：① 逐活体槽位把 <c>_versions</c> 从 <c>Id &gt;&gt; 16</c> 反推回来——
        /// 否则客户端从快照重建后本地版本表归零，新 Spawn 会分配出与服务器既有实体**同 Id** 的实体（撞车 = 分叉）；
        /// ② 空闲游标复位到"最高已用槽 + 1"（避免从 0 起扫到已用槽）。
        /// 分配器状态不进 checksum（§3.6 非逻辑字段），故本操作不破坏校验值。
        /// </summary>
        public void SetAllocatorIdleSlot(int idleSlot)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (IsAlive(i)) _versions[i] = (ulong)Entities[i].Id >> 16;
                else _versions[i] = 0UL;
            }

            if (idleSlot < 0 || idleSlot >= SimConfig.MaxEntities) idleSlot = 0;
            _nextFree = idleSlot;
        }
    }
}
