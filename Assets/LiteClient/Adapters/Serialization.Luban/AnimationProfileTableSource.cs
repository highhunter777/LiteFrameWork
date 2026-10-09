using System.Collections.Generic;
using System.IO;
using LiteFramework.Animation;
using Luban;

namespace LiteClient
{
    /// <summary>
    /// 动画 Profile 的**文件直载源**（编辑器工具与测试共用——不经 DI/内容服务全链）：
    /// 从 GameData/Config 目录读表字节 → 建表 → 翻译边界 → 按模型族装配 Profile。
    /// 与 <see cref="ConfigService"/> 的运行时路径**同一张表、同一翻译器、同一装载器**
    /// ——工具/测试与运行时的差异只在字节来源（文件 vs 内容租约），语义链零分叉。
    /// 运行时消费方不要用本类（走 <see cref="AnimationProfileConfig"/> 读口——
    /// 快照化/原子发布/校验前置归 ConfigService）。
    /// </summary>
    public static class AnimationProfileTableSource
    {
        /// <summary>从表数据目录装配指定模型族的 Profile。
        /// <paramref name="dataDir"/>＝表字节目录（项目相对路径，如 <c>Assets/GameData/Config/</c>）——
        /// 目录内须齐备 <see cref="ConfigService.TableDataFiles"/> 全部 .bytes（建表器全表装载）。</summary>
        public static AnimationProfile Load(string dataDir, string modelFamily)
        {
            var cache = new Dictionary<string, byte[]>(ConfigService.TableDataFiles.Length);
            foreach (string file in ConfigService.TableDataFiles)
            {
                string path = $"{dataDir}{file}.bytes";
                if (!File.Exists(path))
                    throw new System.IO.FileNotFoundException($"表字节缺失：{path}（先跑 Luban\\gen.bat 生成）", path);
                cache[file] = File.ReadAllBytes(path);
            }

            var tables = new cfg.Tables(name => new ByteBuf(cache[name]));
            var rows = AnimationProfileTableMapper.ToRows(tables.Tbanimationprofile);
            return AnimationProfileLoader.FromRows(rows, modelFamily);
        }
    }
}
