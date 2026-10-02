using System.Collections.Generic;
using XLua;

namespace LiteGame.Editor
{
    /// <summary>
    /// xLua 生成配置：**业务类型（Bridge）不进 LuaCallCSharp**——
    /// 生成代码落在 xLuaMain 内，包 wrapper 需要业务类型就得让 xLuaMain 引 LiteClient.Runtime，
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
            typeof(System.Action),
            typeof(System.Action<string>),
            typeof(System.Func<int, XLua.LuaTable>),        // Bridge.data 查表适配（LuaComponent 注册）
            typeof(System.Action<int>),                     // Bridge.ui.Close
            typeof(System.Action<int, XLua.LuaTable>),      // Bridge.ui.Show(id, dataTable)
            typeof(System.Func<int, bool>),                 // Bridge.ui.IsOpen
        };
    }
}
