namespace LiteSim
{
    /// <summary>
    /// 武器系统（《游戏业务系统总设计》§4.2 顺序：Input → Movement → **Weapon** → Shooting；
    /// 本系统在 ShootingSystem 之前跑）：
    /// <list type="number">
    /// <item><b>懒装备</b>（确定性初始化）：活动且存活、槽位状态为未装备的实体 → 装备默认步枪（满弹）——
    ///   首生与 Despawn 后的复活同一条规则自动生效（Despawn 清武器行，见 <see cref="SimWorldState.Despawn"/>）；</item>
    /// <item><b>换弹推进</b>：整数帧倒计（<c>ReloadEndFrame</c>，禁浮点倒计时）；到帧转移弹药回 Ready；</item>
    /// <item><b>换弹请求</b>：离散位 <see cref="SimInputFrame.ButtonReload"/>（边沿帧才会到；seq 由 InputGate 去重）。</item>
    /// </list>
    /// **开火资源与节拍**由 ShootingSystem 经 <see cref="TryConsumeShot"/> 消费（判定点单源不变）——
    /// 本系统不开火、不算伤害。**未装备实体**（测试/沙盒直调 ShootingSystem 的形态）不经武器门：
    /// ShootingSystem 维持旧行为（逐帧 + 兜底伤害），装备实体才受节拍/弹匣/换弹约束。
    /// **确定性**：全整数帧运算；懒装备只读配置、不回读时钟/输入。
    /// </summary>
    public static class WeaponSystem
    {
        public static void Run(SimWorldState s, SimInputFrame[] inputs)
        {
            // ① 懒装备（先于输入/换弹——本帧装备、本帧即可开火，省一帧延迟）
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((s.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                ref EntitySlot e = ref s.Entities[i];
                if (e.Hp <= 0) continue;                                  // 尸体不装备
                ref WeaponRuntime w = ref s.Weapons[i * SimConfig.WeaponSlotsPerEntity];
                if (w.State != WeaponSlotState.Unequipped) continue;      // 已装备形态不动（含换弹/切枪中） // lint-allow R3（枚举判等，非浮点精度比较）
                EquipDefault(ref e, ref w);
            }

            // ② 换弹到帧完成（全槽位推进——与输入无关，整数帧确定性）
            int frame = s.Frame;
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((s.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                ref WeaponRuntime w = ref s.Weapons[i * SimConfig.WeaponSlotsPerEntity];
                if (w.State != WeaponSlotState.Reloading || frame < w.ReloadEndFrame) continue;   // lint-allow R3（枚举/整型判等）
                CompleteReload(ref w);
            }

            // ③ 换弹请求（离散边沿；同帧重复请求自然被 State 条件吸收）
            for (int i = 0; i < inputs.Length; i++)
            {
                if ((inputs[i].Buttons & SimInputFrame.ButtonReload) == 0u) continue;
                if (!s.TryResolve(inputs[i].EntityId, out int slot)) continue;
                ref EntitySlot e = ref s.Entities[slot];
                if (e.Hp <= 0) continue;
                if (!IsEquipped(s, slot, out WeaponDef def, out int wi)) continue;
                ref WeaponRuntime w = ref s.Weapons[wi];
                TryStartReload(ref w, in def, frame);
            }
        }

        /// <summary>默认步枪满弹装备（懒装备单点）。</summary>
        private static void EquipDefault(ref EntitySlot e, ref WeaponRuntime w)
        {
            WeaponDef def = WeaponConfig.Default;
            e.SelectedWeapon = WeaponConfig.DefaultRifleId;
            w.WeaponDefId = def.Id;
            w.MagAmmo = def.MagazineSize;
            w.ReserveAmmo = def.ReserveAmmo;
            w.NextFireFrame = 0;
            w.ReloadEndFrame = 0;
            w.EquipEndFrame = 0;
            w.ShotSeq = 0;
            w.State = WeaponSlotState.Ready;
        }

        /// <summary>本槽选中武器是否已装备（State ≠ Unequipped、槽位合法、表行在册）。</summary>
        public static bool IsEquipped(SimWorldState s, int slotIndex, out WeaponDef def, out int weaponIndex)
        {
            weaponIndex = -1;
            def = default;
            ref EntitySlot e = ref s.Entities[slotIndex];
            int sel = e.SelectedWeapon;
            if (sel < 0 || sel >= SimConfig.WeaponSlotsPerEntity) return false;
            int wi = slotIndex * SimConfig.WeaponSlotsPerEntity + sel;
            ref WeaponRuntime w = ref s.Weapons[wi];
            if (w.State == WeaponSlotState.Unequipped) return false;   // lint-allow R3（枚举判等，非浮点精度比较）
            if (!WeaponConfig.TryGet(w.WeaponDefId, out def)) return false;
            weaponIndex = wi;
            return true;
        }

        /// <summary>
        /// 开火消费（ShootingSystem 判定点调用）：Ready ∧ 节拍到帧 ∧ 有弹 → 扣弹、推进 <see cref="WeaponRuntime.NextFireFrame"/>、
        /// <see cref="WeaponRuntime.ShotSeq"/>++，返回 true 并给武器定义；否则返回 false（该帧不开火——无 Fire 事件、无 RngState 消费）。
        /// </summary>
        public static bool TryConsumeShot(SimWorldState s, int slotIndex, int frame, out WeaponDef def)
        {
            if (!IsEquipped(s, slotIndex, out def, out int wi)) return false;
            ref WeaponRuntime w = ref s.Weapons[wi];
            if (w.State != WeaponSlotState.Ready) return false;           // 换弹/切枪中不可开火 // lint-allow R3（枚举判等）
            if (frame < w.NextFireFrame) return false;                    // 射速节拍
            if (w.MagAmmo <= 0) return false;                             // 打空（不自动换弹——归输入）
            w.MagAmmo--;
            w.NextFireFrame = frame + def.FireIntervalFrames;
            w.ShotSeq++;
            return true;
        }

        private static void TryStartReload(ref WeaponRuntime w, in WeaponDef def, int frame)
        {
            if (w.State != WeaponSlotState.Ready) return;                 // lint-allow R3（枚举判等）
            if (w.MagAmmo >= def.MagazineSize) return;                    // 满弹不换
            if (w.ReserveAmmo <= 0) return;                               // 无备弹不换
            w.State = WeaponSlotState.Reloading;
            w.ReloadEndFrame = frame + def.ReloadFrames;
        }

        private static void CompleteReload(ref WeaponRuntime w)
        {
            if (WeaponConfig.TryGet(w.WeaponDefId, out WeaponDef def))
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
    }
}
