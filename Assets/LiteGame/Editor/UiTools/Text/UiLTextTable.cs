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
}
