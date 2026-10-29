using System;
using System.Collections.Generic;
using System.IO;
using LiteSim;
using LiteFramework;

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
    /// <see cref="Parse"/> 保持纯解析。L1 钉子：`ServerTableLoadTests`/`WeaponTableTests`（装载产物 = 表值实例）。
    ///
    /// **一致性守卫（批 E 起）**：① 两端数值同源（同一 .bytes）；② 判定面表字节基摘要随
    /// `ServerTableLoad.Digest` → 房间 `StartGame.ConfigHash`（+ 本局规则），客户端按本地装载字节复算比对——
    /// **不等即拒局**（此前靠"表数据进 buildHash 编译期闭包 + Join 握手"，2026-10-14 闭包重划后
    /// 数据面守卫单点归运行期摘要：数据可两端热更，守卫在装载后当场生效）。
    /// </summary>
    public static class ServerTableLoader
    {
        /// <summary>客户端表数据目录（相对仓库根；gen.bat Pass 1 产出，两端共用同一份文件）。</summary>
        public const string RelativeDir = "Assets/GameData/Config";

        public const int SingleRowId = SimConfigMapper.DefaultRowId;

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
        /// 从指定表目录装载并返回**装载实例**（表目录 = 客户端 GameData/Config 根，文件按
        /// <c>TableLayout.Files</c> 的领域相对路径取——源链接共编单源；Tables 构造器逐表取字节）。
        /// 预读缓存与客户端 <c>ConfigService.LoadAsync</c> 同形：**裸表名 → 字节**（判定面摘要与建表共用同一批字节）。
        /// </summary>
        public static ServerTableLoad LoadTableBytes(string configDir)
        {
            if (!Directory.Exists(configDir))
                throw new DirectoryNotFoundException($"表数据目录缺失：{configDir}（跑 Luban/gen.bat Pass 1）");

            var cache = new Dictionary<string, byte[]>(TableLayout.Files.Length);
            foreach (string entry in TableLayout.Files)
            {
                string path = Path.Combine(configDir, entry.Replace('/', Path.DirectorySeparatorChar) + ".bytes");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"配置表缺失：{path}（跑 Luban/gen.bat Pass 1——数值缺失等于两端分叉）", path);
                cache[TableLayout.TableName(entry)] = File.ReadAllBytes(path);
            }

            var tables = new cfg.Tables(file => new Luban.ByteBuf(cache[file]));

            var values = ToValues(SimConfigMapper.BuildCombatValues(tables.Tbcombatnum,
                tables.Tbmovementconfig, tables.Tbentityconfig));
            HostLog.Info("Tables",
                $"玩法数值（bin 表）：move={values.MoveSpeed} gravity={values.Gravity} " +
                $"hitscan={values.HitscanRange} hit={CombatConfig.HitscanRadius}(裁决) body={CombatConfig.BodyRadius}:{CombatConfig.HitscanHeight}(烘焙) " +
                $"dmg={values.BaseDamage}±{values.DamageSpread} hp={values.EntityHp}");

            WeaponTable weapons = BuildWeaponTable(tables);
            // 判定面表字节基摘要（批 E）：与客户端同一清单/同一顺序/同一份字节 → 同摘要；
            // 房间摘要 = 它 + 本局规则初值（`FixedCombatConfig.Capture`）——客户端据此复算拒局。
            uint digest = TableLayout.ComputeGameplayDigestBase(cache);
            HostLog.Info("Tables",
                $"玩法数据身份基摘要:0x{digest:x8}（判定面 {TableLayout.JudgmentFiles.Length} 表——客户端装载后复算比对）");
            // 装载产物经返回值交给组合根显式传递（技术债 #1：不再回填全局静态面）。
            return new ServerTableLoad(values, weapons, digest);
        }

        /// <summary>
        /// 纯解析（可测）：玩法、移动与实体表字节 → 固定对局数值。坏格式/截断 → 抛
        /// （不返回默认值——静默兜底会让"表没生成"变成"跑着默认值"的隐形分叉）。
        /// 只解析三张数值来源表，不要求武器或表现配置。
        /// </summary>
        public static ServerCombatValues Parse(byte[] combatnumBytes, byte[] movementconfigBytes,
            byte[] entityconfigBytes)
        {
            var combat = new cfg.Tbcombatnum(new Luban.ByteBuf(combatnumBytes));
            var movement = new cfg.Tbmovementconfig(new Luban.ByteBuf(movementconfigBytes));
            var entity = new cfg.Tbentityconfig(new Luban.ByteBuf(entityconfigBytes));
            return ToValues(SimConfigMapper.BuildCombatValues(combat, movement, entity));
        }

        /// <summary>共用映射产物 → 宿主载体；字段含跨表投影，非单张 combatnum 表行。</summary>
        private static ServerCombatValues ToValues(CombatValues row) => new ServerCombatValues
        {
            Id = SingleRowId,
            MoveSpeed = row.MoveSpeed,
            Gravity = row.Gravity,
            HitscanRange = row.HitscanRange,
            BaseDamage = row.BaseDamage,
            DamageSpread = row.DamageSpread,
            EntityHp = row.EntityHp,
            AimMoveSpeed = row.AimMoveSpeed,
            FireStanceFrames = row.FireStanceFrames,
        };

        /// <summary>武器表装载（服务端与客户端同链——权威 Sim 的武器系统必须拿到同一份武器定义；
        /// 缺默认步枪行即抛，fail-fast 与 tbcombatnum 同纪律）。产出 <see cref="WeaponTable"/> 实例。
        /// fire_mode 合法集 {auto, semi}、slot 须在 <see cref="LiteSim.SimConfig.WeaponSlotsPerEntity"/> 内
        /// ——越界即抛（错表必两端分叉，宁可起不来）。</summary>
        private static WeaponTable BuildWeaponTable(cfg.Tables tables)
        {
            if (tables.Tbweapon == null || tables.Tbweapon.GetOrDefault(WeaponConfig.DefaultRifleId) == null)
                throw new InvalidDataException("tbweapon 缺默认步枪行（id=0）——武器系统懒装备依赖它");

            var table = new WeaponTable();
            int count = 0;
            foreach (cfg.weapon row in tables.Tbweapon.DataList)
            {
                if (row.FireMode != WeaponConfig.FireModeAuto && row.FireMode != WeaponConfig.FireModeSemi)
                    throw new InvalidDataException($"tbweapon id={row.Id} 非法 fire_mode：'{row.FireMode}'（合法集 auto/semi）");
                if (row.Slot < 0 || row.Slot >= SimConfig.WeaponSlotsPerEntity)
                    throw new InvalidDataException($"tbweapon id={row.Id} slot 越界：{row.Slot}（槽数 {SimConfig.WeaponSlotsPerEntity}）");
                if (row.Pellets < 1)
                    throw new InvalidDataException($"tbweapon id={row.Id} pellets 非法：{row.Pellets}（≥1——零弹丸＝哑枪，错表必两端分叉）");
                if (row.MaxReserveAmmo != 0 && row.MaxReserveAmmo < row.ReserveAmmo)
                    throw new InvalidDataException($"tbweapon id={row.Id} max_reserve_ammo 非法：{row.MaxReserveAmmo}（0=无限；否则 ≥ reserve_ammo {row.ReserveAmmo}）");
                bool automatic = row.FireMode == WeaponConfig.FireModeAuto;
                if (table.SetRow(row.Id, row.Damage, row.Rpm, row.MagazineSize, row.ReserveAmmo, row.MaxReserveAmmo,
                        row.ReloadFrames, row.Range, row.Spread, row.Pellets, row.SwitchFrames, automatic))
                {
                    table.SetSlotDefault(row.Slot, row.Id);
                    count++;
                }
            }
            if (!table.TryGet(WeaponConfig.DefaultRifleId, out WeaponDef rifle))
                throw new InvalidDataException("tbweapon 默认步枪行未入表（id 越界？）");
            table.TryGetSlotDefault(1, out int slot1Id);
            HostLog.Info("Tables",
                $"武器表装载：{count} 行（默认步枪 dmg={rifle.Damage} rpm={rifle.Rpm} " +
                $"mag={rifle.MagazineSize} 换弹={rifle.ReloadFrames}帧 节拍={rifle.FireIntervalFrames}帧；槽1默认 id={slot1Id}）");
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

    /// <summary>装载产物（数值行 + 武器表实例 + 判定面表字节基摘要；由装配方持有并显式传递——无静态面）。</summary>
    public readonly struct ServerTableLoad
    {
        /// <summary>玩法数值行（`ToValues()` → <see cref="CombatValues"/> 实例）。</summary>
        public readonly ServerCombatValues Combat;

        /// <summary>武器表实例（tb_weapon 行）。</summary>
        public readonly WeaponTable Weapons;

        /// <summary>判定面表字节基摘要（批 E——`TableLayout.ComputeGameplayDigestBase`；
        /// 经 `HostAssembly.Inputs.GameplayDigestBase` 显式传入宿主/房间）。</summary>
        public readonly uint Digest;

        public ServerTableLoad(ServerCombatValues combat, WeaponTable weapons, uint digest)
        {
            Combat = combat;
            Weapons = weapons;
            Digest = digest;
        }
    }

    /// <summary>组合数值载体，包含移动表重力与实体表出生生命的固定对局投影。</summary>
    public struct ServerCombatValues
    {
        public int Id;
        public float MoveSpeed;
        public float Gravity;
        public float HitscanRange;
        public int BaseDamage;
        public int DamageSpread;
        public int EntityHp;
        public float AimMoveSpeed;
        public int FireStanceFrames;

        /// <summary>表行 → 玩法数值实例（装载产物；由调用方**显式传入**宿主/房间——
        /// 技术债 #1：不再回填全局静态面）。</summary>
        public CombatValues ToValues()
        {
            return new CombatValues(MoveSpeed, Gravity, HitscanRange, BaseDamage, DamageSpread, EntityHp,
                AimMoveSpeed, FireStanceFrames);
        }
    }
}
