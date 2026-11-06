using System;
using System.Collections.Generic;
using System.IO;
using LiteSim;
using LiteFramework;
using LiteGame.Modules.Config;

namespace RoomServer
{
    /// <summary>
    /// 服务端玩法数值/武器表装载（《玩法数值与Luban配置专项设计》）： 服务端跑**权威 Sim**，必须与客户端拿到**同一份手感数值与武器定义**——否则同一份输入两端算出 不同结果，表现为和解风暴。
    /// </summary>
    public static class ServerTableLoader
    {
        /// <summary>客户端表数据目录（相对仓库根；gen.bat Pass 1 产出，两端共用同一份文件）。</summary>
        public const string RelativeDir = "Assets/GameData/Config";

        public const int SingleRowId = SimConfigMapper.DefaultRowId;

        /// <summary>
        /// 从仓库根装载客户端 .bytes 表目录（定位失败或缺表 → 抛，fail-fast）；返回装载实例。
        /// </summary>
        /// <param name="combatOverrides">判定参数覆写合并（**唯一合并点**：表值之后、`ToValues`/摘要之前）。
        public static ServerTableLoad LoadFromRepo(Func<CombatValues, CombatValues> combatOverrides = null)
        {
            string root = FindRepoRoot()
                          ?? throw new InvalidOperationException(
                              $"找不到仓库根（需含 Assets 与 Tests/Tests.slnx）——无法装载 {RelativeDir}");
            return LoadTableBytes(Path.Combine(root, RelativeDir.Replace('/', Path.DirectorySeparatorChar)),
                combatOverrides);
        }

        /// <summary>
        /// 从指定表目录装载并返回**装载实例**（表目录 = 客户端 GameData/Config 根，文件按 <c>TableLayout.Files</c> 的领域相对路径取——源链接共编单源。）。
        /// </summary>
        public static ServerTableLoad LoadTableBytes(string configDir,
            Func<CombatValues, CombatValues> combatOverrides = null)
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

            CombatValues combatValues = SimConfigMapper.BuildCombatValues(tables.Tbcombatnum,
                tables.Tbmovementconfig, tables.Tbentityconfig, tables.Tbcombatgeom);
            // 唯一合并点（服务端）：表值之后、`ToValues`/摘要之前。委托由组合根按 SimParamsGate 求值后给入，
            // 常规联机为 null——两端判定几何必须同源，覆写只服务进程内本地服调参。
            if (combatOverrides != null) combatValues = combatOverrides(combatValues);
            var values = ToValues(combatValues);
            HostLog.Info("Tables",
                $"玩法数值（bin 表）：move={values.MoveSpeed} gravity={values.Gravity} " +
                $"hitscan={values.HitscanRange} hit={combatValues.Geometry.HitscanRadius}(表) body={combatValues.Geometry.BodyRadius}:{combatValues.Geometry.HitscanHeight}(表) " +
                $"dmg={values.BaseDamage}±{values.DamageSpread} hp={values.EntityHp}");

            WeaponTable weapons = BuildWeaponTable(tables);
            // 规则四值：与客户端本地测试房同一读取口（BattleStrategyTableReader）——表驱动，缺行回落默认。
            MatchRules rules = BattleStrategyTableReader.ReadRules(tables.Tbbattlestrategy, MatchRules.Default);
            // 判定面表字节基摘要（批 E）：与客户端同一清单/同一顺序/同一份字节 → 同摘要；
            // 房间摘要 = 它 + 本局规则初值（`FixedCombatConfig.Capture`）——客户端据此复算拒局。
            uint digest = TableLayout.ComputeGameplayDigestBase(cache);
            HostLog.Info("Tables",
                $"玩法数据身份基摘要:0x{digest:x8}（判定面 {TableLayout.JudgmentFiles.Length} 表——客户端装载后复算比对）");
            // 装载产物经返回值交给组合根显式传递（技术债 #1：不再回填全局静态面）。
            return new ServerTableLoad(values, weapons, digest, rules);
        }

        /// <summary>
        /// 纯解析（可测）：玩法、移动与实体表字节 → 固定对局数值。
        /// </summary>
        public static ServerCombatValues Parse(byte[] combatnumBytes, byte[] movementconfigBytes,
            byte[] entityconfigBytes, byte[] combatgeomBytes,
            Func<CombatValues, CombatValues> combatOverrides = null)
        {
            var combat = new cfg.Tbcombatnum(new Luban.ByteBuf(combatnumBytes));
            var movement = new cfg.Tbmovementconfig(new Luban.ByteBuf(movementconfigBytes));
            var entity = new cfg.Tbentityconfig(new Luban.ByteBuf(entityconfigBytes));
            var geometry = new cfg.Tbcombatgeom(new Luban.ByteBuf(combatgeomBytes));
            CombatValues values = SimConfigMapper.BuildCombatValues(combat, movement, entity, geometry);
            if (combatOverrides != null) values = combatOverrides(values);   // 同一条合并点（纯解析入口亦同口径）
            return ToValues(values);
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
            Geometry = row.Geometry,
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

    /// <summary>装载产物（数值行 + 武器表实例 + 判定面表字节基摘要 + 规则默认值；由装配方持有并显式传递——无静态面）。</summary>
    public readonly struct ServerTableLoad
    {
        /// <summary>玩法数值行（`ToValues()` → <see cref="CombatValues"/> 实例）。</summary>
        public readonly ServerCombatValues Combat;

        /// <summary>武器表实例（tb_weapon 行）。</summary>
        public readonly WeaponTable Weapons;

        /// <summary>判定面表字节基摘要（批 E——`TableLayout.ComputeGameplayDigestBase`；
        /// 经 `HostAssembly.Inputs.GameplayDigestBase` 显式传入宿主/房间）。</summary>
        public readonly uint Digest;

        /// <summary>房间规则默认值（battlestrategy 的 match.* 行；未显式配置规则的房间模板用它）</summary>
        public readonly MatchRules Rules;

        public ServerTableLoad(ServerCombatValues combat, WeaponTable weapons, uint digest, MatchRules rules)
        {
            Combat = combat;
            Weapons = weapons;
            Digest = digest;
            Rules = rules;
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

        /// <summary>判定几何（`#combatgeom` 13 列的表值投影）。必须随装载产物传递——服务端判定与
        /// 客户端预测同源；不得在 <see cref="ToValues"/> 回落默认几何。</summary>
        public CombatGeometry Geometry;

        /// <summary>表行 → 玩法数值实例（装载产物；由调用方**显式传入**宿主/房间——
        /// 技术债 #1：不再回填全局静态面）。</summary>
        public CombatValues ToValues()
        {
            return new CombatValues(MoveSpeed, Gravity, HitscanRange, BaseDamage, DamageSpread, EntityHp,
                AimMoveSpeed, FireStanceFrames, Geometry);
        }
    }
}
