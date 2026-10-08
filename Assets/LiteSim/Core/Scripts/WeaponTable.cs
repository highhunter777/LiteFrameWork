namespace LiteSim
{
    /// <summary>
    /// 武器表实例（tb_weapon 装载产物；技术债 #1 家族——与 <see cref="CombatValues"/> 同规：
    /// 装载链产出实例、经参数显式传入机制消费，**无静态可变存储**）。
    ///
    /// - 定容行表（id ∈ [0, <see cref="MaxRows"/>)，越界行装载期忽略并返回 false——行存在性
    ///   （默认步枪 id=0）由装载链 fail-fast 把守；
    /// - **槽位默认映射**（tb_weapon <c>slot</c> 列 → 武器 id，<see cref="SetSlotDefault"/>）：
    ///   切枪装备与懒装备的"槽位 → 武器定义"唯一来源——切到未装备槽/首生懒装备都经它取定义，
    ///   不用"id 恒等槽位"约定（表行 id 与槽位号解耦，换表不破坏机制）；
    /// - **装载期填充、此后按约定只读**（实例经参数/装配输入传递；机制禁读静态读口——纪律扫描 R13）；
    /// - <see cref="Default"/> = 内置默认步枪单行表（未装载兜底——与 tb_weapon 同值，L1 守卫卡漂移）。
    /// </summary>
    public sealed class WeaponTable
    {
        /// <summary>行容量。</summary>
        public const int MaxRows = 16;

        private readonly WeaponDef[] _defs = new WeaponDef[MaxRows];
        private readonly bool[] _has = new bool[MaxRows];
        private readonly int[] _slotDefaults = new int[SimConfig.WeaponSlotsPerEntity];
        private readonly bool[] _slotHasDefault = new bool[SimConfig.WeaponSlotsPerEntity];
        private int _count;

        /// <summary>已装载行数（诊断/断言用）。</summary>
        public int LoadedCount => _count;

        /// <summary>行回填（装载期唯一写入窗；重复 id 覆盖）。id 越界忽略并返回 false。</summary>
        public bool SetRow(int id, int damage, int rpm, int magazineSize, int reserveAmmo, int reloadFrames,
            float range, float spread, int pellets, int switchFrames, bool automatic)
        {
            if (id < 0 || id >= MaxRows) return false;
            _defs[id] = new WeaponDef(id, damage, rpm, magazineSize, reserveAmmo, reloadFrames,
                range, spread, pellets, switchFrames, automatic);
            _has[id] = true;
            _count++;
            return true;
        }

        /// <summary>槽位默认武器注册（tb_weapon <c>slot</c> 列 → 武器 id；装载期与 <see cref="SetRow"/> 同窗）。
        /// **first-wins**：同槽多行取装载序首行（表字节固定 ⇒ 装载序确定）。槽位越界/目标行未在册 → 拒绝（false）。</summary>
        public bool SetSlotDefault(int slot, int weaponId)
        {
            if (slot < 0 || slot >= SimConfig.WeaponSlotsPerEntity) return false;
            if (weaponId < 0 || weaponId >= MaxRows || !_has[weaponId]) return false;
            if (_slotHasDefault[slot]) return false;
            _slotDefaults[slot] = weaponId;
            _slotHasDefault[slot] = true;
            return true;
        }

        /// <summary>查槽位默认武器 id（切枪装备/懒装备的"槽位 → 定义"单源；未注册槽 → false）。</summary>
        public bool TryGetSlotDefault(int slot, out int weaponId)
        {
            if (slot >= 0 && slot < SimConfig.WeaponSlotsPerEntity && _slotHasDefault[slot])
            {
                weaponId = _slotDefaults[slot];
                return true;
            }
            weaponId = -1;
            return false;
        }

        /// <summary>查武器定义（未装载的 id → false）。</summary>
        public bool TryGet(int id, out WeaponDef def)
        {
            if (id >= 0 && id < MaxRows && _has[id])
            {
                def = _defs[id];
                return true;
            }
            def = default;
            return false;
        }

        /// <summary>内置默认表（仅默认步枪一行；未装载兜底——**属性返回新实例**，防共享可变存储）。
        /// 槽位默认映射同步注册（槽 0 → 步枪），与装载链同构。</summary>
        public static WeaponTable Default
        {
            get
            {
                var table = new WeaponTable();
                WeaponDef d = WeaponConfig.DefaultRifle;
                table.SetRow(d.Id, d.Damage, d.Rpm, d.MagazineSize, d.ReserveAmmo, d.ReloadFrames,
                    d.Range, d.Spread, d.Pellets, d.SwitchFrames, d.Automatic);
                table.SetSlotDefault(0, d.Id);
                return table;
            }
        }
    }
}
