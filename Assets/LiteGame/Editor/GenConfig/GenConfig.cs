using System.Collections.Generic;
using XLua;
using LiteFramework;
using LiteGame.Modules.Settings;

namespace LiteGame.Editor
{
    /// <summary>
    /// xLua 生成配置：**业务类型（Bridge）不进 LuaCallCSharp**——
    /// 生成代码落在 xLuaMain 内，包 wrapper 需要业务类型就得让 xLuaMain 引 LiteFramework.Runtime，
    /// 而 Runtime（LuaComponent）引 xLuaMain → asmdef 循环（asmdef 禁环）。
    /// 替代形态：**服务桥走 CSharpCallLua 委托注册**（LuaComponent 把适配函数 Set 进全局表，
    /// 查表属低频入口，动态转换符合热路径纪律 §4.3）。
    ///
    /// 白名单纪律仍然成立：**Luban.Tables / 业务类型永不进本文件**；Unity 标准库由 vendor 的
    /// ExampleGenConfig 提供（保留）。新增委托/类型导出 = 这里加一行 → XLua/Generate Code。
    /// </summary>
    public static class GenConfig
    {
        [CSharpCallLua]
        public static List<System.Type> CSharpCallLua = new List<System.Type>
        {
            // 门面导出面（Bridge.Data.BindGlobals / ProductBridges + MetaBridge + ProductSettings 装订）用到的
            // **全部**委托形——批C-④ 五页首次成为真实 Lua 消费者，此处补齐：IL2CPP 下未登记形只能走 Mono 反射兜底（不可靠）。
            // 批C-⑥：ui 派发改带返回值（Func——get* 系回值）；新增 settings 门面的 float/bool/int 形。
            typeof(System.Func<string, XLua.LuaTable, object>),  // self.ui 派发口（__ui_api_c——get* 回值，其余 nil）
            typeof(System.Action),                          // Bridge.meta.LeaveMatch / Bridge.settings.Save
            typeof(System.Action<string>),                  // Bridge.text.SetLocale / Bridge.ui.Close
            typeof(System.Func<string>),                    // Bridge.text.Locale / Bridge.meta.AccountId/ServerUrl/Version
            typeof(System.Func<bool>),                      // Bridge.meta.IsConfigured / Bridge.settings.SoundEnabled
            typeof(System.Action<bool>),                    // Bridge.settings.SetSoundEnabled
            typeof(System.Func<float>),                     // Bridge.settings.Volume
            typeof(System.Action<float>),                   // Bridge.settings.SetVolume
            typeof(System.Func<int>),                       // Bridge.settings.Quality
            typeof(System.Action<int>),                     // Bridge.settings.SetQuality
            typeof(System.Func<string, string>),            // Bridge.text.Get
            typeof(System.Func<string, bool>),              // Bridge.ui.IsOpen
            typeof(System.Func<int, XLua.LuaTable>),        // Bridge.data.GetItem、Bridge.content.GetProcessor
            typeof(System.Func<XLua.LuaTable>),             // Bridge.data.ListMaps（开房选图下拉目录）
            typeof(System.Func<string, XLua.LuaTable>),     // Bridge.ui.GetLogic
            typeof(System.Func<string, object, object, object, object, string>),   // Bridge.text.Format
            typeof(System.Action<string, XLua.LuaTable>),   // Bridge.ui.Show/Replace(path, dataTable)
            typeof(System.Action<XLua.LuaFunction>),        // Bridge.meta 回调形（GuestLogin/ListRooms/QuickEnter/ListRecords）
            typeof(System.Action<string, XLua.LuaFunction>),// Bridge.meta.EnterRoom(roomId, cb)
            typeof(System.Action<int, int, XLua.LuaFunction>),// Bridge.meta.CreateRoom(mapId, expectedPlayers, cb)
                                                              // （开房参数批：旧 Action<int, LuaFunction> 形此前未登记——本次随三参形一并补齐）
        };
    }
}
