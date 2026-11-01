using System;
using LiteFramework;

namespace LiteGame.Modules.Ui
{
    /// <summary>
    /// Lua 桥装订面（Ui 域消费，实现归 Lua 桥适配器）：Ui 段运行期需要的三件适配器件——
    /// 页面逻辑工厂、页面数据投射、文本门面后端装订。
    ///
    /// 为什么整成一层装订面：`LiteGame.Modules` 程序集硬边禁止直引 `LiteGame.Adapters.*`
    /// （见 ClientAsmdefAllowlistTests）；Ui 段的适配器依赖收成**一个**注入点，
    /// 由装配根（AppComposition）产出、模块只经端口使用——新增一件不需要再加构造参数。
    /// </summary>
    public interface ILuaUiBinding
    {
        /// <summary>页面逻辑工厂（装配给 <c>UIService</c> 的逻辑解析器）。</summary>
        IUiLogicFactory LogicFactory { get; }

        /// <summary>页面数据投射（Ui 段 Put 给流程段——流程段不认适配器与 Lua 表）。</summary>
        IPageDataFactory PageData { get; }

        /// <summary>绑定 <c>Bridge.text</c> 后端（与页面索引 BindLocale 共用同一语言服务实例）。</summary>
        void BindTextFacade(Func<ILocalizationService> localeProvider);
    }
}
