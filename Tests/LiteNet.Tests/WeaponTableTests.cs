using System;
using System.IO;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 武器表链路守卫（tb_weapon ↔ WeaponConfig，两端同源纪律同 <see cref="CombatNumbersTests"/>）：
    /// ① 表产物存在且**默认步枪行与内置默认逐字段一致**（表未装载时的兜底不得漂移）；
    /// ② 服务端装载链（<see cref="CombatNumbers.LoadTableBytes"/>）回填后 WeaponConfig 读到表值。
    /// 触碰 CombatConfig 全局静态（装载即回填的副作用）→ 同集合禁并行。
    /// </summary>
    [Collection("CombatConfigStatic")]
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

            WeaponDef def = WeaponConfig.Default;
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
        public void 服务端装载链_回填后读到表值()
        {
            string dir = Path.Combine(RepoRoot(), "Assets", "GameData", "Config");
            CombatNumbers.LoadTableBytes(dir);            // 装载即回填（含武器表）

            Assert.True(WeaponConfig.TryGet(WeaponConfig.DefaultRifleId, out WeaponDef def), "默认步枪行应在册");
            Assert.True(def.FireIntervalFrames > 0, "节拍帧应为正（ceil(TickRate×60/rpm)）");
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
