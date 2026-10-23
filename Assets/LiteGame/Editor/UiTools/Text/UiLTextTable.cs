using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteClient;
using LiteGame.Editor.UiTools.Validation;
using Luban;

namespace LiteGame.Editor.UiTools.Text
{
    /// <summary>
    /// `tbltext` 单条目的编辑器侧值视图（Key/Zh/En 三列；供校验规则与测试注入共用——
    /// 规则不绑定 Luban 线上类型，合成条目无需构造 ByteBuf）。
    /// </summary>
    public readonly struct UiLTextEntry
    {
        public readonly string Key;
        public readonly string Zh;
        public readonly string En;

        public UiLTextEntry(string key, string zh, string en)
        {
            Key = key;
            Zh = zh;
            En = en;
        }
    }

    /// <summary>
    /// `tbltext` 编辑器读面（LTextKeyRef Inspector 的 key 下拉/预览用；不进运行时）。
    /// 装载口径与 `ConfigService` 一致：表清单取 <see cref="TableLayout.Files"/>，数据路径取
    /// <see cref="TableLayout.TableDataPath"/>，失败显式返回错误而不是伪造空表。
    /// </summary>
    public static class UiLTextTable
    {
        private static Dictionary<string, cfg.ltext> _rows;
        private static string _error;

        public static bool TryGetRows(out IReadOnlyDictionary<string, cfg.ltext> rows, out string error)
        {
            Ensure();
            rows = _rows;
            error = _error;
            return _rows != null;
        }

        public static string[] Keys()
        {
            Ensure();
            return _rows == null
                ? Array.Empty<string>()
                : _rows.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }

        public static void Reload()
        {
            _rows = null;
            _error = null;
        }

        /// <summary>取条目视图（校验规则消费面；装载失败显式报错，不伪造空表）。</summary>
        public static bool TryGetEntries(out System.Collections.Generic.IReadOnlyList<UiLTextEntry> entries, out string error)
        {
            if (!TryGetRows(out var rows, out error))
            {
                entries = null;
                return false;
            }
            entries = rows.Values
                .Select(row => new UiLTextEntry(row.Key, row.Zh, row.En))
                .ToList();
            return true;
        }

        private static void Ensure()
        {
            if (_rows != null || _error != null) return;
            try
            {
                string root = UiValidator.ProjectRoot;
                var cache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (string file in TableLayout.Files)
                {
                    string full = Path.Combine(root, TableLayout.TableDataPath(file).Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                        throw new FileNotFoundException("表数据缺失：" + TableLayout.TableDataPath(file), full);
                    cache[TableLayout.TableName(file)] = File.ReadAllBytes(full);
                }

                var tables = new cfg.Tables(name =>
                {
                    if (!cache.TryGetValue(name, out byte[] bytes))
                        throw new InvalidOperationException("表清单未登记生成表：" + name);
                    return new ByteBuf(bytes);
                });

                var rows = new Dictionary<string, cfg.ltext>(StringComparer.Ordinal);
                foreach (cfg.ltext row in tables.Tbltext.DataList)
                    if (row != null && !string.IsNullOrEmpty(row.Key)) rows[row.Key] = row;
                _rows = rows;
            }
            catch (Exception ex)
            {
                _error = "tbltext 读取失败：" + ex.Message;
            }
        }
    }

    /// <summary>
    /// 文本族规则的表数据面解析：上下文种子优先（测试注入），缺省走 <c>UiLTextTable</c> 真实装载。
    /// 供全部文本族规则共用一份解析（key/占位符/语言列/富文本四规则同源）。
    /// </summary>
    public static class UiLTextEntries
    {
        public static bool TryResolve(Validation.UiValidationContext context,
            out System.Collections.Generic.IReadOnlyList<UiLTextEntry> entries, out string error)
        {
            if (context != null && context.LTextSeed != null)
            {
                entries = context.LTextSeed;
                error = null;
                return true;
            }
            return UiLTextTable.TryGetEntries(out entries, out error);
        }
    }
}
