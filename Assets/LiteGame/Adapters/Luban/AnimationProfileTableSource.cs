using LiteFramework.Animation;
using LiteGame.Modules.Config;

namespace LiteGame
{
    /// <summary>
    /// 动画 Profile 的**文件直载源**（编辑器工具与测试共用——不经 DI/内容服务全链）： 从 GameData/Config 目录读表字节 → 建表 → 翻译边界 → 按模型族装配 Profile。
    /// </summary>
    public static class AnimationProfileTableSource
    {
        /// <summary>从表数据目录装配指定模型族的 Profile。
        /// <paramref name="dataDir"/>＝表字节目录（项目相对路径，如 <c>Assets/GameData/Config/</c>）——
        /// 目录内须齐备 <see cref="ConfigService.TableDataFiles"/> 全部 .bytes（建表器全表装载）。</summary>
        public static AnimationProfile Load(string dataDir, string modelFamily)
        {
            cfg.Tables tables = ConfigTableFileSource.Load(dataDir);
            var rows = AnimationProfileTableMapper.ToRows(tables.Tbanimationprofile);
            return AnimationProfileLoader.FromRows(rows, modelFamily);
        }
    }
}
