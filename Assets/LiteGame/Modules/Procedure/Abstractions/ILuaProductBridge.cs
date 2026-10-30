using XLua; // lint-allow R12：产品门面装订端口（模块自持契约；模块内仅此一处 LuaEnv 形，实现归 Lua 桥）

namespace LiteGame
{
    /// <summary>
    /// 产品 Lua 门面装订端口（模块侧契约）：Preload 首建与 DevReload 重建两条路共用同一个装订口。
    /// 实现归 Lua 桥适配器（框架四门面 + 联机/设置/房间三个产品门面同 env 单源装订）；
    /// 流程只经本端口触发，不认识具体桥。
    /// </summary>
    public interface ILuaProductBridge
    {
        /// <summary>把全部门面绑定到当前 Lua env（env 重建后须重新调用）。</summary>
        void BindGlobals(LuaEnv env);
    }
}