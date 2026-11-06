namespace LiteSim
{
    /// <summary>
    /// 武器系统（《游戏业务系统总设计》§4.2 顺序：Input → Movement → **Weapon** → Shooting； 本系统在 ShootingSystem 之前跑）。
    /// </summary>
    public static class WeaponSystem
    {
        /// <summary><paramref name="rules"/> = 本局测试规则（null = 无规则——无限子弹关闭）。</summary>
        public static void Run(SimWorldState s, SimInputFrame[] inputs, WeaponTable table, MatchTestRules rules = null)
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
                        ClearActionSummary(s, i);                            // 换弹结束：清理公共动作摘要（远端换弹表现载体）
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
                    if (TryStartReload(ref w, in def, frame))
                        SetActionSummary(s, slot, frame);                 // 换弹开始：写公共动作摘要（远端换弹表现载体）
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
        /// 开火消费（ShootingSystem 判定点调用）：Ready ∧ 半自动击发沿 ∧ 节拍到帧 ∧ （有弹 ∨ 无限子弹）→ 扣弹。
        /// </summary>
        public static bool TryConsumeShot(SimWorldState s, int slotIndex, int frame, WeaponTable table, out WeaponDef def,
            MatchTestRules rules = null)
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
            bool infinite = rules != null && rules.InfiniteAmmo;          // 无限子弹（注入规则）——空仓门与扣弹共用
            if (w.MagAmmo <= 0 && !infinite) return false;                // 空仓（备弹有余时已由上次末发自动换弹）；无限子弹放行——干涸枪现场开开关即复活

            // 测试模式无限子弹（注入规则）：不扣弹、也不会打空（末发自动换弹随之不触发）——满弹状态下换弹请求亦被 ② 拒
            if (!infinite)
            {
                w.MagAmmo--;
                if (w.MagAmmo == 0 && w.ReserveAmmo > 0)                  // 末发自动换弹（打空即补）
                {
                    w.State = WeaponSlotState.Reloading;
                    w.ReloadEndFrame = frame + def.ReloadFrames;
                    SetActionSummary(s, slotIndex, frame);                // 自动换弹同样写公共动作摘要（与手动换弹同口径）
                }
            }
            w.NextFireFrame = frame + MatchTestRules.ResolveFireIntervalFrames(rules, def.Rpm, def.FireIntervalFrames);
            w.ShotSeq++;
            return true;
        }

        /// <summary>
        /// 切枪请求（<see cref="SimInputFrame.ButtonSwitchWeapon"/> 边沿；槽位范围由 InputGate 拒伪造， 此处防御性再验）。
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
            ClearActionSummary(s, slotIndex);                   // 新选中槽为 Switching（非换弹）——换弹摘要随之清空
        }

        private static bool TryStartReload(ref WeaponRuntime w, in WeaponDef def, int frame)
        {
            if (w.State != WeaponSlotState.Ready) return false;           // lint-allow R3（枚举判等）
            if (w.MagAmmo >= def.MagazineSize) return false;              // 满弹不换
            if (w.ReserveAmmo <= 0) return false;                         // 无备弹不换
            w.State = WeaponSlotState.Reloading;
            w.ReloadEndFrame = frame + def.ReloadFrames;
            return true;
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

        /// <summary>
        /// 写主动作摘要（槽 0 = 公共动作面，《联机同步残差收敛专项设计》§3.2）：换弹开始/自动换弹调用。
        /// </summary>
        private static void SetActionSummary(SimWorldState s, int slotIndex, int frame)
        {
            ref ActionRuntime a = ref s.Actions[slotIndex * SimConfig.ActionSlotsPerEntity];
            a.ActionId = ActionIds.Reload;
            a.StartFrame = frame;
            a.Phase = ActionPhase.Active;
        }

        /// <summary>清主动作摘要（换弹结束/被切枪打断）：回到"无动作"。</summary>
        private static void ClearActionSummary(SimWorldState s, int slotIndex)
        {
            ref ActionRuntime a = ref s.Actions[slotIndex * SimConfig.ActionSlotsPerEntity];
            a.ActionId = ActionIds.None;
            a.StartFrame = 0;
            a.Phase = ActionPhase.None;
        }
    }
}
