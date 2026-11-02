using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面逻辑工厂端口（UI 域消费，实现归 Lua 桥适配器）：页面定义 → 生命周期逻辑实例。
    ///
    /// 为什么需要：`UIService` 的逻辑解析器过去由装配段直接 `new LuaBehaviourAdapter(...)`——
    /// 那让模块程序集反向认识适配器。工厂端口把"实例怎么来"下沉到适配器，UI 段只决定
    /// "何时要实例 + 确认缝用谁"。
    /// </summary>
    public interface IUILogicFactory
    {
        /// <param name="page">页面定义（<c>LuaPath</c> 即注册表键）。</param>
        /// <param name="confirm">页面确认缝（未绑定时按取消返回）——由 UI 段注入，页面不直取反馈服务。</param>
        IUIFormLogic Create(PageDefinition page, IConfirmDialog confirm);
    }
}
