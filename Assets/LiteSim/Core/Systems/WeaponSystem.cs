namespace LiteSim
{
    /// <summary>
    /// 武器系统（《游戏业务系统总设计》§4.2 顺序：Input → Movement → **Weapon** → Shooting；
    /// 本系统在 ShootingSystem 之前跑）：
    /// <list type="number">
    /// <item><b>懒装备</b>（确定性初始化）：活动且存活、槽 0 未装备的实体 → 按表槽位默认映射装备（满弹）——
    ///   首生与 Despawn 后的复活同一条规则自动生效（Despawn 清武器行，见 <see cref="SimWorldState.Despawn"/>）；</item>
    /// <item><b>到帧推进</b>（全槽位）：换弹到帧转移弹药、切枪到帧解除锁定（整数帧倒计，禁浮点）；</item>
    /// <item><b>离散输入</b>（边沿帧才到；seq 由 InputGate 去重）：处理序 = <b>切枪 → 换弹 → 半自动重臂</b>——
    ///   切枪打断换弹、换弹请求落在切枪后的新选中槽上（Switching 中被状态门拒）；半自动扳机在
    ///   <b>未按住开火的帧重臂</b>（松开重臂；缺席帧不重臂——重连后首次按压可能被旧锁存拦下一次，
    ///   确定性无碍，松开重按即恢复）。</item>
    /// </list>
    /// **开火资源与节拍**由 ShootingSystem 经 <see cref="TryConsumeShot"/> 消费（判定点单源不变）——
    /// 本系统不开火、不算伤害。**未装备实体**（测试/沙盒直调 ShootingSystem 的形态）不经武器门：
    /// ShootingSystem 维持旧行为（逐帧 + 兜底伤害），装备实体才受节拍/弹匣/换弹/切枪/半自动约束。
    /// **确定性**：全整数帧运算；懒装备/切枪/重臂只读配置与输入、不回读时钟。
    /// </summary>
    public static class WeaponSystem
    {
        public static void Run(SimWorldState s, SimInputFrame[] inputs, WeaponTable table)
        {
            int frame = s.Frame;

            // ① 懒装备（先于输入/换弹——本帧装备、本帧即可开火，省一帧延迟）
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((s.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                ref EntitySlot e = ref s.Entities[i];
                if (e.Hp <= 0) continue;                                  // 尸体不装备
                ref WeaponRuntime w = ref s.Weapons[i * SimConfig.WeaponSlotsPerEntity];
                if (w.State != WeaponSlotState.Unequipped) continue;      // 已装备形态不动（含换弹/切枪中） // lint-allow R3（枚举判等，非浮点精度比较）
                EquipDefault(ref e, ref w, table);
            }

            // ② 到帧推进（全槽位——与输入无关，整数帧确定性）：换弹转移弹药回 Ready；切枪解除锁定回 Ready
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((s.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                int wi = i * SimConfig.WeaponSlotsPerEntity;
                for (int wSlot = 0; wSlot < SimConfig.WeaponSlotsPerEntity; wSlot++, wi++)
                {
                    ref WeaponRuntime w = ref s.Weapons[wi];
                    if (w.State == WeaponSlotState.Reloading)                // lint-allow R3（枚举判等，非浮点精度比较）
                    {
                        if (frame < w.ReloadEndFrame) continue;
                        CompleteReload(ref w, table);
                    }
                    else if (w.State == WeaponSlotState.Switching)          // lint-allow R3（枚举判等，非浮点精度比较）
                    {
                        if (frame < w.EquipEndFrame) continue;
                        CompleteSwitch(ref w);
                    }
                }
            }

            // ③ 离散输入（同帧重复请求自然被 State 条件吸收）
            for (int i = 0; i < inputs.Length; i++)
            {
                if (!s.TryResolve(inputs[i].EntityId, out int slot)) continue;
                ref EntitySlot e = ref s.Entities[slot];
                if (e.Hp <= 0) continue;
                uint buttons = inputs[i].Buttons;

                // 切枪（先于换弹——切枪打断换弹，换弹请求随之落在切枪后的新选中槽上）
                if ((buttons & SimInputFrame.ButtonSwitchWeapon) != 0u)
                    TryStartSwitch(s, slot, inputs[i].SelectedWeaponSlot, table, frame);

                // 换弹
                if ((buttons & SimInputFrame.ButtonReload) != 0u
                    && IsEquipped(s, slot, table, out WeaponDef def, out int wi))
                {
                    ref WeaponRuntime w = ref s.Weapons[wi];
                    TryStartReload(ref w, in def, frame);
                }

                // 半自动重臂：未按住开火的帧武装扳机（"松开重臂"——击发沿由 TryConsumeShot 消费）
                if ((buttons & SimInputFrame.ButtonFire) == 0u) e.SemiFireArmed = 1;
            }
        }

        /// <summary>槽 0 默认武器满弹装备（懒装备单点；定义经表槽位映射取，防御性回落内置默认）。</summary>
        private static void EquipDefault(ref EntitySlot e, ref WeaponRuntime w, WeaponTable table)
        {
            // 装载链 fail-fast 保证槽 0 默认行在册（默认步枪）；防御性回落内置默认（同值）
            if (!table.TryGetSlotDefault(0, out int defId) || !table.TryGet(defId, out WeaponDef def))
                def = WeaponConfig.DefaultRifle;
            e.SelectedWeapon = 0;
            w.WeaponDefId = def.Id;
            w.MagAmmo = def.MagazineSize;
            w.ReserveAmmo = def.ReserveAmmo;
            w.NextFireFrame = 0;
            w.ReloadEndFrame = 0;
            w.EquipEndFrame = 0;
            w.ShotSeq = 0;
            w.State = WeaponSlotState.Ready;
            e.SemiFireArmed = 1;                              // 新（重）装备扳机待击
        }

        /// <summary>本槽选中武器是否已装备（State ≠ Unequipped、槽位合法、表行在册）。</summary>
        public static bool IsEquipped(SimWorldState s, int slotIndex, WeaponTable table, out WeaponDef def, out int weaponIndex)
        {
            weaponIndex = -1;
            def = default;
            ref EntitySlot e = ref s.Entities[slotIndex];
            int sel = e.SelectedWeapon;
            if (sel < 0 || sel >= SimConfig.WeaponSlotsPerEntity) return false;
            int wi = slotIndex * SimConfig.WeaponSlotsPerEntity + sel;
            ref WeaponRuntime w = ref s.Weapons[wi];
            if (w.State == WeaponSlotState.Unequipped) return false;   // lint-allow R3（枚举判等，非浮点精度比较）
            if (!table.TryGet(w.WeaponDefId, out def)) return false;
            weaponIndex = wi;
            return true;
        }

        /// <summary>
        /// 开火消费（ShootingSystem 判定点调用）：Ready ∧ 半自动击发沿 ∧ 节拍到帧 ∧ 有弹 →
        /// 扣弹、推进 <see cref="WeaponRuntime.NextFireFrame"/>、<see cref="WeaponRuntime.ShotSeq"/>++，
        /// 返回 true 并给武器定义；否则返回 false（该帧不开火——无 Fire 事件、无 RngState 消费）。
        ///
        /// **半自动边沿门**（<see cref="WeaponDef.Automatic"/> = false）：<see cref="EntitySlot.SemiFireArmed"/>
        /// = 1 即"击发沿"——**消费即解除**（清零），与后续门（节拍/弹匣/状态）结果无关：
        /// 冷却/换弹中按压同样消费击发沿（按住不放不会在冷却结束时自动击发——半自动要求逐次按压），
        /// 松开帧由 ③ 重臂。自动武器（true）整段旁路。
        /// **打空自动换弹**：末发消费后弹匣归零且备弹有余 → 同帧进入 Reloading（打空即补；
        /// 备弹零则保持空仓 Ready）。
        /// </summary>
        public static bool TryConsumeShot(SimWorldState s, int slotIndex, int frame, WeaponTable table, out WeaponDef def)
        {
            if (!IsEquipped(s, slotIndex, table, out def, out int wi)) return false;
            ref EntitySlot e = ref s.Entities[slotIndex];
            ref WeaponRuntime w = ref s.Weapons[wi];
            if (!def.Automatic)
            {
                if (e.SemiFireArmed == 0) return false;       // 按住未松开：非击发沿（半自动"按住只发一发"）
                e.SemiFireArmed = 0;                          // 消费击发沿（与后续门结果无关——见方法注释）
            }
            if (w.State != WeaponSlotState.Ready) return false;           // 换弹/切枪中不可开火 // lint-allow R3（枚举判等）
            if (frame < w.NextFireFrame) return false;                    // 射速节拍
            if (w.MagAmmo <= 0) return false;                             // 空仓（备弹有余时已由上次末发自动换弹）

            w.MagAmmo--;
            w.NextFireFrame = frame + def.FireIntervalFrames;
            w.ShotSeq++;

            if (w.MagAmmo == 0 && w.ReserveAmmo > 0)                      // 末发自动换弹（打空即补）
            {
                w.State = WeaponSlotState.Reloading;
                w.ReloadEndFrame = frame + def.ReloadFrames;
            }
            return true;
        }

        /// <summary>
        /// 切枪请求（<see cref="SimInputFrame.ButtonSwitchWeapon"/> 边沿；槽位范围由 InputGate 拒伪造，
        /// 此处防御性再验）。**选中槽立即翻面**（<see cref="EntitySlot.SelectedWeapon"/> 公共面——
        /// 远端武器外观随快照切换），目标槽进入 <see cref="WeaponSlotState.Switching"/> 锁定
        /// （<see cref="WeaponRuntime.EquipEndFrame"/> = frame + 目标武器 switch_frames），到帧由 ② 解除。
        /// 目标槽未装备过 → 即刻按表满弹装备（数据面）；已装备过 → 弹药/节拍数据保持（切回延续）。
        /// 旧选中槽换弹被取消（切枪打断换弹——《游戏业务系统总设计》§5.4"换弹中途取消"）。
        /// 拒绝面：目标 = 当前槽（no-op）/ 当前选中武器在 Switching（举起窗内不再切，防连按刷窗）/
        /// 目标槽无表默认定义（旧表未落该槽）/ 槽位越界。
        /// </summary>
        private static void TryStartSwitch(SimWorldState s, int slotIndex, int targetSlot, WeaponTable table, int frame)
        {
            if (targetSlot < 0 || targetSlot >= SimConfig.WeaponSlotsPerEntity) return;
            ref EntitySlot e = ref s.Entities[slotIndex];
            if (targetSlot == e.SelectedWeapon) return;                    // 同槽重选 = no-op // lint-allow R3（整型判等，非浮点精度比较）

            int wi = slotIndex * SimConfig.WeaponSlotsPerEntity + e.SelectedWeapon;
            if (s.Weapons[wi].State == WeaponSlotState.Switching) return;  // 举起窗内不再切 // lint-allow R3（枚举判等，非浮点精度比较）

            // 目标槽定义（槽位默认映射单源）；无定义 = 拒（表未落该槽行）
            if (!table.TryGetSlotDefault(targetSlot, out int defId) || !table.TryGet(defId, out WeaponDef def)) return;

            // 旧选中槽：取消换弹（弹药/节拍数据保持——切回延续）
            ref WeaponRuntime old = ref s.Weapons[wi];
            if (old.State == WeaponSlotState.Reloading)                    // lint-allow R3（枚举判等，非浮点精度比较）
            {
                old.State = WeaponSlotState.Ready;
                old.ReloadEndFrame = 0;
            }

            int tw = slotIndex * SimConfig.WeaponSlotsPerEntity + targetSlot;
            ref WeaponRuntime w = ref s.Weapons[tw];
            if (w.State == WeaponSlotState.Unequipped)                      // lint-allow R3（枚举判等，非浮点精度比较）
            {
                w.WeaponDefId = def.Id;                        // 首切：按表满弹装备（数据面）
                w.MagAmmo = def.MagazineSize;
                w.ReserveAmmo = def.ReserveAmmo;
                w.NextFireFrame = 0;
                w.ReloadEndFrame = 0;
                w.ShotSeq = 0;
            }
            else if (w.State == WeaponSlotState.Reloading)                   // lint-allow R3（枚举判等，非浮点精度比较）
            {
                w.ReloadEndFrame = 0;                           // 防御：目标槽残留换弹取消（仅选中槽可发起，常态不可达）
            }
            w.State = WeaponSlotState.Switching;
            w.EquipEndFrame = frame + def.SwitchFrames;

            e.SelectedWeapon = targetSlot;                      // 公共面立即翻（Switching 期武器门拦开火）
            e.SemiFireArmed = 1;                                // 新武器扳机待击
        }

        private static void TryStartReload(ref WeaponRuntime w, in WeaponDef def, int frame)
        {
            if (w.State != WeaponSlotState.Ready) return;                 // lint-allow R3（枚举判等）
            if (w.MagAmmo >= def.MagazineSize) return;                    // 满弹不换
            if (w.ReserveAmmo <= 0) return;                               // 无备弹不换
            w.State = WeaponSlotState.Reloading;
            w.ReloadEndFrame = frame + def.ReloadFrames;
        }

        private static void CompleteReload(ref WeaponRuntime w, WeaponTable table)
        {
            if (table.TryGet(w.WeaponDefId, out WeaponDef def))
            {
                int need = def.MagazineSize - w.MagAmmo;
                if (need < 0) need = 0;
                int take = need < w.ReserveAmmo ? need : w.ReserveAmmo;
                w.MagAmmo += take;
                w.ReserveAmmo -= take;
            }
            w.ReloadEndFrame = 0;
            w.State = WeaponSlotState.Ready;
        }

        /// <summary>切枪到帧完成（② 调用）：解除锁定回 Ready；弹药/节拍数据不动。</summary>
        private static void CompleteSwitch(ref WeaponRuntime w)
        {
            w.EquipEndFrame = 0;
            w.State = WeaponSlotState.Ready;
        }
    }
}
