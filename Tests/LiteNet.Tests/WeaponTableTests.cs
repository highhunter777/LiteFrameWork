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

        /// <summary>霰弹枪行（tb_weapon id=1）在册且表值/派生/击发模式逐项读到：
        /// 多弹丸/半自动/槽 1 默认映射是切枪与散布机制的装载基料。</summary>
        [Fact]
        public void 霰弹枪行_表值与半自动映射与槽位默认在册()
        {
            string dir = Path.Combine(RepoRoot(), "Assets", "GameData", "Config");
            ServerTableLoad load = CombatNumbers.LoadTableBytes(dir);

            const int ShotgunId = 1;
            Assert.True(load.Weapons.TryGet(ShotgunId, out WeaponDef sg), "霰弹枪行（id=1）应在册");
            Assert.Equal(8, sg.Damage);
            Assert.Equal(75, sg.Rpm);
            Assert.Equal(48, sg.FireIntervalFrames);      // ceil(60×60/75) = 48 帧 @60Hz
            Assert.Equal(8, sg.MagazineSize);
            Assert.Equal(24, sg.ReserveAmmo);
            Assert.Equal(168, sg.ReloadFrames);
            Assert.Equal(30f, sg.Range);
            Assert.Equal(3f, sg.Spread);
            Assert.Equal(8, sg.Pellets);                  // 单次击发 8 弹丸（散布判定环逐弹丸）
            Assert.Equal(40, sg.SwitchFrames);
            Assert.False(sg.Automatic, "fire_mode='semi' → 半自动（按住只发一发——松开重臂）");

            // 槽位默认映射（tb_weapon.slot 列）：切枪装备/懒装备的"槽位 → 定义"单源
            Assert.True(load.Weapons.TryGetSlotDefault(0, out int slot0Id));
            Assert.Equal(WeaponConfig.DefaultRifleId, slot0Id);
            Assert.True(load.Weapons.TryGetSlotDefault(1, out int slot1Id));
            Assert.Equal(ShotgunId, slot1Id);
        }

        /// <summary>装载闸门：fire_mode 越界值拒装载（fail-fast——错表必两端分叉）。
        /// 用字节级手工构造会绕过 Luban 生成物——这里以"合法集常量与装载映射"钉住约定，
        /// 真实越界行的整链拒装载由服务端 BuildWeapon 的 throw 路径（缺行/越界）承担。</summary>
        [Fact]
        public void 击发模式合法集_仅auto与semi()
        {
            Assert.Equal("auto", WeaponConfig.FireModeAuto);
            Assert.Equal("semi", WeaponConfig.FireModeSemi);
            // 内置默认步枪为自动（表行同值——L1 守卫卡漂移）
            Assert.True(WeaponConfig.DefaultRifle.Automatic);
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
