using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面数据投射端口（UI 域产出，实现归 Lua 桥适配器）：中立数据树 PageData → 页面导航数据 IUIData（Lua 侧拿到原生表）。
    /// </summary>
    public interface IPageDataFactory
    {
        /// <summary>投射数据树（null = 无数据，返回 null；空树＝空表）。</summary>
        IUIData Create(PageData data);
    }
}
