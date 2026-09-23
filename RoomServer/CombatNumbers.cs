using System;
using System.IO;
using System.Text.Json;
using LiteSim;

namespace RoomServer
{
    /// <summary>
    /// 服务端玩法数值装载（《玩法数值解耦审查与Luban表设计》§3.2，2026-09-19）：
    /// 服务端跑**权威 Sim**，必须与客户端拿到**同一份手感数值**——否则同一份输入两端算出不同结果，
    /// 表现为和解风暴。做法：`gen.bat` 的 Pass 1b 把同一份表源额外产出 json
    /// （`RoomServer/Data/tbcombatnum.json`），本类读取后回填 <see cref="CombatConfig"/>。
    ///
    /// **为什么用 json 而不是 bin**：Luban 的 C# 运行时是**本机 `file:` 依赖**（《环境恢复指南》§4：
    /// manifest 不入库）→ .NET 8 的 RoomServer 无法引用它；json 是本工程可直接解析的形态，
    /// 且与客户端 bin **同一次 gen.bat、同一份 xlsx** 产出 → 不可能漂移。
    ///
    /// **一致性双保险**：① 两端数值同源（同一 xlsx）② 表数据进 buildHash（`scripts/gen-build-hash.py`），
    /// 版本不一致直接在 Join 握手被拒。
    /// </summary>
    public static class CombatNumbers
    {
        /// <summary>表数据相对仓库根的路径（gen.bat Pass 1b 产出）。</summary>
        public const string RelativePath = "RoomServer/Data/tbcombatnum.json";

        public const int SingleRowId = 1;   // 单行表固定 id

        /// <summary>
        /// 自动定位仓库根并装载（Production 入口调用；定位失败或数据缺失 → 抛，fail-fast）。
        /// 数值错了必然分叉——宁可起不来，也不要带着错数值跑权威局。
        /// </summary>
        public static void LoadFromRepo()
        {
            string root = FindRepoRoot()
                          ?? throw new InvalidOperationException(
                              $"找不到仓库根（需含 Assets 与 Tests/Tests.slnx）——无法装载 {RelativePath}");
            Load(Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>从指定 json 文件装载（测试与显式路径用）。</summary>
        public static void Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException(
                    $"玩法数值表缺失：{jsonPath}（跑 Luban/gen.bat 的 Pass 1b 生成——数值缺失等于两端分叉）", jsonPath);

            string json = File.ReadAllText(jsonPath);
            CombatNumValues values = Parse(json);
            values.Apply();

            Console.WriteLine(
                $"[RoomServer] 玩法数值装载：move={values.MoveSpeed} gravity={values.Gravity} " +
                $"hitscan={values.HitscanRange}/{values.HitscanRadius}/{values.HitscanHeight} " +
                $"dmg={values.BaseDamage}±{values.DamageSpread} hp={values.EntityHp}");
        }

        /// <summary>
        /// 纯解析（可测）：json 文本 → 数值。形如 <c>[{ "id":1, "move_speed":5, ... }]</c>。
        /// 缺字段/坏格式 → 抛（不返回默认值——静默兜底会让"表没生成"变成"跑着默认值"的隐形分叉）。
        /// </summary>
        public static CombatNumValues Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                throw new InvalidDataException("tbcombatnum.json 结构异常：应为非空数组");

            JsonElement row = doc.RootElement[0];
            return new CombatNumValues
            {
                Id = RequireInt(row, "id"),
                MoveSpeed = RequireFloat(row, "move_speed"),
                Gravity = RequireFloat(row, "gravity"),
                HitscanRange = RequireFloat(row, "hitscan_range"),
                HitscanRadius = RequireFloat(row, "hitscan_radius"),
                HitscanHeight = RequireFloat(row, "hitscan_height"),
                BaseDamage = RequireInt(row, "base_damage"),
                DamageSpread = RequireInt(row, "damage_spread"),
                EntityHp = RequireInt(row, "entity_hp"),
            };
        }

        private static int RequireInt(JsonElement row, string name)
        {
            if (!row.TryGetProperty(name, out JsonElement e) || !e.TryGetInt32(out int v))
                throw new InvalidDataException($"tbcombatnum.json 缺字段或类型不符：{name}（应为 int）");
            return v;
        }

        private static float RequireFloat(JsonElement row, string name)
        {
            if (!row.TryGetProperty(name, out JsonElement e) || !e.TryGetSingle(out float v))
                throw new InvalidDataException($"tbcombatnum.json 缺字段或类型不符：{name}（应为 float）");
            return v;
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

    /// <summary>表行数值（纯数据载体；<see cref="Apply"/> 回填 LiteSim 的静态消费面）。</summary>
    public struct CombatNumValues
    {
        public int Id;
        public float MoveSpeed;
        public float Gravity;
        public float HitscanRange;
        public float HitscanRadius;
        public float HitscanHeight;
        public int BaseDamage;
        public int DamageSpread;
        public int EntityHp;

        /// <summary>回填 `CombatConfig`——**唯一写入口**（消费点遍布 Sim 系统，静态面只此一处被改写）。</summary>
        public void Apply()
        {
            CombatConfig.LoadFrom(MoveSpeed, Gravity, HitscanRange, HitscanRadius,
                HitscanHeight, BaseDamage, DamageSpread, EntityHp);
        }
    }
}
