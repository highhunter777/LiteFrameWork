using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LiteGame.Modules.Config;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 判定参数覆写 → `#combatgeom` 表行的导出器（编辑器侧，复用 <see cref="CombatGeomTableWriter"/>）：
    /// 把调参面板（<c>SimParamsDebugTunables</c>）留在 <see cref="SimParamsOverrides"/> 里的非空列写回表源。
    /// `confirm = false` 即 dry-run（只出预览报告，不写盘）；写入由表行助手落备份。
    /// 写完不自动跑 Luban：需手动 `Luban/gen.bat` → `python scripts/codegen/toolbox.py build-hash`（两端需重建）。
    /// 校验口径由 <see cref="SimParamsOverrides"/> 的写入校验保证（非法值在 Set 处已被拒，这里只做格式化）。
    /// </summary>
    public static class SimParamsExporter
    {
        private const string NextStep =
            "下一步：Luban/gen.bat → python scripts/codegen/toolbox.py build-hash → 两端需重建";

        private const string BackupDir = "Temp/combatgeom-backups";

        /// <summary>整数列（表头 `##type` 为 int；格式化必须无小数点，表行助手按 INT_RE 拒小数）。</summary>
        private static readonly HashSet<string> IntegerColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            SimParamsOverrides.HeadshotDamageShiftColumn,
            SimParamsOverrides.CorpseFramesColumn,
        };

        /// <summary>导出非空覆写列到表行；<paramref name="confirm"/> 决定真写还是 dry-run 预览。</summary>
        public static bool TryExport(SimParamsOverrides overrides, bool confirm, out string report, out string error)
        {
            report = null;
            error = null;
            if (overrides == null)
            {
                error = "无判定参数覆写实例（调参面板未装配——先在 Play 模式下调参）";
                return false;
            }
            if (!overrides.HasAny)
            {
                report = "无覆写：`#combatgeom` 13 列均未改动（先在 GM/调试面板「判定参数」组调参）\n" + NextStep;
                return true;
            }

            if (!CombatGeomTableWriter.TryReadRow(out Dictionary<string, string> row, out error)) return false;

            var sets = new List<string[]>(overrides.Count);
            var lines = new List<string>(overrides.Count);
            foreach (string column in SimParamsOverrides.Columns)
            {
                if (!overrides.TryGet(column, out float value)) continue;
                string text = FormatValue(column, value);
                string before = row != null && row.TryGetValue(column, out string current) ? current : "?";
                lines.Add(CombatGeomTableWriter.CombatGeomTablePath + " · " + column + " " + before + " → " + text);
                sets.Add(new[] { column, text });
            }

            if (!CombatGeomTableWriter.TryWrite(sets.ToArray(), confirm, out string tableReport, out error))
                return false;

            var builder = new StringBuilder();
            builder.AppendLine(confirm
                ? "导出判定参数到表（已写入）："
                : "导出判定参数到表（预览 / dry-run，未写盘）：");
            foreach (string line in lines) builder.AppendLine(line);
            builder.AppendLine(tableReport);
            builder.AppendLine(confirm
                ? "备份：" + BackupDir + "/combatgeom-<时间戳>.xlsx（有差异时由表行助手落）"
                : "备份：写入时才生成（" + BackupDir + "）");
            builder.Append(NextStep);
            report = builder.ToString();
            return true;
        }

        private static string FormatValue(string column, float value)
            => IntegerColumns.Contains(column)
                ? ((int)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("R", CultureInfo.InvariantCulture);
    }
}
