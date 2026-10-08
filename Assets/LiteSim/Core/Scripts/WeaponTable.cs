namespace LiteSim
{
    /// <summary>
    /// 武器表实例（tb_weapon 装载产物；技术债 #1 家族——与 <see cref="CombatValues"/> 同规：
    /// 装载链产出实例、经参数显式传入机制消费，**无静态可变存储**）。
    ///
    /// - 定容行表（id ∈ [0, <see cref="MaxRows"/>)，越界行装载期忽略并返回 false——行存在性
    ///   （默认步枪 id=0）由装载链 fail-fast 把守；
    /// - **装载期填充、此后按约定只读**（实例经参数/装配输入传递；机制禁读静态读口——纪律扫描 R13）；
    /// - <see cref="Default"/> = 内置默认步枪单行表（未装载兜底——与 tb_weapon 同值，L1 守卫卡漂移）。
    /// </summary>
    public sealed class WeaponTable
    {
        /// <summary>行容量。</summary>
        public const int MaxRows = 16;

        private readonly WeaponDef[] _defs = new WeaponDef[MaxRows];
        private readonly bool[] _has = new bool[MaxRows];
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

        /// <summary>内置默认表（仅默认步枪一行；未装载兜底——**属性返回新实例**，防共享可变存储）。</summary>
        public static WeaponTable Default
        {
            get
            {
                var table = new WeaponTable();
                WeaponDef d = WeaponConfig.DefaultRifle;
                table.SetRow(d.Id, d.Damage, d.Rpm, d.MagazineSize, d.ReserveAmmo, d.ReloadFrames,
                    d.Range, d.Spread, d.Pellets, d.SwitchFrames, d.Automatic);
                return table;
            }
        }
    }
}
