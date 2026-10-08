using System;
using System.IO;
using LiteSim;

namespace RoomServer
{
    /// <summary>
    /// 服务端玩法数值/武器表装载（《玩法数值与Luban配置专项设计》）：
    /// 服务端跑**权威 Sim**，必须与客户端拿到**同一份手感数值与武器定义**——否则同一份输入两端算出
    /// 不同结果，表现为和解风暴。
    ///
    /// **主源链路**：本类直读**客户端同一份 .bytes**（`Assets/GameData/Config/*.bytes`，gen.bat Pass 1 产出；
    /// 缺省走 `LoadFromRepo` 仓库路径，`--combat-table <目录>` 显式覆盖）。两端同代码（生成物源链接共编）
    /// 同数据（同一份二进制），物理上不可能漂移。
    ///
    /// **装载即产出实例**：`LoadTableBytes`/`LoadFromRepo` 返回 <see cref="ServerTableLoad"/>
    /// （数值行 `ToValues()` → <see cref="CombatValues"/> 实例；武器行 → <see cref="WeaponTable"/> 实例）——
    /// 调用方**显式传入**宿主/房间（技术债 #1：不再回填全局静态面）。装载与传递是同一契约的两半，
    /// 丢弃返回值即"服务端跑默认值"分叉的复辟面；结构性防线 = `HostAssembly.Inputs` 必填校验（缺即拒装配）。
    /// <see cref="Parse"/> 保持纯解析。L1 钉子：`CombatNumbersTests`/`WeaponTableTests`（装载产物 = 表值实例）。
    ///
    /// **一致性双保险**：① 两端数值同源（同一 .bytes）② 表数据进 buildHash
    /// （`scripts/codegen/gen-build-hash.py`），版本不一致直接在 Join 握手被拒。
    /// </summary>
    public static class CombatNumbers
    {
        /// <summary>客户端表数据目录（相对仓库根；gen.bat Pass 1 产出，两端共用同一份文件）。</summary>
        public const string RelativeDir = "Assets/GameData/Config";

        public const int SingleRowId = 1;   // 单行表固定 id

