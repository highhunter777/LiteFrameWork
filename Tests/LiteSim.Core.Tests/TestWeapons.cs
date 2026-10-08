using LiteSim;

namespace LiteSim.Tests
{
    /// <summary>
    /// 测试用武器表工厂：命中几何类用例的**零散布钉子**（散布行为由散布/霰弹专项用例覆盖，
    /// 不与系统链/爆头带/障碍遮挡等几何用例耦合）与多槽位用例的自定义行表构造。
    /// </summary>
    internal static class TestWeapons
    {
        /// <summary>内置默认步枪、spread=0（直射——与未装备旧路径同几何；槽 0 默认映射照常注册）。</summary>
        public static WeaponTable NoSpread()
        {
            var t = new WeaponTable();
            WeaponDef d = WeaponConfig.DefaultRifle;
            t.SetRow(d.Id, d.Damage, d.Rpm, d.MagazineSize, d.ReserveAmmo, d.ReloadFrames,
                d.Range, 0f, d.Pellets, d.SwitchFrames, d.Automatic);
            t.SetSlotDefault(0, d.Id);
            return t;
        }

        /// <summary>单行自定义表（id/slot 显式给——霰弹槽 1 / 半自动 / 多弹丸用例的装载面）。</summary>
        public static WeaponTable WithRow(int id, int damage, int rpm, int magazineSize, int reserveAmmo,
            int reloadFrames, float range, float spread, int pellets, int switchFrames, bool automatic, int slot)
        {
            var t = new WeaponTable();
            t.SetRow(id, damage, rpm, magazineSize, reserveAmmo, reloadFrames, range, spread, pellets, switchFrames, automatic);
            t.SetSlotDefault(slot, id);
            return t;
        }
    }
}
