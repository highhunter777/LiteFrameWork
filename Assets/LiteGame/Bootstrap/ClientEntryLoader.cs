using System;

namespace LiteGame
{
    /// <summary>
    /// 入口装载器（AOT 侧唯一反射点）：按程序集限定名解析 <see cref="IClientEntry"/> 实现。
    /// 今天用 <see cref="Type.GetType(string)"/>；B6 改成 <c>Assembly.Load("LiteGame.App")</c> + 同名字符串，
    /// 接口与调用序不变（方案 (c)：hclr 唯一装载点）。
    /// 入口类型名可解析性由 EditMode 守卫真解析一次；IL2CPP 侧由 link.xml preserve <c>LiteGame.App</c>。
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
