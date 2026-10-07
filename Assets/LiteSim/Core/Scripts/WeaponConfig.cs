namespace LiteSim
{
    /// <summary>武器定义（表驱——《游戏业务系统总设计》§5.2 tb_weapon 的 Sim 侧投影；纯 primitives）。</summary>
    public readonly struct WeaponDef
    {
        public readonly int Id;
        /// <summary>单发伤害（爆头 ×2 在命中判定处应用；±浮动走 CombatConfig.DamageSpread）。</summary>
        public readonly int Damage;
        public readonly int Rpm;
        public readonly int MagazineSize;
        public readonly int ReserveAmmo;
        public readonly int ReloadFrames;
        public readonly float Range;
        /// <summary>散布（度——瞄准收窄；实现后置，当前仅装载）。</summary>
        public readonly float Spread;
        /// <summary>单次击发弹丸数（霰弹枪 &gt;1；步枪 =1）。</summary>
        public readonly int Pellets;
        public readonly int SwitchFrames;
        /// <summary>击发节拍（帧，@TickRate）：<c>ceil(TickRate × 60 / rpm)</c>——整数帧，禁浮点倒计时。
        /// （rpm = 发/分 ⇒ 每发间隔 = 60/rpm 秒 ⇒ ×TickRate 帧；600rpm @60Hz ⇒ 6 帧。）</summary>
        public readonly int FireIntervalFrames;

        /// <summary>自动击发（按住连发）；false = 半自动（按住只发一发——边沿门后续补）。</summary>
        public readonly bool Automatic;

        public WeaponDef(int id, int damage, int rpm, int magazineSize, int reserveAmmo, int reloadFrames,
            float range, float spread, int pellets, int switchFrames, bool automatic)
        {
            Id = id;
            Damage = damage;
            Rpm = rpm;
            MagazineSize = magazineSize;
            ReserveAmmo = reserveAmmo;
            ReloadFrames = reloadFrames;
            Range = range;
            Spread = spread;
            Pellets = pellets;
            SwitchFrames = switchFrames;
            FireIntervalFrames = (SimConfig.TickRate * 60 + rpm - 1) / rpm;   // ceil(TickRate×60/rpm)：600rpm @60Hz → 6 帧
            Automatic = automatic;
        }
    }

    /// <summary>
    /// 武器表装载面（《玩法数值与 Luban 配置专项设计》：玩法数值进表、Sim 只接受已解析的 primitive 值）。
    /// 两端同链：客户端 `ConfigService` / 服务端 `CombatNumbers` 读**同一份** tbweapon.bytes 逐行回填
    /// （L1 守卫卡"默认值 = 表值"漂移）。**默认表内置步枪**（表未装载时的兜底——与 tb_weapon.xlsx 同值）。
    /// </summary>
    public static class WeaponConfig
    {
        /// <summary>默认步枪 id（懒装备与兜底用）。</summary>
        public const int DefaultRifleId = 0;

        /// <summary>内置默认（= tb_weapon.xlsx 步枪行；L1 守卫卡漂移）。</summary>
        private static readonly WeaponDef DefaultRifle =
            new WeaponDef(id: 0, damage: 16, rpm: 600, magazineSize: 30, reserveAmmo: 90,
                reloadFrames: 132, range: 100f, spread: 1.2f, pellets: 1, switchFrames: 30, automatic: true);

        private static readonly WeaponDef[] _defs = new WeaponDef[16];
        private static readonly bool[] _has = new bool[16];

        static WeaponConfig()
        {
            _defs[DefaultRifleId] = DefaultRifle;
            _has[DefaultRifleId] = true;
        }

        /// <summary>表行回填（逐行；重复 id 覆盖）。id 越界（&lt;0 或 ≥16）忽略并返回 false。</summary>
        public static bool SetRow(int id, int damage, int rpm, int magazineSize, int reserveAmmo, int reloadFrames,
            float range, float spread, int pellets, int switchFrames, bool automatic)
        {
            if (id < 0 || id >= _defs.Length) return false;
            _defs[id] = new WeaponDef(id, damage, rpm, magazineSize, reserveAmmo, reloadFrames,
                range, spread, pellets, switchFrames, automatic);
            _has[id] = true;
            return true;
        }

        /// <summary>查武器定义（未装载的 id → false；默认步枪恒在册）。</summary>
        public static bool TryGet(int id, out WeaponDef def)
        {
            if (id >= 0 && id < _defs.Length && _has[id])
            {
                def = _defs[id];
                return true;
            }
            def = default;
            return false;
        }

        /// <summary>内置默认步枪定义（懒装备/兜底单源）。</summary>
        public static WeaponDef Default => DefaultRifle;
    }
}