        /// <summary>
        /// 从仓库根装载客户端 .bytes 表目录（定位失败或缺表 → 抛，fail-fast）；返回装载实例。
        /// 数值错了必然分叉——宁可起不来，也不要带着错数值跑权威局。
        /// </summary>
        public static ServerTableLoad LoadFromRepo()
        {
            string root = FindRepoRoot()
                          ?? throw new InvalidOperationException(
                              $"找不到仓库根（需含 Assets 与 Tests/Tests.slnx）——无法装载 {RelativeDir}");
            return LoadTableBytes(Path.Combine(root, RelativeDir.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>
        /// 从指定表目录装载并返回**装载实例**（表目录 = 客户端 GameData/Config；表清单与客户端
        /// <c>ConfigService.TableDataFiles</c> 同源——Tables 构造器逐表取字节）。
        /// </summary>
        public static ServerTableLoad LoadTableBytes(string configDir)
        {
            if (!Directory.Exists(configDir))
                throw new DirectoryNotFoundException($"表数据目录缺失：{configDir}（跑 Luban/gen.bat Pass 1）");

            var tables = new cfg.Tables(file =>
            {
                string path = Path.Combine(configDir, file + ".bytes");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"配置表缺失：{path}（跑 Luban/gen.bat Pass 1——数值缺失等于两端分叉）", path);
                return new Luban.ByteBuf(File.ReadAllBytes(path));
            });

            var values = ToValues(tables.Tbcombatnum.Get(SingleRowId)
                                  ?? throw new InvalidDataException($"tbcombatnum 缺 id={SingleRowId} 行（单行数值表）"));
            Console.WriteLine(
                $"[RoomServer] 玩法数值（bin 表）：move={values.MoveSpeed} gravity={values.Gravity} " +
                $"hitscan={values.HitscanRange} hit={CombatConfig.HitscanRadius}(裁决) body={CombatConfig.BodyRadius}:{CombatConfig.HitscanHeight}(烘焙) " +
                $"dmg={values.BaseDamage}±{values.DamageSpread} hp={values.EntityHp}");

            WeaponTable weapons = BuildWeaponTable(tables);
            // 装载产物经返回值交给组合根显式传递（技术债 #1：不再回填全局静态面）。
            return new ServerTableLoad(values, weapons);
        }

        /// <summary>
        /// 纯解析（可测）：tbcombatnum.bytes 字节 → 数值。坏格式/截断 → 抛
        /// （不返回默认值——静默兜底会让"表没生成"变成"跑着默认值"的隐形分叉）。
        /// 单表直读（不构造 Tables——那是全表装载器，顺序依赖没必要带进解析面）。
        /// </summary>
        public static CombatNumValues Parse(byte[] combatnumBytes)
        {
            var table = new cfg.Tbcombatnum(new Luban.ByteBuf(combatnumBytes));
            var row = table.Get(SingleRowId)
                      ?? throw new InvalidDataException($"tbcombatnum.bytes 缺 id={SingleRowId} 行（单行数值表）");
            return ToValues(row);
        }

        /// <summary>表行 → 数值载体（<see cref="Parse"/> 与 <see cref="LoadTableBytes"/> 共用一份映射）。</summary>
        private static CombatNumValues ToValues(cfg.combatnum row) => new CombatNumValues
        {
            Id = row.Id,
            MoveSpeed = row.MoveSpeed,
            Gravity = row.Gravity,
            HitscanRange = row.HitscanRange,
            BaseDamage = row.BaseDamage,
            DamageSpread = row.DamageSpread,
            EntityHp = row.EntityHp,
        };

        /// <summary>武器表装载（服务端与客户端同链——权威 Sim 的武器系统必须拿到同一份武器定义；
        /// 缺默认步枪行即抛，fail-fast 与 tbcombatnum 同纪律）。产出 <see cref="WeaponTable"/> 实例。</summary>
        private static WeaponTable BuildWeaponTable(cfg.Tables tables)
        {
            if (tables.Tbweapon == null || tables.Tbweapon.GetOrDefault(WeaponConfig.DefaultRifleId) == null)
                throw new InvalidDataException("tbweapon 缺默认步枪行（id=0）——武器系统懒装备依赖它");

            var table = new WeaponTable();
            int count = 0;
            foreach (cfg.weapon row in tables.Tbweapon.DataList)
            {
                bool automatic = row.FireMode == "auto";
                if (table.SetRow(row.Id, row.Damage, row.Rpm, row.MagazineSize, row.ReserveAmmo,
                        row.ReloadFrames, row.Range, row.Spread, row.Pellets, row.SwitchFrames, automatic))
                    count++;
            }
            if (!table.TryGet(WeaponConfig.DefaultRifleId, out WeaponDef rifle))
                throw new InvalidDataException("tbweapon 默认步枪行未入表（id 越界？）");
            Console.WriteLine(
                $"[RoomServer] 武器表装载：{count} 行（默认步枪 dmg={rifle.Damage} rpm={rifle.Rpm} " +
                $"mag={rifle.MagazineSize} 换弹={rifle.ReloadFrames}帧 节拍={rifle.FireIntervalFrames}帧）");
            return table;
        }

        /// <summary>仓库根定位：与测试侧同款标记（Assets + Tests/Tests.slnx），从程序目录向上找。</summary>
        internal static string FindRepoRoot()
        {
            for (var cur = new DirectoryInfo(AppContext.BaseDirectory); cur != null; cur = cur.Parent)
            {
                if (Directory.Exists(Path.Combine(cur.FullName, "Assets"))
                    && File.Exists(Path.Combine(cur.FullName, "Tests", "Tests.slnx")))
                    return cur.FullName;
            }
            return null;
        }
    }

    /// <summary>装载产物（数值行 + 武器表实例；由装配方持有并显式传递——无静态面）。</summary>
    public readonly struct ServerTableLoad
    {
        /// <summary>玩法数值行（`ToValues()` → <see cref="CombatValues"/> 实例）。</summary>
        public readonly CombatNumValues Combat;

        /// <summary>武器表实例（tb_weapon 行）。</summary>
        public readonly WeaponTable Weapons;

        public ServerTableLoad(CombatNumValues combat, WeaponTable weapons)
        {
            Combat = combat;
            Weapons = weapons;
        }
    }

    /// <summary>表行数值（纯数据载体；<see cref="ToValues"/> 产出 <see cref="CombatValues"/> 实例）。</summary>
    public struct CombatNumValues
    {
        public int Id;
        public float MoveSpeed;
        public float Gravity;
        public float HitscanRange;
        public int BaseDamage;
        public int DamageSpread;
        public int EntityHp;

        /// <summary>表行 → 玩法数值实例（装载产物；由调用方**显式传入**宿主/房间——
        /// 技术债 #1：不再回填全局静态面）。</summary>
        public CombatValues ToValues()
        {
            return new CombatValues(MoveSpeed, Gravity, HitscanRange, BaseDamage, DamageSpread, EntityHp);
        }
    }
}
