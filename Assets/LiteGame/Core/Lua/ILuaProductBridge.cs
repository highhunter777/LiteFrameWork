namespace LiteGame
{
    /// <summary>
    /// 产品 Lua 门面装订端口（模块自持契约）：Preload 首建与 DevReload 重建两条路共用同一个装订口。
    /// </summary>
    public interface ILuaProductBridge
    {
        /// <summary>把全部门面绑定到当前 Lua env（env 重建后须重新调用）。</summary>
        void BindGlobals(object env);
    }
}
