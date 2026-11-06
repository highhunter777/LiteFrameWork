using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面逻辑工厂端口（UI 域消费，实现归 Lua 桥适配器）：页面定义 → 生命周期逻辑实例。
    /// </summary>
    public interface IUILogicFactory
    {
        /// <param name="page">页面定义（<c>LuaPath</c> 即注册表键）。</param>
        /// <param name="confirm">页面确认缝（未绑定时按取消返回）——由 UI 段注入，页面不直取反馈服务。</param>
        IUIFormLogic Create(PageDefinition page, IConfirmDialog confirm);
    }
}
