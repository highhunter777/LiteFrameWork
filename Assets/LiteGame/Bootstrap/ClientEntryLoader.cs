using System;

namespace LiteGame
{
    /// <summary>
    /// 入口装载器（AOT 侧唯一反射点）：按程序集限定名解析 IClientEntry 实现。
    /// </summary>
    public static class ClientEntryLoader
    {
        /// <summary>入口类型程序集限定名（与 <c>LiteGame.App</c> 的 <c>ClientEntry</c> 单源）。</summary>
        public const string EntryTypeName = "LiteGame.ClientEntry, LiteGame.App";

        /// <summary>解析并实例化入口；缺失/未实现契约即显性失败（不静默降级为“空启动”）。</summary>
        public static IClientEntry Load()
        {
            Type type = Type.GetType(EntryTypeName, throwOnError: false);
            if (type == null)
                throw new InvalidOperationException($"入口类型无法解析：{EntryTypeName}（检查 LiteGame.App 是否参与构建）");
            if (!typeof(IClientEntry).IsAssignableFrom(type))
                throw new InvalidOperationException($"入口类型未实现 IClientEntry：{type.FullName}");
            return (IClientEntry)Activator.CreateInstance(type);
        }
    }
}
