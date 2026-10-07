using System;
using System.IO;
using LiteSim;

namespace RoomServer
{
    /// <summary>
    /// 服务端玩法数值装载（《玩法数值解耦审查与Luban表设计》§3.2）：
    /// 服务端跑**权威 Sim**，必须与客户端拿到**同一份手感数值**——否则同一份输入两端算出不同结果，
    /// 表现为和解风暴。
    ///
    /// **主源链路**：本类直读**客户端同一份 .bytes**（`Assets/GameData/Config/*.bytes`，gen.bat Pass 1 产出；
    /// 缺省走 `LoadFromRepo` 仓库路径，`--combat-table <目录>` 显式覆盖）。两端同代码（生成物源链接共编）
    /// 同数据（同一份二进制），物理上不可能漂移。
    ///
    /// **装载即回填**：`LoadTableBytes`/`LoadFromRepo` 装载后立即经 <see cref="CombatNumValues.Apply"/> 回填权威数值面
    /// （客户端 `ConfigService` 同语义）——装载与回填是同一契约的两半，拆开即"服务端跑默认值、客户端跑表值"的静默分叉；
    /// <see cref="Parse"/> 保持纯解析（不触碰静态面）。L1 钉子：`CombatNumbersTests.装载即回填_运行面读到表值`。
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
        /// 从仓库根装载客户端 .bytes 表目录（定位失败或缺表 → 抛，fail-fast）；装载即回填，返回表行数值。
        /// 数值错了必然分叉——宁可起不来，也不要带着错数值跑权威局。
        /// </summary>
        public static CombatNumValues LoadFromRepo()
        {
            string root = FindRepoRoot()
                          ?? throw new InvalidOperationException(
                              $"找不到仓库根（需含 Assets 与 Tests/Tests.slnx）——无法装载 {RelativeDir}");
            return LoadTableBytes(Path.Combine(root, RelativeDir.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>
        /// 从指定表目录装载并**回填权威数值面**（表目录 = 客户端 GameData/Config；表清单与客户端
        /// <c>ConfigService.TableDataFiles</c> 同源——Tables 构造器逐表取字节），返回表行数值（观测/断言用）。
        /// </summary>
        public static CombatNumValues LoadTableBytes(string configDir)
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
            values.Apply();
            return values;
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

    /// <summary>表行数值（纯数据载体；<see cref="Apply"/> 回填 LiteSim 的静态消费面）。</summary>
    public struct CombatNumValues
    {
        public int Id;
        public float MoveSpeed;
        public float Gravity;
        public float HitscanRange;
        public int BaseDamage;
        public int DamageSpread;
        public int EntityHp;

        /// <summary>回填 `CombatConfig`——**唯一写入口**（消费点遍布 Sim 系统，静态面只此一处被改写；
        /// 装载链 <see cref="CombatNumbers.LoadTableBytes"/>/<see cref="CombatNumbers.LoadFromRepo"/> 装载即调用）。</summary>
        public void Apply()
        {
            CombatConfig.LoadFrom(MoveSpeed, Gravity, HitscanRange,
                BaseDamage, DamageSpread, EntityHp);
        }
    }
}
