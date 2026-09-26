using LiteFramework;
using XLua;

namespace LiteGame
{
    // 三注册表的类型化入口（M3 步骤 2.4）：元素类型同为 LuaTable，容器按 Type 键控（重复注册抛），
    // 故以三个标记接口区分。实例在 GameEntry 装配点创建 → ProcedureLaunch 注册（唯一受信装配点）
    // → RegistryFiller 填充。注册表键 = 全路径（"UI.UIMain"，LuaRegistryTests 同款约定），kind 仅作报错提示。

    /// <summary>UI 逻辑表注册表（TbUIForm.LuaPath → ui/ 下逻辑表；M4 UI 壳经生命周期桥消费）。</summary>
    public interface IUILuaRegistry : ILuaRegistry<LuaTable> { }

    /// <summary>内容处理器注册表（TbContentEntry.Entry → content/ 下处理器表）。</summary>
    public interface IContentLuaRegistry : ILuaRegistry<LuaTable> { }

    /// <summary>壳策略覆盖注册表（TbStrategy.LuaPath，空 = 用 C# 默认实现，§4.4）。</summary>
    public interface IStrategyLuaRegistry : ILuaRegistry<LuaTable> { }

    /// <summary>UI 逻辑表注册表实现（kind="UI"）。</summary>
    public sealed class UiLuaRegistry : LuaRegistry<LuaTable>, IUILuaRegistry
    {
        public UiLuaRegistry() : base("UI") { }
    }

    /// <summary>内容处理器注册表实现（kind="Content"）。</summary>
    public sealed class ContentLuaRegistry : LuaRegistry<LuaTable>, IContentLuaRegistry
    {
        public ContentLuaRegistry() : base("Content") { }
    }

    /// <summary>壳策略覆盖注册表实现（kind="Strategies"）。</summary>
    public sealed class StrategyLuaRegistry : LuaRegistry<LuaTable>, IStrategyLuaRegistry
    {
        public StrategyLuaRegistry() : base("Strategies") { }
    }
}
