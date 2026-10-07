using System.Collections.Generic;
using System.IO;
using LiteGame;            // ConfigService（表数据预取清单）
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using Luban;
using NUnit.Framework;
using UnityEngine;
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 客户端玩法数值链路（《玩法数值解耦审查与Luban表设计》§4）：
    /// 直接按 `ConfigService` 的同一路径与同一 loader 建 `cfg.Tables` → 取 `Tbcombatnum` 单行 →
    /// 与 `CombatConfig` 的运行时值比对。**验的是真实数据链路**（bin 文件存在且能被 Luban 解析），
    /// 而不是"代码里写了个数"。
    ///
    /// 为什么不复用 `ConfigService.LoadAsync`：它走 YooAsset 异步 + 流程前置，EditMode 里拉不起来；
    /// 这里用同一份 `TableDataFiles` 清单直读磁盘，等价且更快（清单一致性由第二个用例保证）。
    /// </summary>
    public sealed class CombatNumbersEditModeTests : UnityTestBase
    {
        private static readonly string[] DataFiles =
        {
            "tbitemconfig", "tbmovementconfig", "tbuiform", "tbcontententry", "tbstrategy", "tbcombatnum",
            "tbweapon",
        };

        [Test]
        [Category(TestCategory.Contract)]
        public void 预取清单_含玩法数值表()
        {
            CollectionAssert.Contains(ConfigService.TableDataFiles, "tbcombatnum",
                "ConfigService 预取清单缺 tbcombatnum → 启动不会装载数值（表生成了也没用）");
            foreach (string f in DataFiles)
                CollectionAssert.Contains(ConfigService.TableDataFiles, f, $"预取清单缺 {f}");
        }

        [Test]
        [Category(TestCategory.Asset)]
        public void 数值表_可建表且与运行时值一致()
        {
            // 表数据目录从 ConfigService 单源取
            string dir = Path.Combine(ProjectRoot(), ConfigService.DataDir);
            var cache = new Dictionary<string, byte[]>(DataFiles.Length);
            foreach (string f in DataFiles)
            {
                string path = Path.Combine(dir, f + ".bytes");
                Assert.IsTrue(File.Exists(path), $"缺表数据：{path}（跑 Luban/gen.bat）");
                cache[f] = File.ReadAllBytes(path);
            }

            var tables = new cfg.Tables(file => new ByteBuf(cache[file]));   // 与 ConfigService 同款 loader
            cfg.combatnum row = tables.Tbcombatnum.Get(1);
            Assert.IsNotNull(row, "tbcombatnum 缺 id=1 行（单行数值表）");

            Assert.AreEqual(CombatConfig.MoveSpeed, row.MoveSpeed, 1e-6f);
            Assert.AreEqual(CombatConfig.Gravity, row.Gravity, 1e-6f);
            Assert.AreEqual(CombatConfig.HitscanRange, row.HitscanRange, 1e-6f);
            // 身位半径/高度不属表——烘焙常量（BodyBake：prefab CC 单源；表列已退役）
            Assert.AreEqual(CombatConfig.BaseDamage, row.BaseDamage);
            Assert.AreEqual(CombatConfig.DamageSpread, row.DamageSpread);
            Assert.AreEqual(CombatConfig.EntityHp, row.EntityHp);

            // 武器表（tb_weapon）与 Sim 侧运行时值一致——客户端表链路的武器面钉子
            // （数值语义的完整对账另有 LiteNet.Tests/WeaponTableTests 双端守卫）
            cfg.weapon gun = tables.Tbweapon.Get(WeaponConfig.DefaultRifleId);
            Assert.IsNotNull(gun, "tbweapon 缺默认步枪行（id=0）");
            Assert.AreEqual(WeaponConfig.Default.Damage, gun.Damage);
            Assert.AreEqual(WeaponConfig.Default.Rpm, gun.Rpm);
            Assert.AreEqual(WeaponConfig.Default.MagazineSize, gun.MagazineSize);
            Assert.AreEqual(WeaponConfig.Default.ReserveAmmo, gun.ReserveAmmo);
            Assert.AreEqual(WeaponConfig.Default.ReloadFrames, gun.ReloadFrames);
            Assert.AreEqual(WeaponConfig.Default.Range, gun.Range, 1e-6f);
            Assert.AreEqual(WeaponConfig.Default.Spread, gun.Spread, 1e-6f);
            Assert.AreEqual(WeaponConfig.Default.Pellets, gun.Pellets);
            Assert.AreEqual(WeaponConfig.Default.SwitchFrames, gun.SwitchFrames);
        }

        private static string ProjectRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath);
            for (var cur = dir; cur != null; cur = cur.Parent)
                if (Directory.Exists(Path.Combine(cur.FullName, "Assets"))
                    && File.Exists(Path.Combine(cur.FullName, "Tests", "Tests.slnx")))
                    return cur.FullName;
            throw new DirectoryNotFoundException("找不到项目根");
        }
    }
}
