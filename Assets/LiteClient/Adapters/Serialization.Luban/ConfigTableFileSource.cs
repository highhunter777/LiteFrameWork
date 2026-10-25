using System;
using System.Collections.Generic;
using System.IO;
using Luban;

namespace LiteClient
{
    /// <summary>
    /// 配置表的文件直载源（编辑器工具与测试共用——不经 DI/内容服务全链）：
    /// 从 <see cref="TableLayout.DataDir"/> 风格目录读全部表字节 → Luban <see cref="cfg.Tables"/>。
    /// 与运行时 <see cref="ConfigService"/> 同一张表、同一 loader 口径；差异只在字节来源（磁盘 vs 内容租约）。
    /// </summary>
    public static class ConfigTableFileSource
    {
        /// <summary>装载全部表（目录内须齐备 <see cref="ConfigService.TableDataFiles"/> 的 .bytes；缺失显式抛）。</summary>
        public static cfg.Tables Load(string dataDir)
        {
            IReadOnlyDictionary<string, byte[]> cache = LoadBytes(dataDir);
            return new cfg.Tables(name => new ByteBuf(cache[name]));
        }

        /// <summary>按裸表名读取全部表字节（键＝<see cref="TableLayout.TableName"/>，Luban loader 契约）。</summary>
        public static IReadOnlyDictionary<string, byte[]> LoadBytes(string dataDir)
        {
            if (string.IsNullOrEmpty(dataDir)) throw new ArgumentException("表数据目录不能为空", nameof(dataDir));

            var cache = new Dictionary<string, byte[]>(ConfigService.TableDataFiles.Length);
            foreach (string file in ConfigService.TableDataFiles)
            {
                string path = string.Equals(dataDir, TableLayout.DataDir, StringComparison.Ordinal)
                    ? TableLayout.TableDataPath(file)
                    : $"{dataDir}{file}.bytes";
                if (!File.Exists(path))
                    throw new FileNotFoundException($"表字节缺失：{path}（先跑 Luban\\gen.bat 生成）", path);
                cache[TableLayout.TableName(file)] = File.ReadAllBytes(path);
            }
            return cache;
        }
    }
}
