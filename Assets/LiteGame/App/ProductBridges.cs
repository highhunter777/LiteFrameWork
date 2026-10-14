using XLua; // lint-allow R12：产品桥装订口（框架四门面+meta 同 env 单源）——依赖方向与 MetaBridge 同判（App 侧门面引用 App 编排件）

namespace LiteGame
{
    /// <summary>
    /// 产品桥装订面（单源）：Lua env 重建时绑定**全部**全局门面——框架四件（data/ui/content/text，
    /// <see cref="Bridge.BindGlobals"/>）+ 产品门面（联机 meta <see cref="MetaBridge"/>、设置
    /// <see cref="ProductSettings"/>、房间会话 <see cref="RoomBridge"/>——房主开局批）。
    /// Preload 首建与 DevReload 重建两条路共用本口——新增产品门面在此加一行，
    /// 禁止在流程里散落第二处 env 绑定（白名单纪律的可枚举点）。
    /// </summary>
    public static class ProductBridges
    {
        public static void BindGlobals(LuaEnv env)
        {
            Bridge.BindGlobals(env);              // 框架四门面（data/ui/content/text）
            MetaBridge.BindGlobals(env);          // 联机门面（meta——App 侧，批C-③）
            ProductSettings.BindGlobals(env);     // 设置门面（settings——App 侧，批C-⑥）
            RoomBridge.BindGlobals(env);          // 房间会话门面（room——App 侧，房主开局批）
        }
    }
}
