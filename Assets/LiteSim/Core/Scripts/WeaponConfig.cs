namespace LiteSim
{
    /// <summary>武器定义（表驱——《游戏业务系统总设计》§5.2 tb_weapon 的 Sim 侧投影；纯 primitives）。</summary>
    public readonly struct WeaponDef
    {
        public readonly int Id;
        /// <summary>单发伤害（爆头 ×2 在命中判定处应用；±浮动走装载实例的 DamageSpread）。</summary>
        public readonly int Damage;
        public readonly int Rpm;
        public readonly int MagazineSize;
        public readonly int ReserveAmmo;
        public readonly int ReloadFrames;
        public readonly float Range;
        /// <summary>散布（度）＝**瞄准态最大偏转角**（腰射 = 表值 × <see cref="CombatConfig.HipSpreadFactor"/>）；
        /// 偏转几何单源 <see cref="SimSpread"/>，随机消费在 <see cref="ShootingSystem"/>（每弹丸 2 笔）。</summary>
        public readonly float Spread;
        /// <summary>单次击发弹丸数（霰弹枪 &gt;1；步枪 =1）。</summary>
        public readonly int Pellets;
        public readonly int SwitchFrames;
        /// <summary>击发节拍（帧，@TickRate）：<c>ceil(TickRate × 60 / rpm)</c>——整数帧，禁浮点倒计时。
        /// （rpm = 发/分 ⇒ 每发间隔 = 60/rpm 秒 ⇒ ×TickRate 帧；600rpm @60Hz ⇒ 6 帧。）</summary>
        public readonly int FireIntervalFrames;

        /// <summary>自动击发（按住连发）；false = 半自动（按住只发一发——击发沿门
        /// <see cref="EntitySlot.SemiFireArmed"/>：松开帧重臂、击发消费即解除、冷却内按压同样消费）。</summary>
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
    /// 武器常量与读口（技术债 #1 家族收尾——静态可变装载面已拆除）：
    ///
    /// - **机制消费经参数接收 <see cref="WeaponTable"/> 实例**（`SimStep`→`WeaponSystem`/`ShootingSystem`；
    ///   房间快照 `FixedCombatConfig` 携带；回溯/回放/纯驱动同链）；
    /// - 本类只留**编译期常量**（<see cref="DefaultRifleId"/>、内置默认步枪 <see cref="DefaultRifle"/>）
    ///   与**客户端单世界读口**（<see cref="Publish"/>/<see cref="Loaded"/> 及兼容便捷读
    ///   <see cref="Default"/>/<see cref="TryGet"/>——表现面读，**机制禁读：纪律扫描 R13 把守**）；
    /// - "内置默认 = 表值"漂移由 L1 守卫卡（<see cref="WeaponTable"/> 装载产物同在册面同钉）。
    /// </summary>
    public static class WeaponConfig
    {
        /// <summary>默认步枪 id（懒装备与兜底用——**编译期常量，机制可读**）。</summary>
        public const int DefaultRifleId = 0;

        /// <summary>击发模式表值：自动（按住连发——节拍门内逐发消费）。</summary>
        public const string FireModeAuto = "auto";

        /// <summary>击发模式表值：半自动（按住只发一发——扳机松开重臂，<see cref="WeaponDef.Automatic"/> = false）。
        /// 合法集即 <see cref="FireModeAuto"/>/<see cref="FireModeSemi"/>——装载链据此映射 bool，越界值 fail-fast 拒装载。</summary>
        public const string FireModeSemi = "semi";

        /// <summary>内置默认步枪定义（= tb_weapon.xlsx 步枪行；未装载兜底，L1 守卫卡漂移。**不可变常量面**）。</summary>
        public static readonly WeaponDef DefaultRifle =
            new WeaponDef(id: 0, damage: 16, rpm: 600, magazineSize: 30, reserveAmmo: 90,
                reloadFrames: 132, range: 100f, spread: 1.2f, pellets: 1, switchFrames: 30, automatic: true);

        private static WeaponTable _loaded = WeaponTable.Default;

        /// <summary>发布装载表（**客户端单世界读口**的原子替换；服务端**不经本槽**——
        /// 装载产物经 `HostAssembly.Inputs` 显式传入）。</summary>
        public static void Publish(WeaponTable table) { _loaded = table; }

        /// <summary>当前装载表（表现/工具读口；**机制消费禁读**——经 <see cref="WeaponTable"/> 参数接收）。</summary>
        public static WeaponTable Loaded => _loaded;

        /// <summary>兼容读口：默认步枪定义（表现面/工具便捷读；未在册回落内置默认，同值）。</summary>
        public static WeaponDef Default => _loaded.TryGet(DefaultRifleId, out WeaponDef d) ? d : DefaultRifle;

        /// <summary>兼容读口：查武器定义（表现面/工具便捷读；机制用传入表的 <see cref="WeaponTable.TryGet"/>）。</summary>
        public static bool TryGet(int id, out WeaponDef def) => _loaded.TryGet(id, out def);
    }
}
