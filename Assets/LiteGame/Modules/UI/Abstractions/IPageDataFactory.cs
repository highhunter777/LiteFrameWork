using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面数据投射端口（UI 域产出，实现归 Lua 桥适配器）：中立数据树 <see cref="PageData"/> →
    /// 页面导航数据 <see cref="IUIData"/>（Lua 侧拿到原生表；C# 页面拿到自定义 <see cref="IUIData"/>）。
    ///
    /// 为什么需要：数据面是**流程段唯一会碰平台脚本对象的地方**（结算摘要 / 菜单门控位）。
    /// 收成端口后流程段只装配具名键，不引 XLua；平台投射与 DevReload 后的 env 重建由实现侧承担。
    /// </summary>
    public interface IPageDataFactory
    {
        /// <summary>投射数据树（null = 无数据，返回 null；空树＝空表）。</summary>
        IUIData Create(PageData data);
    }
}
