using System.Threading;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 注册表填充端口（模块侧契约）：读注册表三件套逐行 require/校验/填充，
    /// 汇总为 <see cref="RegistryFillReport"/>（整批跑完统一判定）。实现归 Lua 桥适配器；
    /// Preload 与 DevReload 走同一条填充路径。
    /// </summary>
    public interface ILuaRegistryFiller
    {
        /// <summary>整批填充（同步——require 走 env 同步签名；调用契约：config.LoadAsync 与 lua.DoMain 已完成）。</summary>
        RegistryFillReport FillAll(CancellationToken ct = default);
    }
}