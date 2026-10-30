using LiteFramework;
using cfg;

namespace LiteGame
{
    /// <summary>Luban 专用的配置服务视图（Tables 是 Luban 生成类型）；消费者必须本来就认识 Luban。</summary>
    public interface IConfigService : IConfigTableSource
    {
        cfg.Tables Tables { get; }
    }
}
