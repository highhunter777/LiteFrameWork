using System;
using System.Collections.Generic;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// `#uiform.xlsx` 的读写唯一门面（《UI 编辑器工具链专项设计》§3.1/§3.6）：页面清单对账、契约生成、
    /// 校验与登记都只经这里读表；写表/备份/回滚仍由 `scripts/codegen/_uiform_row.py` 承担。
    ///
    /// 路径可注入（`tablePath`）：生产默认 `Luban/Data/#uiform.xlsx`；夹具用副本，绝不触碰真实表。
    /// 本类不依赖 UnityEditor 语义（表是数据面）；写盘失败**也回传备份句柄**，供登记核验回滚。
    /// </summary>
    public static class UiPageTableHelper
    {
        public const string TableRelativePath = "Luban/Data/#uiform.xlsx";
        public const string HelperRelativePath = "scripts/codegen/_uiform_row.py";

        /// <summary>读表（失败显式，不伪造空表）。</summary>
        public static bool TryListPages(out List<UiPageTableRow> rows, out string error, string tablePath = null)
        {
            UiPageToolProcess.HelperRun run = Run("--list", tablePath);
            if (run.Parsed == null || !run.Parsed.ok)
            {
                rows = null;
                error = "页面清单读取失败：" + (run.Parsed?.error ?? run.Error);
                return false;
            }

            rows = Convert(run.Parsed.rows);
            error = null;
            return true;
        }

        /// <summary>读表（失败返回空；对账/工具请走 <see cref="TryListPages"/>）。</summary>
        public static List<UiPageTableRow> ListPages(string tablePath = null)
            => TryListPages(out List<UiPageTableRow> rows, out _, tablePath)
                ? rows
                : new List<UiPageTableRow>();

        /// <summary>建议 id（Python 助手单源：最小空闲 ≥16，跳过保留区）。</summary>
        public static int SuggestId(string tablePath = null)
        {
            UiPageToolProcess.HelperRun run = Run("--suggest", tablePath);
            return run.Parsed != null && run.Parsed.ok ? run.Parsed.id : -1;
        }

        /// <summary>写前校验（id 冲突/保留区/路径与页名一致/layer 0–3；fullScreen 与实际写值一致）。</summary>
        public static bool Check(string pageName, int id, int layer, bool fullScreen, out string error,
            string tablePath = null)
        {
            UiPageToolProcess.HelperRun run = Run(
                $"--page {Quote(pageName)} --id {id} --layer {layer} " +
                $"--full-screen {(fullScreen ? "true" : "false")} --check", tablePath);
            if (run.Parsed == null)
            {
                error = run.Error;
                return false;
            }

            error = run.Parsed.ok ? null : run.Parsed.error;
            return run.Parsed.ok;
        }

        /// <summary>写一行（带备份；备份先于写，失败也回传句柄）。</summary>
        public static UiPageTableWriteResult Write(string pageName, int id, int layer, bool fullScreen,
            string tablePath = null)
        {
            UiPageToolProcess.HelperRun run = Run(
                $"--page {Quote(pageName)} --id {id} --layer {layer} " +
                $"--full-screen {(fullScreen ? "true" : "false")} --confirm", tablePath);
            if (run.Parsed == null) return UiPageTableWriteResult.Failure(run.Error);
            return run.Parsed.ok
                ? UiPageTableWriteResult.Success(run.Parsed.backup)
                : UiPageTableWriteResult.Failure(run.Parsed.error, run.Parsed.backup);
        }

        /// <summary>按备份回滚（只允许本表备份目录中的备份）。</summary>
        public static bool Rollback(string backup, out string error, string tablePath = null)
        {
            UiPageToolProcess.HelperRun run = Run($"--rollback {Quote(backup)}", tablePath);
            if (run.Parsed == null)
            {
                error = run.Error;
                return false;
            }

            error = run.Parsed.ok ? null : run.Parsed.error;
            return run.Parsed.ok;
        }

        /// <summary>删除一行（显式退役路径用；登记不消费）。</summary>
        public static bool Delete(int id, out string backup, out string error, string tablePath = null)
        {
            backup = null;
            UiPageToolProcess.HelperRun run = Run($"--delete-id {id} --confirm", tablePath);
            if (run.Parsed == null)
            {
                error = run.Error;
                return false;
            }

            error = run.Parsed.ok ? null : run.Parsed.error;
            backup = run.Parsed.backup;
            return run.Parsed.ok;
        }

        /// <summary>按页面名（可选 id）查找行：读失败返回 <see cref="UiPageRowLookup.Unknown"/>，不与“无行”混同。</summary>
        public static UiPageRowLookup LookupRow(string pageName, int id, out UiPageTableRow row, out string error,
            string tablePath = null)
        {
            row = null;
            if (!TryListPages(out List<UiPageTableRow> rows, out error, tablePath))
                return UiPageRowLookup.Unknown;
            foreach (UiPageTableRow candidate in rows)
            {
                if (!string.Equals(candidate.PageName, pageName, StringComparison.Ordinal)) continue;
                if (id > 0 && candidate.id != id) continue;
                row = candidate;
                return UiPageRowLookup.Present;
            }

            return UiPageRowLookup.Absent;
        }

        private static UiPageToolProcess.HelperRun Run(string arguments, string tablePath)
        {
            string table = string.IsNullOrEmpty(tablePath) ? TableRelativePath : tablePath.Replace('\\', '/');
            return UiPageToolProcess.RunHelper(arguments + " --table " + Quote(table));
        }

        private static List<UiPageTableRow> Convert(UiPageToolProcess.HelperRow[] source)
        {
            var rows = new List<UiPageTableRow>();
            foreach (UiPageToolProcess.HelperRow item in source ?? Array.Empty<UiPageToolProcess.HelperRow>())
            {
                if (item == null) continue;
                rows.Add(new UiPageTableRow
                {
                    id = item.id,
                    lua = item.lua,
                    prefab = item.prefab,
                    layer = item.layer,
                    fullScreen = item.fullScreen,
                });
            }

            return rows;
        }

        private static string Quote(string value) => UiPageToolProcess.Quote(value);
    }
}
