using System;
using System.IO;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 武器表链路守卫（tb_weapon ↔ <see cref="WeaponTable"/> 实例，两端同源纪律同 <see cref="CombatNumbersTests"/>）：
    /// ① 表产物存在且**默认步枪行与内置默认逐字段一致**（表未装载时的兜底不得漂移）；
    /// ② 服务端装载链（<see cref="CombatNumbers.LoadTableBytes"/>）产出的 <see cref="WeaponTable"/> 实例在册读到表值。
    /// 数值/武器表经实例传递（技术债 #1）——本类不触碰任何全局面，无串行集/还原需求。
    /// </summary>
    public sealed class WeaponTableTests
    {
        private const string TableRelativePath = "Assets/GameData/Config/tbweapon.bytes";

        [Fact]
        public void 表文件存在且默认步枪行与内置默认一致()
        {
            string path = Path.Combine(RepoRoot(), TableRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"缺武器表产物：{TableRelativePath}（跑 Luban/gen.bat Pass 1——武器定义缺失等于两端分叉）");

            var table = new cfg.Tbweapon(new Luban.ByteBuf(File.ReadAllBytes(path)));
            cfg.weapon row = table.GetOrDefault(WeaponConfig.DefaultRifleId);
            Assert.NotNull(row);

            // 兜底面 = WeaponTable.Default（内置默认步枪单行表）；必须与表行逐字段一致（漂移即 L1 红）
            Assert.True(WeaponTable.Default.TryGet(WeaponConfig.DefaultRifleId, out WeaponDef def));
            Assert.Equal(def.Damage, row.Damage);
            Assert.Equal(def.Rpm, row.Rpm);
            Assert.Equal(def.MagazineSize, row.MagazineSize);
            Assert.Equal(def.ReserveAmmo, row.ReserveAmmo);
            Assert.Equal(def.ReloadFrames, row.ReloadFrames);
            Assert.Equal(def.Range, row.Range);          // float 表值 = 常量（同字面量，无运算）
            Assert.Equal(def.Spread, row.Spread);
            Assert.Equal(def.Pellets, row.Pellets);
            Assert.Equal(def.SwitchFrames, row.SwitchFrames);
        }

        [Fact]
        public void 服务端装载链产出实例_表值在册()
        {
            string dir = Path.Combine(RepoRoot(), "Assets", "GameData", "Config");
            ServerTableLoad load = CombatNumbers.LoadTableBytes(dir);   // 装载产出实例（数值行 + 武器表）

            Assert.True(load.Weapons.TryGet(WeaponConfig.DefaultRifleId, out WeaponDef def), "默认步枪行应在册");
            Assert.True(def.FireIntervalFrames > 0, "节拍帧应为正（ceil(TickRate×60/rpm)）");
            Assert.True(load.Weapons.LoadedCount >= 1, "至少默认步枪行");
            // 数值行经 ToValues() 同链可得（与武器表同一次装载——同一契约的两半）
            CombatValues values = load.Combat.ToValues();
            Assert.Equal(load.Combat.EntityHp, values.EntityHp);
        }

        private static string RepoRoot()
        {
            for (var c = new DirectoryInfo(AppContext.BaseDirectory); c != null; c = c.Parent)
                if (Directory.Exists(Path.Combine(c.FullName, "Tests"))
                    && File.Exists(Path.Combine(c.FullName, "Tests", "Tests.slnx")))
                    return c.FullName;
            throw new DirectoryNotFoundException("找不到仓库根（需要 Tests/Tests.slnx）");
        }
    }
}
