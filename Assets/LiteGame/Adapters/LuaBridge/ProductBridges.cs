using System;
using XLua; // lint-allow R12：产品桥装订口（框架四门面+产品门面同 env 单源）——本件住 Lua 桥适配器，装配根注入流程端口

namespace LiteGame
{
    /// <summary>
    /// 产品桥装订面（单源）：Lua env 重建时绑定**全部**全局门面——框架四件（data/ui/content/text，
    /// <see cref="Bridge.BindGlobals"/>）+ 产品门面（联机 meta <see cref="MetaBridge"/>、设置
    /// <see cref="ProductSettings"/>、房间会话 <see cref="RoomBridge"/>——房主开局批）。
    /// Preload 首建与 DevReload 重建两条路共用本口——新增产品门面在此加一行，
    /// 禁止在流程里散落第二处 env 绑定（白名单纪律的可枚举点）。
    /// 流程经 <see cref="ILuaProductBridge"/> 端口消费，模块不反向认识本件。
    /// </summary>
    public sealed class ProductBridges : ILuaProductBridge
    {
        private readonly RoomBridge _room;   // 与流程共用同一实例（Battle 阶段装订的会话必须能被 env 重建后的门面读到）

        public ProductBridges(RoomBridge room)
            => _room = room ?? throw new ArgumentNullException(nameof(room));

        public void BindGlobals(LuaEnv env)
        {
            Bridge.BindGlobals(env);              // 框架四门面（data/ui/content/text）
            MetaBridge.BindGlobals(env);          // 联机门面（meta——产品模块）
            ProductSettings.BindGlobals(env);     // 设置门面（settings——产品模块）
            _room.BindGlobals(env);               // 房间会话门面（room——本适配器）
        }
    }
}