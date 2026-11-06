using System;
using LiteFramework;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// Lua 桥装订面（UI 域消费，实现归 Lua 桥适配器）：UI 段运行期需要的三件适配器件—— 页面逻辑工厂、页面数据投射、文本门面后端装订。
    /// </summary>
    public interface ILuaUIBinding
    {
        /// <summary>页面逻辑工厂（装配给 <c>UIService</c> 的逻辑解析器）。</summary>
        IUILogicFactory LogicFactory { get; }

        /// <summary>页面数据投射（UI 段 Put 给流程段——流程段不认适配器与 Lua 表）。</summary>
        IPageDataFactory PageData { get; }

        /// <summary>绑定 <c>Bridge.text</c> 后端（与页面索引 BindLocale 共用同一语言服务实例）。</summary>
        void BindTextFacade(Func<ILocalizationService> localeProvider);
    }
}
