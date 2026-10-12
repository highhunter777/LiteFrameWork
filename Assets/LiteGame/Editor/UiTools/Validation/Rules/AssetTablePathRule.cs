using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 规则：表内资源路径必须存在（《UI编辑器工具专项设计》§4 资源族 + 步 5 登记——
    /// uiform 悬空两次实锤〔201 出生即悬空、批 2 改名漏跟表引用〕的机制化）。
    ///
    /// **通用反射面**：装载全部 Luban 表，扫每行**公开字符串字段**——值以 <c>.prefab</c>/<c>.unity</c>
    /// 结尾即视为资源路径（工程相对，无 <c>Assets/</c> 前缀按表内惯例补前），盘上不存在＝Error。
    /// 不枚举具体表/列——未来任何表加路径列即被同一门禁罩住（守卫类修法，不是点位补丁）。
    /// 表文件缺失＝Warning（构建产物未生成属构建环境问题，非资产断链）。
    /// </summary>
    public sealed class AssetTablePathRule : IUiRule
    {
        public string RuleId => "ui.asset-table-paths";
        public string Family => "资源";

        /// <summary>表数据目录（Luban bytes 产物落点）。</summary>
        public const string TableDataDir = "Assets/GameData/Config";

        // 表清单 = 生成域全部表（bytes 文件, 生成表类）——新表入 Tables.cs 后此处随批补登记
        private static readonly (string file, Type type)[] Tables =
        {
            ("UI/tbuiform.bytes", typeof(cfg.Tbuiform)),
            ("Animation/tbanimationprofile.bytes", typeof(cfg.Tbanimationprofile)),
            ("Combat/tbcombatnum.bytes", typeof(cfg.Tbcombatnum)),
            ("Combat/tbweapon.bytes", typeof(cfg.Tbweapon)),
            ("Content/tbcontententry.bytes", typeof(cfg.Tbcontententry)),
            ("Entity/tbentityconfig.bytes", typeof(cfg.Tbentityconfig)),
            ("Item/tbitemconfig.bytes", typeof(cfg.Tbitemconfig)),
            ("Movement/tbmovementconfig.bytes", typeof(cfg.Tbmovementconfig)),
            ("Strategy/tbstrategy.bytes", typeof(cfg.Tbstrategy)),
        };

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            if (context == null || string.IsNullOrEmpty(context.ProjectRoot)) return issues;
            issues.AddRange(CollectMissingPaths(context.ProjectRoot));
            return issues;
        }

        /// <summary>一处表内路径条目（装载面提取；核对面纯消费——测试可合成直喂）。</summary>
        public readonly struct PathEntry
        {
            public readonly string TableFile;   // 相对 TableDataDir（如 "UI/tbuiform.bytes"）
            public readonly string Field;
            public readonly string RowKey;       // 行定位（如 "Id=201"）
            public readonly string Value;        // 表内原值（可能无 Assets/ 前缀）

            public PathEntry(string tableFile, string field, string rowKey, string value)
            {
                TableFile = tableFile;
                Field = field;
                RowKey = rowKey;
                Value = value;
            }
        }

        /// <summary>装载全部表并提取路径形态条目（.prefab/.unity 结尾的字符串字段值）。</summary>
        public static List<PathEntry> ExtractPathEntries(string projectRoot,
            IReadOnlyList<(string file, Type type)> tables = null)
        {
            var entries = new List<PathEntry>();
            if (!TableDirExists(projectRoot)) return entries;      // 表目录整体缺席＝非工程上下文（合成测试），跳过
            foreach (var (file, type) in tables ?? Tables)
            {
                string path = Path.Combine(projectRoot,
                    (TableDataDir + "/" + file).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) continue;               // 缺产物由 CollectMissingPaths 报，提取面跳过

                var table = Activator.CreateInstance(type, new object[] { new Luban.ByteBuf(File.ReadAllBytes(path)) });
                var list = (System.Collections.IEnumerable)type.GetProperty("DataList")?.GetValue(table);
                if (list == null) continue;

                foreach (var row in list)
                    foreach (var field in row.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (field.FieldType != typeof(string)) continue;
                        string value = (string)field.GetValue(row);
                        if (!IsPathLike(value)) continue;
                        entries.Add(new PathEntry(file, field.Name, RowKey(row), value));
                    }
            }
            return entries;
        }

        /// <summary>核对面（纯函数）：路径形态条目逐一验盘——不存在＝Error；表产物缺失＝Warning。
        /// 测试合成条目直喂此口（负例免手造 Luban bytes）。</summary>
        public static List<UiIssue> CheckPaths(string projectRoot, IEnumerable<PathEntry> entries,
            IReadOnlyList<(string file, Type type)> tables = null)
        {
            var issues = new List<UiIssue>();
            if (!TableDirExists(projectRoot)) return issues;       // 表目录整体缺席＝非工程上下文，跳过
            foreach (var (file, _) in tables ?? Tables)
            {
                string path = Path.Combine(projectRoot,
                    (TableDataDir + "/" + file).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    issues.Add(Make(TableDataDir + "/" + file, "-", UiSeverity.Warning,
                        "表数据文件缺失（Luban 产物未生成？）", "跑 toolbox.py luban 重生成表产物"));
            }

            foreach (var entry in entries)
            {
                string assetRelative = entry.Value.StartsWith("Assets/", StringComparison.Ordinal)
                    ? entry.Value
                    : "Assets/" + entry.Value;
                string full = Path.Combine(projectRoot, assetRelative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                    issues.Add(Make(assetRelative, entry.RowKey, UiSeverity.Error,
                        $"表内路径指向的资产不存在：{entry.TableFile} → {entry.Field} = {entry.Value}",
                        "表内路径列指向的资产必须在盘上：改资产名须同批改表源并重生成（toolbox.py luban）"));
            }
            return issues;
        }

        /// <summary>装载 + 提取 + 核对（Check 的实现面；正/负例测试经 CheckPaths/ExtractPathEntries 分开钉）。</summary>
        public static List<UiIssue> CollectMissingPaths(string projectRoot)
            => CheckPaths(projectRoot, ExtractPathEntries(projectRoot));

        /// <summary>路径形态判据：.prefab/.unity 结尾（忽略大小写）——覆盖已知表内资源引用形态，
        /// 不含糊到把 LuaPath 这类点分模块名误判。</summary>
        public static bool IsPathLike(string value)
            => !string.IsNullOrEmpty(value)
               && (value.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                   || value.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));

        /// <summary>表数据目录是否在场（缺席＝非工程上下文——合成测试根；整目录缺不报，单件缺才 Warning）。</summary>
        private static bool TableDirExists(string projectRoot)
            => !string.IsNullOrEmpty(projectRoot)
               && Directory.Exists(Path.Combine(projectRoot,
                   TableDataDir.Replace('/', Path.DirectorySeparatorChar)));

        private static string RowKey(object row)
        {
            var idField = row.GetType().GetField("Id", BindingFlags.Public | BindingFlags.Instance);
            return idField != null ? $"Id={idField.GetValue(row)}" : row.ToString();
        }

        private static UiIssue Make(string asset, string node, UiSeverity severity, string message, string fix)
            => new UiIssue { RuleId = "ui.asset-table-paths", Severity = severity, Asset = asset, Node = node, Message = message, FixHint = fix };
    }
}
